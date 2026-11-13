using System.Diagnostics;
using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record InstallationRecoveryRunner(int Schema, string TransactionId, string Installation, string Data,
    string Executable, Dictionary<string, string> Files);
public sealed record InstallationRecoveryPresence(string TransactionId, int ProcessId, DateTimeOffset ProcessStartedAt,
    string State, DateTimeOffset SeenAt, string? DiagnosticId = null);

/// <summary>Resume only a retained authorized transaction; no archive selection, client restart or request replay.</summary>
public static class InstallationUpdateRecovery
{
    public static async Task<InstallationUpdateState?> RequestAsync(string data, Guid binding, string secret, CancellationToken token, bool start = true)
    {
        string journal;
        try { journal = RuntimePaths.UpdateJournal(data); }
        catch (IOException) { return null; } // Explicit development scopes do not have portable update transactions.
        if (!File.Exists(journal)) return null;
        await ConnectionStore.DemandStartupAsync(data, binding, secret, token);
        if (start) return Start(data, authorize: false);
        var state = new InstallationUpdate(RuntimePaths.PortableInstallationForData(data), data).Run("status");
        return (state.Phase is "complete" or "rolled_back" || state.Activation?.ResumeAuthorized != true) && !File.Exists(Path.Combine(data, "promotion.lock")) ? null : state;
    }

    public static InstallationUpdateState? Start(string data, bool authorize = true)
    {
        var root = RuntimePaths.PortableInstallationForData(data);
        var update = new InstallationUpdate(root, data);
        var state = update.Run("status");
        if (state.Phase is "complete" or "rolled_back" && !File.Exists(Path.Combine(data, "promotion.lock"))) return null;
        if (!authorize && state.Activation?.ResumeAuthorized != true) return null;
        if (state.Schema != 2) throw new RelayException("update_legacy_recovery_required", "The retained legacy transaction needs the relay maintainer's explicit recovery. No project work was admitted.", false);
        if (state.Phase == "preparing") throw new RelayException("update_candidate_incomplete", "The retained candidate stage is incomplete. Ask the relay maintainer to resume preparation from its reviewed artifact.", false);
        var runner = Runner(data, state.Id);
        if (authorize) state = update.Run("authorize");
        var directory = RuntimePaths.UpdateDirectory(root);
        using var ownership = new FileStream(Path.Combine(directory, "resume-launch.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var presence = Presence(data);
        if (presence is not null && presence.TransactionId == state.Id && Alive(presence, runner.Executable)) return state;
        var start = new ProcessStartInfo(runner.Executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        start.ArgumentList.Add("installation-resume"); start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(data);
        using var process = Process.Start(start) ?? throw new IOException("The retained recovery process could not start.");
        WritePresence(data, new(state.Id, process.Id, new(process.StartTime.ToUniversalTime()), "starting", DateTimeOffset.UtcNow));
        return state;
    }

    public static async Task<int> ContinueAsync(string data, CancellationToken token)
    {
        var root = RuntimePaths.PortableInstallationForData(data);
        var directory = RuntimePaths.UpdateDirectory(root);
        using var ownership = new FileStream(Path.Combine(directory, "resume.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var update = new InstallationUpdate(root, data);
        var state = update.Run("status");
        if (state.Activation?.ResumeAuthorized != true) throw new RelayException("update_not_authorized", "Preparation alone never authorizes automatic execution.", false);
        var runner = Runner(data, state.Id);
        if (!string.Equals(runner.Executable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new IOException("Continuation must run from the verified recovery image.");
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var current = update.Run("status");
                if (current.Id != state.Id) throw new IOException("The continuation does not own the newly selected transaction.");
                WritePresence(data, new(state.Id, Environment.ProcessId, AgentRelay.Protocol.McpProcessIdentity.StartedAt, "running", DateTimeOffset.UtcNow));
                state = update.Run(current.Phase == "rolled_back" ? "rollback" : "resume");
                if (state.Phase is "complete" or "rolled_back")
                {
                    WritePresence(data, new(state.Id, Environment.ProcessId, AgentRelay.Protocol.McpProcessIdentity.StartedAt, "complete", DateTimeOffset.UtcNow));
                    return 0;
                }
                if (state.ErrorCode is "update_uncertain_work" or "update_bootstrap_pending")
                {
                    WritePresence(data, new(state.Id, Environment.ProcessId, AgentRelay.Protocol.McpProcessIdentity.StartedAt, "needs_attention", DateTimeOffset.UtcNow));
                    return 2;
                }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
        }
        catch (Exception error)
        {
            var failure = InstallationUpdateDiagnostics.Capture(data, error);
            WritePresence(data, new(state.Id, Environment.ProcessId, AgentRelay.Protocol.McpProcessIdentity.StartedAt, "failed", DateTimeOffset.UtcNow, failure.DiagnosticId));
            return 1;
        }
    }

    public static InstallationRecoveryPresence? Presence(string data)
    {
        var path = Path.Combine(RuntimePaths.UpdateDirectory(RuntimePaths.PortableInstallationForData(data)), "continuation.json");
        if (!File.Exists(path)) return null;
        RuntimePaths.DemandPrivatePath(path);
        if (new FileInfo(path).Length > 16384) throw new IOException("Recovery presence exceeds its bound.");
        return RelayJson.Deserialize<InstallationRecoveryPresence>(AgentRelay.Protocol.McpPrivateFiles.ReadText(path, 16384));
    }

    private static bool Alive(InstallationRecoveryPresence value, string executable)
    {
        try
        {
            using var process = Process.GetProcessById(value.ProcessId);
            return !process.HasExited && new DateTimeOffset(process.StartTime.ToUniversalTime()) == value.ProcessStartedAt &&
                string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
    }

    private static InstallationRecoveryRunner Runner(string data, string transaction)
    {
        var root = RuntimePaths.PortableInstallationForData(data);
        var path = Path.Combine(RuntimePaths.UpdateDirectory(root), "recovery.json");
        RuntimePaths.DemandPrivatePath(path);
        var value = RelayJson.Deserialize<InstallationRecoveryRunner>(AgentRelay.Protocol.McpPrivateFiles.ReadText(path));
        var directory = Path.Combine(RuntimePaths.UpdateDirectory(root), transaction, "runner");
        if (value.Schema != 1 || value.TransactionId != transaction || !string.Equals(value.Installation, root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(value.Data, data, StringComparison.OrdinalIgnoreCase) || !string.Equals(value.Executable, Path.Combine(directory, "AgentRelay.Windows.exe"), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Recovery image belongs to another transaction or scope.");
        AgentRelay.Protocol.McpProcessSelections.Validate(new(value.Executable, [], value.Files));
        return value;
    }

    private static void WritePresence(string data, InstallationRecoveryPresence value)
    {
        var path = Path.Combine(RuntimePaths.UpdateDirectory(RuntimePaths.PortableInstallationForData(data)), "continuation.json");
        RuntimePaths.DemandPrivatePath(path);
        var temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, RelayJson.Serialize(value)); File.Move(temporary, path, true);
    }
}
