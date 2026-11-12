using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class InstallationUpdateScenarios
{
    internal static void Run(string root)
    {
        var installation = Path.Combine(root, "installation");
        var data = Path.Combine(installation, "var", "apps", "agent-relay", "default");
        var candidate = Path.Combine(root, "candidate");
        Directory.CreateDirectory(Path.Combine(installation, "app"));
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(installation, "app", "AgentRelay.Windows.exe"), "old");
        File.WriteAllText(Path.Combine(installation, "app", "obsolete.txt"), "remove");
        File.WriteAllText(Path.Combine(installation, "app", "mcp-contract.json"), "{\"version\":\"0.0.1\"}");
        var pointer = new { executable = Path.Combine(installation, "app", "AgentRelay.Windows.exe"), distribution = "portable-win-x64" };
        File.WriteAllText(Path.Combine(data, "mcp-runtime.json"), RelayJson.Serialize(pointer));
        File.WriteAllText(Path.Combine(data, "untouched.txt"), "user-data");
        Directory.CreateDirectory(Path.Combine(data, "mcp-launcher"));
        File.WriteAllText(Path.Combine(data, "mcp-launcher", "AgentRelay.Windows.exe"), "old-broker");
        Directory.CreateDirectory(Path.Combine(data, "startup-launcher"));
        File.WriteAllText(Path.Combine(data, "startup-launcher", "AgentRelay.Windows.exe"), "old-startup");
        Directory.CreateDirectory(Path.Combine(candidate, "app", "documentation"));
        File.WriteAllText(Path.Combine(candidate, "app", "AgentRelay.Windows.exe"), "new");
        File.WriteAllText(Path.Combine(candidate, "app", "mcp-contract.json"), RelayJson.Serialize(new { version = "0.0.2-rc.1", contractHash = new string('a', 64) }));
        File.WriteAllText(Path.Combine(candidate, "app", "documentation", "quickstart.md"), "updated-guide");
        File.WriteAllText(Path.Combine(candidate, "README.md"), "updated-readme");
        Manifest(candidate);
        var update = new InstallationUpdate(installation, data);
        var prepared = update.Run("prepare", candidate);
        Check(prepared.Phase == "prepared" && update.Run("prepare", candidate).Id == prepared.Id, "repeat preparation retains transaction");
        Check(prepared.Files.Any(file => file.Name == "README.md") && prepared.Files.Any(file => file.Name.EndsWith("mcp-launcher/documentation/quickstart.md")), "complete payload includes root docs and broker resources");
        Refuses("update_barrier_required", () => update.Run("apply"));
        File.WriteAllText(Path.Combine(data, "promotion.lock"), prepared.Id);
        using (RelayLocation.OpenHostOwnership(data)) Refuses<IOException>(() => update.Run("apply"));
        var installed = Path.Combine(installation, "app", "AgentRelay.Windows.exe");
        using (new FileStream(installed, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            try { update.Run("apply"); throw new Exception("Locked component was overwritten."); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
        Check(update.Run("status").Phase == "applying", "locked file retains resumable applying state");
        // A previous file may already have moved when a later component fails; reconcile real hashes.
        Check(update.Run("apply").Phase == "files_applied", "resume after partial filesystem publication");
        Check(File.ReadAllText(installed) == "new" && !File.Exists(Path.Combine(installation, "app", "obsolete.txt")), "application replacement and retired files");
        Check(File.ReadAllText(Path.Combine(data, "mcp-launcher", "AgentRelay.Windows.exe")) == "new"
            && File.ReadAllText(Path.Combine(data, "startup-launcher", "AgentRelay.Windows.exe")) == "new", "both retained launchers updated");
        Refuses("update_activation_required", () => update.Run("verify"));
        // An abrupt process exit after publishing one file has the same journal/hash boundary as this retained state.
        Check(new InstallationUpdate(installation, data).Run("rollback").Phase == "files_restored", "new process instance restores retained before-images");
        Check(update.Run("rollback-finish").Phase == "rolled_back", "recovery completes only after its adapter confirms restoration");
        Check(File.ReadAllText(installed) == "old" && File.ReadAllText(Path.Combine(data, "mcp-launcher", "AgentRelay.Windows.exe")) == "old-broker", "paired rollback restores application and broker");
        Check(!File.Exists(Path.Combine(installation, "README.md")) && File.ReadAllText(Path.Combine(data, "untouched.txt")) == "user-data", "rollback deletes only newly managed files and preserves data");
        Check(update.Run("rollback").Phase == "rolled_back", "repeat rollback is a no-op");
        using (new FileStream(Path.Combine(installation, "var", "apps", "agent-relay", "update", "update.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Refuses<IOException>(() => update.Run("status"));
        File.WriteAllText(Path.Combine(candidate, "README.md"), "tampered");
        Refuses("update_candidate_invalid", () => update.Run("prepare", candidate));
        Console.WriteLine("COMPLETE_INSTALLATION_UPDATE=PASS (all components, locked file, resume, activation refusal, rollback, data preservation, exclusive lock, manifest tampering)");
    }

    private static void Manifest(string candidate)
    {
        var files = Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(candidate, file).Replace('\\', '/'), file => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))));
        File.WriteAllText(Path.Combine(candidate, "release-manifest.json"), RelayJson.Serialize(new { version = "0.0.2-rc.1", selfContained = true, files }));
    }

    private static void Refuses(string code, Action action)
    {
        try { action(); }
        catch (RelayException error) when (error.Code == code) { return; }
        throw new Exception("Expected " + code);
    }
    private static void Refuses<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("Expected " + typeof(T).Name);
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
