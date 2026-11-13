using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record InstallationHost(int ProcessId, DateTimeOffset StartedAt, string Executable);
public sealed record InstallationStartupSnapshot(bool Enabled, string Capture);

/// <summary>OS/host adapter for the existing transaction. Decisions and durable phases remain in InstallationUpdate.</summary>
public interface IInstallationRuntime
{
    InstallationHost? Inspect(string executable);
    void ConfirmDrain();
    void Stop(InstallationHost host);
    void Start(string executable);
    void DemandReady(string executable, string version, string contractHash);
    bool StartupEnabled(string executable);
    InstallationStartupSnapshot CaptureStartup(string executable);
    void ReconcileStartup(string executable, InstallationStartupSnapshot before, bool restore);
}

internal sealed class InstallationRuntime(string data) : IInstallationRuntime
{
    private static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();
    private McpHostStatus Handshake()
    {
        var pointer = JsonNode.Parse(File.ReadAllText(Path.Combine(data, "mcp-runtime.json")))!;
        var binding = Guid.Parse(pointer["connectionId"]!.GetValue<string>());
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        return Wait(new RelayPipeClient(data, binding).RequestAsync(new("mcp_handshake"), timeout.Token)).Mcp
            ?? throw new IOException("Host did not return authenticated identity.");
    }

    public InstallationHost? Inspect(string executable)
    {
        try { using var owner = RelayLocation.OpenHostOwnership(data); return null; }
        catch (IOException error) when (RelayLocation.IsSharingConflict(error)) { }
        var status = Handshake();
        if (status.ProcessId is not { } pid || status.ProcessStartedAt is not { } started)
            throw new RelayException("update_host_identity_required", "The running host must expose its authenticated process identity before automatic cutover.", false);
        var host = new InstallationHost(pid, started, Path.GetFullPath(executable));
        using var process = DemandProcess(host);
        return host;
    }

    public void ConfirmDrain()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Wait(new RelayPipeClient(data).RequestAsync(new("deployment_quiesce"), timeout.Token));
    }

    public void Stop(InstallationHost host)
    {
        using var process = DemandProcess(host);
        process.Kill();
        process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
    }

    private static Process DemandProcess(InstallationHost host)
    {
        var process = Process.GetProcessById(host.ProcessId);
        if (new DateTimeOffset(process.StartTime.ToUniversalTime()) != host.StartedAt ||
            !string.Equals(process.MainModule?.FileName, host.Executable, StringComparison.OrdinalIgnoreCase))
        {
            process.Dispose();
            throw new RelayException("update_host_identity_changed", "The authenticated host process changed; no process was stopped.", false);
        }
        return process;
    }

    public void Start(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (var argument in new[] { "tray", "--data-dir", data }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Selected host could not start.");
    }

    public void DemandReady(string executable, string version, string contractHash)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                var host = Inspect(executable) ?? throw new IOException("Selected host is not listening yet.");
                var status = Handshake();
                if (status.Identity.Version != version || status.Identity.ContractHash != contractHash)
                    throw new RelayException("runtime_contract_mismatch", "The selected host does not match the complete candidate.", false);
                return;
            }
            catch (Exception error) when ((error is IOException || error is RelayException { Code: "relay_unavailable" or "relay_timeout" }) && deadline.Elapsed < TimeSpan.FromSeconds(12))
            { Thread.Sleep(100); }
        }
    }

    public bool StartupEnabled(string executable)
    {
        var value = Command(executable, "startup-status");
        if (value.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
            throw new IOException("Startup preference inspection reported an error.");
        return value.GetProperty("enabled").GetBoolean();
    }

    public InstallationStartupSnapshot CaptureStartup(string executable)
    {
        var capture = Command(executable, "startup-entry");
        return new(capture.GetProperty("entry").ValueKind != JsonValueKind.Null, capture.GetRawText());
    }

    public void ReconcileStartup(string executable, InstallationStartupSnapshot before, bool restore)
    {
        var current = Command(executable, "startup-entry");
        using var document = JsonDocument.Parse(before.Capture);
        var entry = current.GetProperty("entry");
        var desired = restore ? document.RootElement.GetProperty("entry") : before.Enabled ? current.GetProperty("enabledEntry") : JsonSerializer.SerializeToElement<object?>(null);
        if (JsonElement.DeepEquals(entry, desired)) return;
        var previous = document.RootElement.GetProperty("entry");
        if (!JsonElement.DeepEquals(entry, previous) && !(restore && JsonElement.DeepEquals(entry, current.GetProperty("enabledEntry"))))
            throw new IOException("The startup entry changed outside this transaction; no registry value was overwritten.");
        var action = desired.ValueKind == JsonValueKind.Null ? "disable" : restore ? "restore" : "enable";
        Command(executable, "startup-entry", RelayJson.Serialize(new { expected = entry, action, entry = action == "restore" ? desired : JsonSerializer.SerializeToElement<object?>(null) }));
    }

    private JsonElement Command(string executable, string mode, string? input = null)
    {
        var arguments = new List<string> { mode, "--data-dir", data };
        if (input is not null) arguments.Add("--apply");
        var result = LocalProcess.RunAsync(executable, arguments, input, TimeSpan.FromSeconds(12), CancellationToken.None).GetAwaiter().GetResult();
        if (result.ExitCode != 0) throw new IOException("Owned startup command failed: " + result.Error);
        return JsonSerializer.Deserialize<JsonElement>(result.Output);
    }
}
