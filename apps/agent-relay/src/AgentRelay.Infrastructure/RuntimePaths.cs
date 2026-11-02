using System.Text.Json;
using System.Text.RegularExpressions;

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
        return Path.Combine(root, "var", "apps", "agent-relay", "default");
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
        for (var current = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Private relay storage cannot traverse a reparse point.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Private relay files cannot be reparse points.");
    }
    public static void DemandActive(string data)
    {
        if (File.Exists(Path.Combine(data, "migrated-to.json"))) throw new AgentRelay.Core.RelayException("relay_migrated", "Cette instance a été migrée. Rechargez la connexion MCP et utilisez le nouveau lanceur ; aucun nouveau journal ne sera créé ici.", effectsPossible: false);
    }

    public static bool IsNativeRoot(string root) => Regex.IsMatch(root, "^/srv/agent-relay/var/apps/agent-relay/(sessions/[a-f0-9]{32}|profiles/[a-zA-Z0-9-]+)$") || Regex.IsMatch(root, "^/srv/agent-relay-[a-zA-Z0-9-]+$");
    public static string NativeSession(Guid session) => "/srv/agent-relay/var/apps/agent-relay/sessions/" + session.ToString("N");
}
