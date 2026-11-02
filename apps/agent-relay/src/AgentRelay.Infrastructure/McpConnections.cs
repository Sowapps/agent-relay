using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record McpConnectionLease(Guid InstanceId, Guid? BindingId, string Client, string BrokerVersion,
    string Version, string ContractHash, string Generation, string? CatalogContract, string State, DateTimeOffset LastSeen);

// Diagnostic leases are never authentication or authorization. Close is separate from credential revocation.
public static class McpConnections
{
    public static string DirectoryFor(string data) => Path.Combine(data, "mcp-connections");
    public static bool CloseRequested(string data, Guid instance) => File.Exists(Path.Combine(DirectoryFor(data), instance.ToString("N") + ".close"));
    public static void Write(string data, McpConnectionLease lease)
    {
        if (!Directory.Exists(data) || File.Exists(Path.Combine(data, "migrated-to.json"))) return;
        var directory = DirectoryFor(data); Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, lease.InstanceId.ToString("N") + ".json"); RuntimePaths.DemandPrivatePath(path);
        var pending = path + ".pending-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(pending, RelayJson.Serialize(lease)); File.Move(pending, path, true);
    }
    public static IReadOnlyList<McpConnectionLease> List(string data)
    {
        var directory = DirectoryFor(data);
        if (!Directory.Exists(directory)) return [];
        var values = new List<McpConnectionLease>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(256))
        {
            try
            {
                RuntimePaths.DemandPrivatePath(path);
                if (new FileInfo(path).Length > 16384) continue;
                var item = RelayJson.Deserialize<McpConnectionLease>(File.ReadAllText(path));
                if (Path.GetFileNameWithoutExtension(path) != item.InstanceId.ToString("N")) continue;
                var age = DateTimeOffset.UtcNow - item.LastSeen;
                values.Add(age > TimeSpan.FromSeconds(10) || age < -TimeSpan.FromMinutes(1) ? item with { State = "offline" } : item);
            }
            catch (Exception error) when (error is IOException or JsonException) { }
        }
        return values;
    }
    public static void RequestClose(string data, Guid instance)
    {
        if (!List(data).Any(item => item.InstanceId == instance && item.State is not ("offline" or "closed"))) throw new RelayException("not_found", "No active broker connection has that identity.");
        var path = Path.Combine(DirectoryFor(data), instance.ToString("N") + ".close"); RuntimePaths.DemandPrivatePath(path);
        File.WriteAllText(path, "Close after the admitted MCP call finishes; do not revoke credentials or replay work.");
    }
}
