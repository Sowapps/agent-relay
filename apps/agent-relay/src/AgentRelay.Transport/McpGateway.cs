using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Protocol;

namespace AgentRelay.Transport;

public sealed record McpTransferState(string Selection, string StateDirectory, string GatewayIdentity,
    JsonNode? Initialization, string? PendingFrame, byte[] Unread, Guid InstanceId, Guid? BindingId = null);

/// <summary>Serialized stdio ownership and worker replacement. No application routing or credentials live here.</summary>
public sealed class McpGateway : IAsyncDisposable
{
    internal static string Version { get; } = typeof(McpGateway).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
    private readonly string selectionPath, stateDirectory;
    private readonly Stream output;
    private readonly McpFrames input;
    private readonly Guid instance;
    private readonly Guid? binding;
    private string? pending, selectedText, gatewayIdentity, workerIdentity;
    private JsonNode? initialization;
    private McpProcessSelection? selection;
    private Process? worker;
    private McpFrames? responses;
    private Task? workerErrors;
    private DateTimeOffset lastPresence;
    private string? failedGateway;
    private string? pollError;
    private bool relinquished;
    private readonly DateTimeOffset started = McpProcessIdentity.StartedAt;
    private sealed class SelectionChanged : Exception;
    private sealed class CloseRequested : Exception;

    public McpGateway(string selectionPath, string stateDirectory, Stream input, Stream output, McpTransferState? transfer = null)
    {
        this.selectionPath = Path.GetFullPath(selectionPath);
        this.stateDirectory = Path.GetFullPath(stateDirectory);
        this.output = output;
        this.input = new(input, transfer?.Unread);
        instance = transfer?.InstanceId ?? Guid.NewGuid();
        binding = transfer?.BindingId;
        initialization = transfer?.Initialization?.DeepClone();
        pending = transfer?.PendingFrame;
        gatewayIdentity = transfer?.GatewayIdentity;
    }

