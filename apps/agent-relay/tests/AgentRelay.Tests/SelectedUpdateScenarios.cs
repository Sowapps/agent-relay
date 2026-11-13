using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Protocol;

internal static class SelectedUpdateScenarios
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public static void Run(string root)
    {
        var nominal = Seed(Path.Combine(root, "nominal"));
        var runtime = new Runtime(nominal.Root);
        var update = new InstallationUpdate(nominal.Installation, nominal.Data, runtime);
        var prepared = update.Run("prepare", nominal.Candidate);
        Check(prepared.Schema == 2 && update.Run("prepare", nominal.Candidate).Id == prepared.Id, "Idempotent selected preparation");
        Check(InstallationUpdateRecovery.Start(nominal.Data, authorize: false) is null && !File.Exists(Path.Combine(nominal.Data, "promotion.lock")),
            "A prepared artifact silently authorized deployment through recovery");
        var store = new SqliteWorkStore(new RelayDatabase(nominal.Data));
        var work = new WorkRecord(new(Guid.NewGuid(), "fixture", "command", ""), "pending", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        store.SaveAsync(work, CancellationToken.None).GetAwaiter().GetResult();
        Check(update.Run("resume").Phase == "awaiting_work" && runtime.Load().Stops == 0, "Busy update stopped active work");
        store.SaveAsync(work with { State = "unknown" }, CancellationToken.None).GetAwaiter().GetResult();
        Check(update.Run("resume").Phase == "awaiting_work", "Uncertain work was ignored");
        store.SaveAsync(work with { State = "completed" }, CancellationToken.None).GetAwaiter().GetResult();
        var complete = update.Run("resume");
        Check(complete.Phase == "complete" && !File.Exists(Path.Combine(nominal.Data, "promotion.lock")), "Complete update did not release its own barrier");
        var starts = runtime.Load().Starts;
        Check(update.Run("resume").Phase == "complete" && runtime.Load().Starts == starts, "Completed update restarted host");
        Check(File.ReadAllText(Path.Combine(nominal.Installation, "app", "AgentRelay.Windows.exe")) == "old-host", "Loaded old payload was overwritten");
        Check(File.ReadAllText(Path.Combine(nominal.Installation, "README.md")) == "new docs", "Root docs not updated");
        Check(File.ReadAllBytes(Path.Combine(nominal.Data, "connections", "fixture.bin")).SequenceEqual(new byte[] { 1, 2, 3 }), "Credentials changed");
        Refuses("update_recovery_required", () => update.Run("rollback"));
        var completedJournal = Path.Combine(RuntimePaths.ApplicationDirectory(nominal.Installation), "update", "current.json");
        var reopening = JsonNode.Parse(File.ReadAllText(completedJournal))!;
        reopening["phase"] = "reopening";
        File.WriteAllText(completedJournal, reopening.ToJsonString());
        var newWork = work with { Request = work.Request with { Id = Guid.NewGuid() }, State = "completed" };
        store.SaveAsync(newWork, CancellationToken.None).GetAwaiter().GetResult();
        Check(update.Run("resume").Phase == "complete" && store.FindAsync(newWork.Request.Id, CancellationToken.None).GetAwaiter().GetResult() is not null,
            "Reopening recovery overwrote admitted work or required a missing barrier");

        var pending = Seed(Path.Combine(root, "transports"));
        var old = McpProcessSelections.Read(RuntimePaths.TransportSelection(pending.Data));
        var instance = Guid.NewGuid();
        void Presence(McpProcessSelection selected) => McpTransportPresences.Write(RuntimePaths.TransportStateDirectory(pending.Data),
            new(instance, Environment.ProcessId, null, "fixture", McpProcessSelections.Identity(selected.Gateway), null,
                McpProcessSelections.Identity(selected.Worker), "current", DateTimeOffset.UtcNow));
        Presence(old);
        var stale = McpTransportPresences.Read(RuntimePaths.TransportStateDirectory(pending.Data)).Single();
        McpTransportPresences.Write(RuntimePaths.TransportStateDirectory(pending.Data), stale with { SeenAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
        Check(McpTransportPresences.Read(RuntimePaths.TransportStateDirectory(pending.Data)).Single().State == "unresponsive", "A live quiet/busy transport was discarded as offline");
        var port = new Runtime(pending.Root);
        var waiting = new InstallationUpdate(pending.Installation, pending.Data, port);
        waiting.Run("prepare", pending.Candidate);
        Check(waiting.Run("resume").Phase == "awaiting_transports" && File.Exists(Path.Combine(pending.Data, "promotion.lock")), "Admission reopened before transport acknowledgement");
        Presence(McpProcessSelections.Read(RuntimePaths.TransportSelection(pending.Data)));
        Check(waiting.Run("resume").Phase == "complete" && port.Load().Starts == 1, "Acknowledgement resume failed or restarted host");

        var recovery = Seed(Path.Combine(root, "rollback"));
        var broken = new Runtime(recovery.Root) { FailCandidate = true };
        var retired = Seed(Path.Combine(root, "retired-transport"));
        var retiredOld = McpProcessSelections.Read(RuntimePaths.TransportSelection(retired.Data));
        var retiredId = Guid.NewGuid();
        var retiredPresence = new McpTransportPresence(retiredId, Environment.ProcessId, null, "fixture", McpProcessSelections.Identity(retiredOld.Gateway), null,
            McpProcessSelections.Identity(retiredOld.Worker), "current", DateTimeOffset.UtcNow);
        McpTransportPresences.Write(RuntimePaths.TransportStateDirectory(retired.Data), retiredPresence);
        var retireUpdate = new InstallationUpdate(retired.Installation, retired.Data, new Runtime(retired.Root));
        retireUpdate.Run("prepare", retired.Candidate);
        Check(retireUpdate.Run("resume").Phase == "awaiting_transports", "Live old transport bypassed acknowledgement");
        McpTransportPresences.Write(RuntimePaths.TransportStateDirectory(retired.Data), retiredPresence with { State = "closed" });
        Check(retireUpdate.Run("resume").Phase == "complete", "A safely closed transport blocked completion forever");
        var rollback = new InstallationUpdate(recovery.Installation, recovery.Data, broken);
        var pointerBefore = File.ReadAllText(Path.Combine(recovery.Data, "mcp-runtime.json"));
        rollback.Run("prepare", recovery.Candidate);
        try { rollback.Run("resume"); throw new Exception("Bad readiness accepted"); }
        catch (IOException error) when (error.Message == "candidate readiness fixture") { }
        Check(rollback.Run("status").Phase == "rolled_back" && File.ReadAllText(Path.Combine(recovery.Installation, "README.md")) == "old docs", "Rollback did not restore root docs");
        Check(File.ReadAllText(Path.Combine(recovery.Data, "mcp-runtime.json")) == pointerBefore && !File.Exists(Path.Combine(recovery.Data, "promotion.lock")), "Rollback did not restore coherent selection/admission");
        Check(rollback.Run("rollback").Phase == "rolled_back", "Rollback repetition failed");

        var cancel = Seed(Path.Combine(root, "cancel"));
        var cancelPort = new Runtime(cancel.Root);
        var cancelled = new InstallationUpdate(cancel.Installation, cancel.Data, cancelPort);
        new SqliteWorkStore(new RelayDatabase(cancel.Data)).SaveAsync(work, CancellationToken.None).GetAwaiter().GetResult();
        cancelled.Run("prepare", cancel.Candidate);
        Check(cancelled.Run("resume").Phase == "awaiting_work", "Cancellation fixture was not waiting for work");
        Check(cancelled.Run("rollback").Phase == "rolled_back" && cancelPort.Load().Stops == 0, "Cancelling an unstarted update stopped working host");

        var conflict = Seed(Path.Combine(root, "conflict"));
        var conflictPort = new Runtime(conflict.Root) { ConflictOnReady = true };
        var conflicted = new InstallationUpdate(conflict.Installation, conflict.Data, conflictPort);
        conflicted.Run("prepare", conflict.Candidate);
        try { conflicted.Run("resume"); throw new Exception("Unrecognized selection overwritten"); }
        catch (AggregateException) { }
        Check(File.ReadAllText(RuntimePaths.TransportSelection(conflict.Data)) == "{}" && File.Exists(Path.Combine(conflict.Data, "promotion.lock")),
            "Recovery overwrote an external selection or released its barrier");

        foreach (var phase in new[] { "after-stop", "after-select", "reopening" })
        {
            var interrupted = Seed(Path.Combine(root, phase));
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "AgentRelay.Tests.exe")) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "selected-update-crash", interrupted.Root, phase }) start.ArgumentList.Add(argument);
            using var child = Process.Start(start) ?? throw new Exception("Crash fixture failed to start");
            child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            Check(child.ExitCode == 91, "Fixture did not exit at requested real process boundary: " + child.StandardError.ReadToEnd());
            var resumed = new InstallationUpdate(interrupted.Installation, interrupted.Data, new Runtime(interrupted.Root));
            Check(resumed.Run("resume").Phase == "complete", "Interrupted transaction did not resume: " + phase);
            Check(new Runtime(interrupted.Root).Load().Starts == 1, "Interrupted resume repeated host start: " + phase);
        }
        Console.WriteLine("SELECTED_INSTALLATION_UPDATE=PASS (busy/unknown drain, immutable slots, transport acknowledgement barrier, repeated no-op, coherent rollback, real process-exit resume; runtime adapter fixture, no native host/model)");
    }

    public static void Crash(string root, string phase)
    {
        var paths = Paths(root);
        var runtime = new Runtime(root) { CrashAt = phase };
        var update = new InstallationUpdate(paths.Installation, paths.Data, runtime);
        update.Run("prepare", paths.Candidate);
        update.Run("resume");
        throw new Exception("Crash boundary not reached");
    }

    private sealed record Fixture(string Root, string Installation, string Data, string Candidate);
    private static Fixture Paths(string root) => new(root, Path.Combine(root, "installation"), RuntimePaths.PortableDataDirectory(Path.Combine(root, "installation")), Path.Combine(root, "candidate"));
    private static Fixture Seed(string root)
    {
        var value = Paths(root);
        Directory.CreateDirectory(Path.Combine(value.Installation, "app"));
        Directory.CreateDirectory(Path.Combine(value.Installation, "transport"));
        Directory.CreateDirectory(Path.Combine(value.Candidate, "app"));
        Directory.CreateDirectory(Path.Combine(value.Candidate, "transport"));
        new RelayDatabase(value.Data);
        Directory.CreateDirectory(Path.Combine(value.Data, "connections"));
        File.WriteAllBytes(Path.Combine(value.Data, "connections", "fixture.bin"), [1, 2, 3]);
        File.WriteAllText(Path.Combine(value.Data, "relay-projects.json"), "fixture unchanged permissions");
        File.WriteAllText(Path.Combine(value.Installation, "README.md"), "old docs");
        File.WriteAllText(Path.Combine(value.Installation, "app", "AgentRelay.Windows.exe"), "old-host");
        File.WriteAllText(Path.Combine(value.Installation, "transport", "AgentRelay.Transport.exe"), "old-gateway");
        var compatibility = new McpContractCompatibility(McpStableProtocol.Requirement, McpToolCatalog.OperationsHash, new string('c', 64));
        File.WriteAllText(Path.Combine(value.Installation, "app", "mcp-contract.json"), RelayJson.Serialize(new McpIdentity("0.0.1-rc.1", new string('b', 64), new string('d', 64)) { Compatibility = compatibility }));
        File.WriteAllText(Path.Combine(value.Installation, "app", "mcp-operations.json"), RelayJson.Serialize(McpToolCatalog.Manifest));
        var previous = new McpProcessSelection(1, McpStableProtocol.Requirement,
            Image(Path.Combine(value.Installation, "app"), "AgentRelay.Windows.exe"), Image(Path.Combine(value.Installation, "transport"), "AgentRelay.Transport.exe"));
        File.WriteAllText(RuntimePaths.TransportSelection(value.Data), RelayJson.Serialize(previous));
        File.WriteAllText(Path.Combine(value.Data, "mcp-runtime.json"), RelayJson.Serialize(new { executable = previous.Worker.Executable, distribution = "portable-win-x64", connectionId = Guid.NewGuid() }));
        File.WriteAllText(Path.Combine(value.Candidate, "app", "AgentRelay.Windows.exe"), "new-host");
        File.WriteAllText(Path.Combine(value.Candidate, "transport", "AgentRelay.Transport.exe"), "new-gateway");
        File.WriteAllText(Path.Combine(value.Candidate, "app", "mcp-contract.json"), RelayJson.Serialize(new McpIdentity("0.0.2-rc.1", new string('a', 64), new string('e', 64)) { Compatibility = compatibility }));
        File.WriteAllText(Path.Combine(value.Candidate, "app", "mcp-operations.json"), RelayJson.Serialize(McpToolCatalog.Manifest));
        File.WriteAllText(Path.Combine(value.Candidate, "README.md"), "new docs");
        var files = Directory.EnumerateFiles(value.Candidate, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(value.Candidate, path).Replace('\\', '/'), Hash);
        File.WriteAllText(Path.Combine(value.Candidate, "release-manifest.json"), RelayJson.Serialize(new { version = "0.0.2-rc.1", selfContained = true, files }));
        new Runtime(root).Save(new(previous.Worker.Executable, 0, 0));
        return value;
    }

    private static McpProcessImage Image(string root, string executable) => new(Path.Combine(root, executable), [], Directory.EnumerateFiles(root).ToDictionary(path => Path.GetFileName(path), Hash));
    private static void Refuses(string code, Action call)
    {
        try { call(); throw new Exception("Expected " + code); }
        catch (RelayException error) when (error.Code == code) { }
    }

    private sealed record RuntimeState(string? Running, int Starts, int Stops);
    private sealed class Runtime(string root) : IInstallationRuntime
    {
        public bool FailCandidate { get; init; }
        public bool ConflictOnReady { get; init; }
        public string? CrashAt { get; init; }
        private string PathForState => Path.Combine(root, "runtime.json");
        public RuntimeState Load() => RelayJson.Deserialize<RuntimeState>(File.ReadAllText(PathForState));
        public void Save(RuntimeState state) => File.WriteAllText(PathForState, RelayJson.Serialize(state));
        public InstallationHost? Inspect(string executable)
        {
            var state = Load();
            if (state.Running is null) return null;
            Check(state.Running == executable, "Runtime inspected through wrong selected executable");
            return new(123, DateTimeOffset.UnixEpoch, executable);
        }
        public void ConfirmDrain() { }
        public void Stop(InstallationHost host)
        {
            var state = Load(); Save(state with { Running = null, Stops = state.Stops + 1 });
            if (CrashAt == "after-stop") Environment.Exit(91);
        }
        public void Start(string executable)
        {
            var state = Load(); Save(state with { Running = executable, Starts = state.Starts + 1 });
            if (CrashAt == "after-select") Environment.Exit(91);
        }
        public void DemandReady(string executable, string version, string contractHash)
        {
            if (ConflictOnReady && version == "0.0.2-rc.1")
            {
                File.WriteAllText(RuntimePaths.TransportSelection(Paths(root).Data), "{}");
                throw new IOException("unexpected selection fixture");
            }
            if (CrashAt == "reopening" && JsonNode.Parse(File.ReadAllText(Path.Combine(RuntimePaths.ApplicationDirectory(Paths(root).Installation), "update", "current.json")))!["phase"]?.GetValue<string>() == "reopening") Environment.Exit(91);
            if (FailCandidate && version == "0.0.2-rc.1") throw new IOException("candidate readiness fixture");
        }
        public bool StartupEnabled(string executable) => true;
        public InstallationStartupSnapshot CaptureStartup(string executable) => new(true, "{}");
        public void ReconcileStartup(string executable, InstallationStartupSnapshot before, bool restore) { }
    }
}
