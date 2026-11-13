using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Infrastructure;

/// <summary>Coherent stopped-scope snapshot, paired with restoration; never replaces a completed before-image.</summary>
internal static class InstallationSnapshot
{
    private static readonly string[] stateFiles = ["relay.sqlite", "mcp-runtime.json", "relay-config.json", "relay-projects.json", "mcp-contract.json", "mcp-contract-history.json", "transport-selection.json"];
    private static string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    public static void Capture(string data, string destination)
    {
        if (Directory.Exists(destination)) { Verify(destination); return; }
        var pending = destination + ".pending-" + Guid.NewGuid().ToString("N");
        RuntimePaths.DemandPrivatePath(Path.Combine(pending, "snapshot.json"));
        // Resume partial capture only before any release selection: completed snapshots are never overwritten.
        Directory.CreateDirectory(pending);
        foreach (var name in stateFiles.Where(name => name != "relay.sqlite"))
            if (File.Exists(Path.Combine(data, name))) File.Copy(Path.Combine(data, name), Path.Combine(pending, name), true);
        if (Directory.Exists(Path.Combine(data, "connections")))
            foreach (var source in Directory.EnumerateFiles(Path.Combine(data, "connections")))
            {
                RuntimePaths.DemandPrivatePath(source);
                Directory.CreateDirectory(Path.Combine(pending, "connections"));
                File.Copy(source, Path.Combine(pending, "connections", Path.GetFileName(source)), true);
            }
        using (var source = new RelayDatabase(data, readOnly: true).Open())
        using (var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = RelayDatabase.DatabasePath(pending), Pooling = false }.ToString()))
        { target.Open(); source.BackupDatabase(target); }
        var files = Directory.EnumerateFiles(pending, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "snapshot.json")
            .ToDictionary(path => Path.GetRelativePath(pending, path).Replace('\\', '/'), Hash);
        Write(Path.Combine(pending, "snapshot.json"), RelayJson.Serialize(files));
        Directory.Move(pending, destination);
        Verify(destination);
    }

    public static IReadOnlyDictionary<string, string> Verify(string destination)
    {
        RuntimePaths.DemandPrivatePath(Path.Combine(destination, "snapshot.json"));
        var files = RelayJson.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(destination, "snapshot.json")));
        if (!files.ContainsKey("relay.sqlite")) throw new IOException("Coherent snapshot omits the database.");
        var actualNames = Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) != "snapshot.json").Select(path => Path.GetRelativePath(destination, path).Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        if (!actualNames.SetEquals(files.Keys)) throw new IOException("Snapshot inventory differs from its completed manifest.");
        foreach (var (name, hash) in files)
        {
            if (!stateFiles.Contains(name, StringComparer.Ordinal) && !(name.StartsWith("connections/", StringComparison.Ordinal) && name[12..].IndexOfAny(['/', '\\']) < 0))
                throw new IOException("Snapshot contains an unmanaged path.");
            var path = Owned(destination, name);
            if (!File.Exists(path) || Hash(path) != hash) throw new IOException("Coherent snapshot is missing or changed: " + name);
        }
        return files;
    }

    /// <seealso cref="Capture"/>
    public static void Restore(string data, string snapshot)
    {
        var files = Verify(snapshot);
        foreach (var name in stateFiles.Concat(new[] { "relay.sqlite-wal", "relay.sqlite-shm" }))
        {
            var target = Owned(data, name);
            if (files.ContainsKey(name)) Replace(target, Owned(snapshot, name));
            else if (File.Exists(target)) File.Delete(target);
        }
        var connections = Path.Combine(data, "connections");
        if (Directory.Exists(connections))
            foreach (var file in Directory.EnumerateFiles(connections))
                if (!files.ContainsKey("connections/" + Path.GetFileName(file))) { RuntimePaths.DemandPrivatePath(file); File.Delete(file); }
        foreach (var (name, _) in files.Where(item => item.Key.StartsWith("connections/", StringComparison.Ordinal))) Replace(Owned(data, name), Owned(snapshot, name));
    }

    public static void DemandPreserved(string data, string snapshot)
    {
        var files = Verify(snapshot);
        var directory = Path.Combine(data, "connections");
        var actualConnections = Directory.Exists(directory) ? Directory.EnumerateFiles(directory).Select(path => "connections/" + Path.GetFileName(path)).ToHashSet(StringComparer.Ordinal) : [];
        if (!actualConnections.SetEquals(files.Keys.Where(name => name.StartsWith("connections/", StringComparison.Ordinal)))) throw new IOException("Credential inventory changed during activation.");
        foreach (var (name, hash) in files.Where(item => item.Key is "relay-projects.json" or "relay-config.json" || item.Key.StartsWith("connections/", StringComparison.Ordinal)))
            if (!File.Exists(Owned(data, name)) || Hash(Owned(data, name)) != hash) throw new IOException("Preserved configuration/credentials changed: " + name);
    }

    private static string Owned(string root, string name)
    {
        var path = Path.GetFullPath(name, root);
        if (Path.IsPathFullyQualified(name) || !path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Snapshot path escapes its owner.");
        RuntimePaths.DemandPrivatePath(path);
        return path;
    }

    private static void Replace(string target, string source)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var pending = target + ".snapshot-pending";
        using (var input = File.OpenRead(source))
        using (var output = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None)) { input.CopyTo(output); output.Flush(true); }
        File.Move(pending, target, true);
    }

    private static void Write(string path, string text)
    {
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
        writer.Write(text); writer.Flush(); stream.Flush(true);
    }
}