    public async Task RunAsync()
    {
        while (true)
        {
            JsonNode? id = null;
            string? method = null;
            var forwarded = false;
            try
            {
                var frame = pending ?? await input.ReadAsync();
                pending = null;
                if (frame is null) return;
                var request = JsonNode.Parse(frame) as JsonObject ?? throw new JsonException("Expected a JSON-RPC object.");
                id = request["id"]?.DeepClone();
                method = request["method"]?.GetValue<string>();
                if (request["jsonrpc"]?.GetValue<string>() != "2.0" || method is null) throw new JsonException("Expected JSON-RPC 2.0 method.");
                var next = ReadSelection();
                var nextGateway = McpProcessSelections.Identity(next.Gateway);
                gatewayIdentity ??= nextGateway;
                if (nextGateway != gatewayIdentity)
                {
                    if (failedGateway == nextGateway) throw new McpProtocolException("transport_update_failed", "This successor already failed readiness; correct its selected bundle before retrying.");
                    var transfer = new McpTransferState(selectionPath, stateDirectory, nextGateway,
                        initialization?.DeepClone(), frame, input.ExportUnread(), instance, binding);
                    if (await McpSuccession.TransferAsync(next.Gateway, transfer, DropWorkerAsync)) { relinquished = true; return; }
                    failedGateway = nextGateway;
                    throw new McpProtocolException("transport_update_failed", "The successor did not become ready. This request was not forwarded; inspect transport diagnostics.");
                }
                if (id is null) continue;
                object? result;
                switch (method)
                {
                    case "initialize":
                        initialization = request["params"]?.DeepClone();
                        result = new { protocolVersion = initialization?["protocolVersion"]?.GetValue<string>() ?? "2025-11-25",
                            capabilities = new { tools = new { }, resources = new { subscribe = false } },
                            serverInfo = new { name = "agent-relay", version = Version },
                            instructions = "Read agent-relay://docs/quickstart and agent-relay://docs/tools. Use discover_operations for current schemas and authorized context, then invoke_operation with an explicit revision. call_id is correlation only; retain business request_id and never replay uncertain work." };
                        break;
                    case "tools/list": result = new { tools = McpStableProtocol.Tools }; break;
                    case "ping": result = new { }; break;
                    default:
                        await EnsureWorkerAsync(next.Worker);
                        if (method == "tools/call")
                        {
                            var parameters = request["params"] as JsonObject ?? throw new JsonException("Missing tool parameters.");
                            var meta = parameters["_meta"] as JsonObject ?? new JsonObject();
                            meta["agentRelayWireHash"] = McpStableProtocol.WireHash;
                            parameters["_meta"] = meta.DeepClone();
                        }
                        forwarded = true;
                        await McpFrames.WriteAsync(worker!.StandardInput.BaseStream, request.ToJsonString(McpProcessSelections.Json));
                        var response = JsonNode.Parse(await responses!.ReadAsync().WaitAsync(TimeSpan.FromSeconds(205))
                            ?? throw new IOException("Worker closed before its reply.")) as JsonObject ?? throw new JsonException("Invalid worker reply.");
                        if (!JsonNode.DeepEquals(id, response["id"])) throw new IOException("Worker correlation mismatch.");
                        await McpFrames.WriteAsync(output, response.ToJsonString(McpProcessSelections.Json));
                        continue;
                }
                await McpFrames.WriteAsync(output, JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result }, McpProcessSelections.Json));
            }
            catch (CloseRequested) { return; }
            catch (SelectionChanged)
            {
                var next = ReadSelection();
                var identity = McpProcessSelections.Identity(next.Gateway);
                var transfer = new McpTransferState(selectionPath, stateDirectory, identity, initialization?.DeepClone(), null,
                    input.ExportUnread(), instance, binding);
                try { if (await McpSuccession.TransferAsync(next.Gateway, transfer, DropWorkerAsync)) { relinquished = true; return; } }
                catch (Exception error) { Record(error); }
                failedGateway = identity;
                Record(new McpProtocolException("transport_update_failed", "Idle successor did not acknowledge readiness; original stream ownership retained."));
            }
            catch (Exception error)
            {
                try { await DropWorkerAsync(); }
                catch (Exception cleanup) { Record(cleanup); }
                var code = forwarded ? "mcp_transport_lost" : error is McpProtocolException protocol ? protocol.Code : "transport_unavailable";
                var diagnostic = Record(error);
                var message = forwarded ? "Worker response lost; execution is unconfirmed. Inspect retained results; never replay automatically."
                    : "Transport request refused before forwarding. Inspect the diagnostic and selected installation.";
                var failure = new { isError = true, structuredContent = new { error = new { code, message,
                    execution = forwarded ? "unconfirmed" : "not_started", recovery = "inspect_before_retry", diagnosticId = diagnostic.Id,
                    diagnosticStorageFailed = diagnostic.Failed } }, content = new[] { new { type = "text", text = message + " [" + code + "]" } } };
                await McpFrames.WriteAsync(output, method == "tools/call"
                    ? JsonSerializer.Serialize(new { jsonrpc = "2.0", id, result = failure }, McpProcessSelections.Json)
                    : JsonSerializer.Serialize(new { jsonrpc = "2.0", id, error = new { code = -32000, message, data = failure.structuredContent.error } }, McpProcessSelections.Json));
            }
        }
    }

    public async Task PollAsync()
    {
        try
        {
            if (McpTransportPresences.CloseRequested(stateDirectory, instance)) throw new CloseRequested();
            var selected = ReadSelection();
            var identity = McpProcessSelections.Identity(selected.Gateway);
            if (identity != gatewayIdentity && identity != failedGateway && input.CanTransfer) throw new SelectionChanged();
            if (workerIdentity is not null && workerIdentity != McpProcessSelections.Identity(selected.Worker)) await DropWorkerAsync();
            if (DateTimeOffset.UtcNow - lastPresence > TimeSpan.FromSeconds(1))
            {
                McpTransportPresences.Write(stateDirectory, new(instance, Environment.ProcessId, binding, Version, gatewayIdentity!, workerIdentity,
                    McpProcessSelections.Identity(selected.Worker), identity == gatewayIdentity ? "current" : "update_failed", DateTimeOffset.UtcNow, input.BufferedBytes, started,
                    Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR"), ClientName()));
                lastPresence = DateTimeOffset.UtcNow;
            }
            pollError = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or McpProtocolException)
        {
            var key = error.GetType().Name + error.Message;
            if (pollError != key) { Record(error); pollError = key; }
        }
    }

    private McpProcessSelection ReadSelection()
    {
        McpPrivateFiles.Demand(selectionPath);
        if (new FileInfo(selectionPath).Length > McpWireLimits.MaximumFrameBytes) throw new IOException("Selection exceeds its limit.");
        var text = McpPrivateFiles.ReadText(selectionPath);
        if (text != selectedText)
        {
            selection = McpProcessSelections.Parse(text);
            selectedText = text;
        }
        return selection!;
    }

    private string ClientName()
    {
        var name = initialization?["clientInfo"]?["name"]?.GetValue<string>() ?? "MCP client";
        return new string(name.Where(character => !char.IsControl(character)).Take(200).ToArray());
    }

    private async Task EnsureWorkerAsync(McpProcessImage image)
    {
        var key = McpProcessSelections.Identity(image);
        if (workerIdentity == key && worker is { HasExited: false }) return;
        await DropWorkerAsync();
        McpProcessSelections.Validate(image);
        var start = new ProcessStartInfo(image.Executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in image.Arguments) start.ArgumentList.Add(argument);
        if (binding is { } connection) { start.ArgumentList.Add("--connection-id"); start.ArgumentList.Add(connection.ToString("D")); }
        start.Environment["AGENT_RELAY_MCP_INSTANCE"] = instance.ToString("D");
        start.Environment["AGENT_RELAY_MCP_BROKER_VERSION"] = Version;
        worker = Process.Start(start) ?? throw new IOException("Worker could not start.");
        workerErrors = DrainErrorsAsync(worker.StandardError);
        responses = new(worker.StandardOutput.BaseStream);
        await McpFrames.WriteAsync(worker.StandardInput.BaseStream, JsonSerializer.Serialize(new { jsonrpc = "2.0", id = -700, method = "initialize",
            @params = initialization ?? new JsonObject { ["protocolVersion"] = "2025-11-25", ["clientInfo"] = new JsonObject { ["name"] = "Agent Relay transport", ["version"] = "1" }, ["capabilities"] = new JsonObject() } }, McpProcessSelections.Json));
        var reply = JsonNode.Parse(await responses.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10)) ?? throw new IOException("Worker initialization failed."));
        if (reply?["id"]?.GetValue<int>() != -700 || reply["result"]?["_meta"]?["agentRelay"]?["compatibility"]?["transport"]?.Deserialize<McpTransportRequirement>(McpProcessSelections.Json) != McpStableProtocol.Requirement)
            throw new McpProtocolException("transport_incompatible", "Selected worker did not acknowledge the required stable protocol.");
        await McpFrames.WriteAsync(worker.StandardInput.BaseStream, "{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
        workerIdentity = key;
    }

    private async Task DrainErrorsAsync(StreamReader errors)
    {
        // Drain bounded chunks without persisting arbitrary worker output (which may contain payloads/secrets).
        var chars = new char[4096];
        try { while (await errors.ReadAsync(chars) > 0) { } }
        catch (IOException error) { Record(error); }
    }

    private async Task DropWorkerAsync()
    {
        if (worker is null) return;
        try
        {
            if (!worker.HasExited) worker.Kill();
            await worker.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (workerErrors is not null) await workerErrors.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { worker.Dispose(); worker = null; responses = null; workerIdentity = null; workerErrors = null; }
    }

    private (string Id, bool Failed) Record(Exception error)
    {
        var id = Guid.NewGuid().ToString("N");
        try
        {
            var path = Path.Combine(stateDirectory, "diagnostics", id + ".json");
            McpPrivateFiles.Demand(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(new { id, time = DateTimeOffset.UtcNow, type = error.GetType().Name, error.Message }, McpProcessSelections.Json));
            return (id, false);
        }
        catch (Exception storage) when (storage is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Agent Relay diagnostic storage failed: " + id + " (" + storage.GetType().Name + ")");
            return (id, true);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DropWorkerAsync();
        if (!relinquished && selection is { } selected)
            McpTransportPresences.Write(stateDirectory, new(instance, Environment.ProcessId, binding, Version, gatewayIdentity!, null,
                McpProcessSelections.Identity(selected.Worker), "closed", DateTimeOffset.UtcNow, ProcessStartedAt: started));
    }
}
