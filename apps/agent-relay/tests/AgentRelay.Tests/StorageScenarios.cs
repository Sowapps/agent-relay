using AgentRelay.Infrastructure;

internal static class StorageScenarios
{
    internal static void Run(string fixture)
    {
        var root = Path.Combine(fixture, "installation");
        var binary = Path.Combine(root, "nested", "binary"); Directory.CreateDirectory(binary);
        File.WriteAllText(Path.Combine(binary, "installation.json"), "{\"version\":1,\"root\":\"../..\"}");
        var expected = Path.Combine(root, "var", "apps", "agent-relay", "default");
        if (RuntimePaths.DefaultDataDirectory(binary) != expected || Directory.Exists(Path.Combine(root, "local"))) throw new Exception("Installation root resolution depends on cwd/local.");
        // Exercise the actual Windows volume lookup for a destination whose parents do not exist yet.
        var publication = typeof(FileTransferService).Assembly.GetType("AgentRelay.Infrastructure.FilePublication")!;
        publication.GetMethod("DemandSameVolume", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(null, [root, Path.Combine(root, "not-created", "target.bin")]);
        var data = Path.Combine(fixture, "migrated"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "migrated-to.json"), "{}");
        try { _ = RelayRuntime.Start(data, _ => { }); throw new Exception("A retired scope was started."); }
        catch (IOException) { }
        if (File.Exists(Path.Combine(data, "relay.sqlite"))) throw new Exception("Retired scope created a database.");
        var id = Guid.NewGuid();
        if (!RuntimePaths.TransferPartial(expected, id).StartsWith(Path.Combine(expected, "staging", "transfers") + Path.DirectorySeparatorChar)) throw new Exception("Transfer partial escaped private storage.");
        Console.WriteLine("RUNTIME_STORAGE=PASS (installation descriptor, actual Windows volume qualification, private staging, retired-scope refusal)");
    }
}
