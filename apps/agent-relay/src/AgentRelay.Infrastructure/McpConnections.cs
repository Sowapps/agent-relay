using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record McpConnectionLease(Guid InstanceId, Guid? BindingId, string Client, string BrokerVersion,
    string Version, string ContractHash, string Generation, string? CatalogContract, string State, DateTimeOffset LastSeen);

// Diagnostic leases are never authentication or authorization. Close is separate from credential revocation.
public static class McpConnections
{
    private const int ScanLimit = 256;
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
        => Read(data, DateTimeOffset.UtcNow).Items;

    // Shared with the read-only CLI projection: freshness and file validation have one owner.
    // See McpConnectionInspection.Capture; diagnostic leases never establish process/chat ownership.
    internal static McpLeaseSnapshot Read(string data, DateTimeOffset now)
    {
        var directory = DirectoryFor(data);
        if (!Directory.Exists(directory)) return new(false, 0, 0, [], new Dictionary<string, int>());
        RuntimePaths.DemandPrivatePath(directory);
        var paths = Directory.EnumerateFiles(directory, "*.json").OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        var values = new List<McpConnectionLease>();
        var failures = new Dictionary<string, int>(StringComparer.Ordinal);
        void Reject(string code) => failures[code] = failures.GetValueOrDefault(code) + 1;
        foreach (var path in paths.Take(ScanLimit))
        {
            try
            {
                RuntimePaths.DemandPrivatePath(path);
                if (new FileInfo(path).Length > 16384) { Reject("lease_too_large"); continue; }
                var item = RelayJson.Deserialize<McpConnectionLease>(File.ReadAllText(path));
                if (item is null || Path.GetFileNameWithoutExtension(path) != item.InstanceId.ToString("N") ||
                    string.IsNullOrWhiteSpace(item.Client) || string.IsNullOrWhiteSpace(item.State) || item.LastSeen == default)
                { Reject("lease_invalid"); continue; }
                var age = now - item.LastSeen;
                values.Add(age > TimeSpan.FromSeconds(10) || age < -TimeSpan.FromMinutes(1) ? item with { State = "offline" } : item);
            }
            catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
            { Reject(error is JsonException ? "lease_invalid_json" : "lease_unreadable"); }
        }
        // Heartbeat modification times select recent records; they must not reorder an open monitoring view.
        return new(true, paths.Length, Math.Min(paths.Length, ScanLimit),
            values.OrderBy(item=>item.Client,StringComparer.OrdinalIgnoreCase).ThenBy(item=>item.InstanceId).ToArray(), failures);
    }
    public static void RequestClose(string data, Guid instance)
    {
        if (!List(data).Any(item => item.InstanceId == instance && item.State is not ("offline" or "closed"))) throw new RelayException("not_found", "No active broker connection has that identity.");
        var path = Path.Combine(DirectoryFor(data), instance.ToString("N") + ".close"); RuntimePaths.DemandPrivatePath(path);
        File.WriteAllText(path, "Close after the admitted MCP call finishes; do not revoke credentials or replay work.");
    }
}

internal sealed record McpLeaseSnapshot(bool Available, int Files, int Scanned,
    IReadOnlyList<McpConnectionLease> Items, IReadOnlyDictionary<string, int> Failures);
