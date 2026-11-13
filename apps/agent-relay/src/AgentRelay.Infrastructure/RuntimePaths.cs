using System.Text.Json;

namespace AgentRelay.Infrastructure;

// Composition-owned locations. User workspaces and explicit output directories are separate contracts.
public static class RuntimePaths
{
    public static string? InstallationRoot(string? binaryDirectory = null)
    {
        var folder = Path.GetFullPath(binaryDirectory ?? AppContext.BaseDirectory);
        var marker = Path.Combine(folder, "installation.json");
        if (!File.Exists(marker)) return null;
        using var document = JsonDocument.Parse(File.ReadAllText(marker));
        var value = document.RootElement;
        if (value.GetProperty("version").GetInt32() != 1) throw new IOException("Unsupported installation layout.");
        var root = value.GetProperty("root").GetString();
        if (string.IsNullOrWhiteSpace(root)) throw new IOException("Installation root is missing.");
        return Path.GetFullPath(root, folder);
    }

    public static string DefaultDataDirectory(string? binaryDirectory = null)
    {
        var root = InstallationRoot(binaryDirectory) ?? throw new IOException("L’installation doit déclarer sa racine. Utilisez son lanceur ou un --data-dir explicite ; aucun dossier local/ ou profil utilisateur n’est choisi automatiquement.");
        return PortableDataDirectory(root);
    }

    public static string ApplicationDirectory(string installation) => Path.Combine(Path.GetFullPath(installation), "var", "apps", "agent-relay");
    public static string PortableDataDirectory(string installation) => Path.Combine(ApplicationDirectory(installation), "default");
    public static string ReleaseDirectory(string installation, string candidateHash) => Path.Combine(ApplicationDirectory(installation), "releases", candidateHash);
    public static string TransportSelection(string data) => Path.Combine(Path.GetFullPath(data), "transport-selection.json");
    public static string TransportStateDirectory(string data) => Path.Combine(Path.GetFullPath(data), "transport");
    public static string TransportBootstrapDirectory(string data) => Path.Combine(Path.GetFullPath(data), "transport-bootstrap");
    public static string TransportBootstrapExecutable(string data) => Path.Combine(TransportBootstrapDirectory(data), "AgentRelay.Transport.exe");
    public static string TransportEntryScript(string data) => Path.Combine(Path.GetFullPath(data), "launch.ps1");
    public static string TransportBundleDirectory(string data) => Path.Combine(TransportStateDirectory(data), "bundles");
    public static string UpdateDirectory(string installation) => Path.Combine(ApplicationDirectory(installation), "update");
    public static string UpdateJournal(string data) => Path.Combine(UpdateDirectory(PortableInstallationForData(data)), "current.json");
    public static string RuntimePointer(string data) => Path.Combine(Path.GetFullPath(data), "mcp-runtime.json");
    public static string InstallationDescriptor(string installation, string binaryDirectory) => RelayJson.Serialize(new
    { version = 1, root = Path.GetRelativePath(Path.GetFullPath(binaryDirectory), Path.GetFullPath(installation)) });
    public static string PortableInstallationForData(string data)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(data));
        var root = new DirectoryInfo(path).Parent?.Parent?.Parent?.Parent?.FullName;
        if (root is null || !string.Equals(PortableDataDirectory(root), path, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The data directory does not belong to the portable installation layout.");
        return root;
    }

    public static IEnumerable<string> ProtectedRoots(string data)
    {
        yield return Path.GetFullPath(data);
        yield return AppContext.BaseDirectory;
        if (InstallationRoot() is { } root) yield return Path.Combine(root, "var");
    }

    public static string SessionWorkspace(string data, Guid session) => Path.Combine(Path.GetFullPath(data), "workspaces", session.ToString("N"));
    public static string TransferPartial(string data, Guid request) => Path.Combine(Path.GetFullPath(data), "staging", "transfers", request.ToString("D") + ".part");
    public static void DemandPrivatePath(string path)
    {
        AgentRelay.Protocol.McpPrivateFiles.Demand(path);
    }
    public static void DemandActive(string data)
    {
        if (File.Exists(Path.Combine(data, "migrated-to.json"))) throw new AgentRelay.Core.RelayException("relay_migrated", "Cette instance a été migrée. Rechargez la connexion MCP et utilisez le nouveau lanceur ; aucun nouveau journal ne sera créé ici.", effectsPossible: false);
    }

    public static bool IsNativeRoot(string root) => NativeLayout.IsRoot(root);
    public static string NativeSession(Guid session) => NativeLayout.Session(session);
}
