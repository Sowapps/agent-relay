using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Retained stdio front door: switches workers only between requests, never resends an uncertain request.
public sealed class McpBroker : IAsyncDisposable
{
    private readonly string initialData;
    private readonly Guid? binding;
    private readonly Guid instance = Guid.NewGuid();
    private readonly object output = new();
    private readonly object admission = new();
    private readonly CancellationTokenSource stop = new();
    private Process? worker;
    private BoundedLines? responses;
    private JsonNode? initialization;
    private JsonNode? clientInitialization;
    private string? selected, catalog, notified;
    private string data, client = "Unspecified MCP client", state = "starting";
    private McpIdentity identity = McpIdentity.Current;
    private int busy;
    private McpBroker(string data, Guid? binding) { initialData = this.data = Path.GetFullPath(data); this.binding = binding; }

    public static async Task RunAsync(string data, Guid? binding)
    {
        await using var broker = new McpBroker(data, binding);
        await broker.Run();
    }
    private (string Data, string Executable, string? Assembly, McpIdentity? Identity) Resolve()
    {
        var scope = initialData;
        var root = RuntimePaths.InstallationRoot();
        for (var count = 0; File.Exists(Path.Combine(scope, "migrated-to.json")); count++)
        {
            if (count >= 4 || root is null) throw new RelayException("relay_migrated", "Reload the connection with the verified migrated data scope.", false);
            var destination = ReadMetadata(Path.Combine(scope, "migrated-to.json"), "relay_migrated")["destination"] is JsonValue nextValue && nextValue.TryGetValue<string>(out var nextText) ? nextText : null;
            if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination)) throw new RelayException("relay_migrated", "The migrated data scope needs a verified absolute destination. Reload the reviewed connection.", false);
            var next = Path.GetFullPath(destination);
            var owned = Path.Combine(root, "var", "apps", "agent-relay") + Path.DirectorySeparatorChar;
            if (!next.StartsWith(owned, StringComparison.OrdinalIgnoreCase) || next == scope) throw new RelayException("relay_migrated", "The migration destination cannot be verified.", false);
            scope = next;
        }
        var pointer = Path.Combine(scope, "mcp-runtime.json");
        if (!File.Exists(pointer))
        {
            // Public documentation remains readable before initialization. Tool calls still require a real credential/host.
            var assembly = Path.Combine(AppContext.BaseDirectory, "AgentRelay.Windows.dll");
            var executable = Environment.ProcessPath!;
            return (scope, executable, Path.GetFileName(executable).Equals("AgentRelay.Windows.exe", StringComparison.OrdinalIgnoreCase) ? null : assembly, null);
        }
        var value = ReadMetadata(pointer, "runtime_unconfigured");
        var executablePath = value["executable"] is JsonValue executableValue && executableValue.TryGetValue<string>(out var executableText) ? executableText : null;
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath)) throw new RelayException("runtime_unconfigured", "Runtime metadata needs an explicit native executable.", false);
        var target = Path.GetFullPath(executablePath);
        if (root is null || !target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).Equals("AgentRelay.Windows.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(target))
            throw new RelayException("runtime_unconfigured", "The selected runtime is outside this installation or is unavailable.", false);
        RuntimePaths.DemandPrivatePath(target);
        var manifest = Path.Combine(Path.GetDirectoryName(target)!, "mcp-contract.json");
        if (!File.Exists(manifest)) throw new RelayException("connection_outdated", "The selected runtime predates the verified MCP broker. Prepare the current installation first.", false);
        var contract = ReadMetadata(manifest, "runtime_unconfigured").Deserialize<McpIdentity>(RelayJson.Options)!;
        if (string.IsNullOrWhiteSpace(contract.Version) || contract.ContractHash?.Length != 64 || contract.Generation?.Length != 64)
            throw new RelayException("runtime_unconfigured", "Runtime contract metadata is invalid.", false);
        if (contract.Version == identity.Version && selected is not null && contract.ContractHash != identity.ContractHash)
            throw new RelayException("connection_outdated", "A different contract reused a deployed version. Select a new RC version.", false);
        return (scope, target, null, contract);
    }
    private static JsonObject ReadMetadata(string path, string code)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? throw new JsonException(); }
        catch (JsonException) { throw new RelayException(code, "The relay installation/migration metadata is malformed. Repair the reviewed installation; do not recreate its database.", false); }
    }
    private string Key((string Data, string Executable, string? Assembly, McpIdentity? Identity) runtime) => runtime.Data + "|" + runtime.Executable + "|" + runtime.Assembly + "|" + runtime.Identity?.Generation;
    private void Send(JsonNode value) { lock (output) Console.WriteLine(value.ToJsonString(RelayJson.Options)); }
    private void Save()
    {
        if (binding is null || !File.Exists(Path.Combine(data, "connections", binding.Value.ToString("N") + ".bin"))) return;
        McpConnections.Write(data, new(instance, binding, client, McpIdentity.Current.Version, identity.Version, identity.ContractHash, identity.Generation, catalog, state, DateTimeOffset.UtcNow));
    }
    private async Task Monitor()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stop.Token))
            {
                if (McpConnections.CloseRequested(data, instance))
                {
                    lock (admission)
                    {
                        state = "closing";
                        if (Volatile.Read(ref busy) == 0) stop.Cancel();
                    }
                    if (stop.IsCancellationRequested) break;
                }
                else if (selected is not null)
                {
                    try
                    {
                        var next = Key(Resolve());
                        if (next != selected)
                        {
                            state = "update_pending";
                            if (notified != next)
                            {
                                Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/tools/list_changed" });
                                Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "notifications/resources/list_changed" });
                                notified = next;
                            }
                        }
                    }
                    catch (Exception error) when (error is RelayException or IOException or JsonException) { state = "outdated"; }
                }
                // Do not race the request thread's metadata/lease replacement.
                if (Volatile.Read(ref busy) == 0) Save();
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
    }
    private async Task Run()
    {
        var monitor = Monitor(); var input = new BoundedLines(Console.In, PipeFrames.MaximumBytes);
        try
        {
            while (!stop.IsCancellationRequested)
            {
                JsonNode? id = null; string? method = null; var forwarded = false;
                try
                {
                    var line = await input.ReadAsync().WaitAsync(stop.Token); if (line is null) break;
                    lock (admission)
                    {
                        if (stop.IsCancellationRequested || McpConnections.CloseRequested(data, instance)) break;
                        Volatile.Write(ref busy, 1);
                    }
                    var request = JsonNode.Parse(line) ?? throw new JsonException("Missing request.");
                    if (request["id"] is null) continue;
                    id = request["id"]!.DeepClone(); method = request["method"]!.GetValue<string>();
                    if (method == "initialize")
                    {
                        clientInitialization = request["params"]?.DeepClone();
                        client = clientInitialization?["clientInfo"]?["name"]?.GetValue<string>() ?? client;
                        client = new string(client.Where(character => !char.IsControl(character)).Take(200).ToArray());
                    }
                    await EnsureWorker();
                    JsonNode response;
                    if (method == "initialize")
                    {
                        response = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = initialization!.DeepClone() };
                    }
                    else
                    {
                        if (method == "tools/call")
                        {
                            var parameters = request["params"] as JsonObject ?? throw new JsonException("Missing parameters.");
                            var meta = parameters["_meta"] as JsonObject ?? new JsonObject();
                            meta["agentRelayContract"] = catalog; parameters["_meta"] = meta.DeepClone();
                        }
                        forwarded = true;
                        await worker!.StandardInput.WriteLineAsync(request.ToJsonString(RelayJson.Options));
                        response = JsonNode.Parse(await responses!.ReadAsync().WaitAsync(TimeSpan.FromSeconds(190)) ?? throw new IOException("Worker closed."))!;
                        if (method == "tools/list" && response["result"] is not null) catalog = identity.ContractHash;
                    }
                    state = catalog == identity.ContractHash ? "current" : catalog is null ? "catalog_unconfirmed" : "client_reload_required";
                    Send(response); Save();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
                catch (Exception error)
                {
                    state = "offline";
                    var reported = forwarded && error is IOException or TimeoutException or JsonException
                        ? new RelayException("mcp_transport_lost", "The MCP worker response was lost. Inspect retained results; the request is never replayed automatically.", true) : error;
                    var failure = JsonSerializer.SerializeToNode(McpErrors.Result(reported, McpDiagnostics.Record(data, error)), RelayJson.Options)!;
                    if (method == "tools/call") Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = failure });
                    else Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = -32000, ["message"] = failure["content"]![0]!["text"]!.GetValue<string>() } });
                    DropWorker();
                }
                finally
                {
                    lock (admission)
                    {
                        Volatile.Write(ref busy, 0);
                        if (McpConnections.CloseRequested(data, instance)) stop.Cancel();
                    }
                }
            }
        }
        finally { stop.Cancel(); await monitor; state = "closed"; Save(); }
    }
    private async Task EnsureWorker()
    {
        var runtime = Resolve(); var key = Key(runtime);
        if (selected == key && worker is { HasExited: false }) return;
        DropWorker(); data = runtime.Data;
        var start = new ProcessStartInfo(runtime.Executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new System.Text.UTF8Encoding(false), StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8 };
        if (runtime.Assembly is not null) start.ArgumentList.Add(runtime.Assembly);
        start.ArgumentList.Add("mcp-worker"); start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(data);
        if (binding is { } id) { start.ArgumentList.Add("--connection-id"); start.ArgumentList.Add(id.ToString("D")); }
        start.Environment["AGENT_RELAY_MCP_INSTANCE"] = instance.ToString("D");
        start.Environment["AGENT_RELAY_MCP_BROKER_VERSION"] = McpIdentity.Current.Version;
        worker = Process.Start(start) ?? throw new IOException("Worker start failed.");
        worker.ErrorDataReceived += (_, args) => { if (!string.IsNullOrEmpty(args.Data)) McpDiagnostics.RecordText(data, args.Data); }; worker.BeginErrorReadLine();
        responses = new(worker.StandardOutput, PipeFrames.MaximumBytes);
        var parameters = clientInitialization?.DeepClone() ?? new JsonObject { ["protocolVersion"] = "2025-11-25", ["clientInfo"] = new JsonObject { ["name"] = client, ["version"] = "1" }, ["capabilities"] = new JsonObject() };
        await worker.StandardInput.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = -700, ["method"] = "initialize", ["params"] = parameters }.ToJsonString());
        var reply = JsonNode.Parse(await responses.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10)) ?? throw new IOException("Worker initialization failed."))!;
        initialization = reply["result"]?.DeepClone() ?? throw new RelayException("runtime_unconfigured", "The selected worker could not initialize.", false);
        identity = initialization["_meta"]!["agentRelay"]!.Deserialize<McpIdentity>(RelayJson.Options)!;
        if (runtime.Identity is not null && runtime.Identity != identity) throw new RelayException("runtime_unconfigured", "The selected worker does not match its qualified contract metadata.", false);
        await worker.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        selected = key;
    }
    private void DropWorker()
    {
        if (worker is not null) { try { if (!worker.HasExited) worker.Kill(); } catch (InvalidOperationException) { } worker.Dispose(); }
        worker = null; responses = null; selected = null;
    }
    public ValueTask DisposeAsync() { DropWorker(); stop.Dispose(); return ValueTask.CompletedTask; }
}
