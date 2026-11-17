using System.Text.Json.Serialization;

namespace AgentRelay.Infrastructure;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record McpConnectionQuery(string? Client = null, string? State = null,
    bool IncludeOffline = false, int Limit = 10, bool Details = false);

public sealed record McpConnectionFinding(string Client, string State, DateTimeOffset LastSeen,
    McpConnectionLease? Details);

public sealed record McpConnectionReport(DateTimeOffset CapturedAt, string Scope, bool Available, McpConnectionQuery Query,
    int Files, int Scanned, bool ScanTruncated, int Valid, int Matched, int Omitted,
    IReadOnlyDictionary<string, int> States, IReadOnlyDictionary<string, int> Failures,
    IReadOnlyList<McpConnectionFinding> Connections, string Ownership);

/// <summary>Bounded, read-only projection of the shared lease reader; never connects to or starts a relay.</summary>
/// <seealso cref="McpConnections.List"/>
public static class McpConnectionInspection
{
    public static McpConnectionReport Capture(string data, McpConnectionQuery? query = null)
        => Capture(data, query ?? new(), DateTimeOffset.UtcNow);

    internal static McpConnectionReport Capture(string data, McpConnectionQuery query, DateTimeOffset now)
    {
        if (query.Limit is < 1 or > 50) throw new ArgumentException("Connection limit must be between 1 and 50.");
        if (query.Client is not null && string.IsNullOrWhiteSpace(query.Client) ||
            query.State is not null && string.IsNullOrWhiteSpace(query.State))
            throw new ArgumentException("Connection filters cannot be empty.");
        var snapshot = McpConnections.Read(data, now);
        var matches = snapshot.Items.Where(item =>
            (query.Client is null || string.Equals(item.Client, query.Client, StringComparison.OrdinalIgnoreCase)) &&
            (query.State is null || string.Equals(item.State, query.State, StringComparison.OrdinalIgnoreCase)) &&
            (query.IncludeOffline || query.State is not null || item.State is not ("offline" or "closed")))
            .OrderByDescending(item => item.LastSeen).ThenBy(item => item.InstanceId).ToArray();
        var findings = matches.Take(query.Limit).Select(item => new McpConnectionFinding(
            item.Client, item.State, item.LastSeen, query.Details ? item : null)).ToArray();
        return new(now, Path.GetFullPath(data), snapshot.Available, query, snapshot.Files, snapshot.Scanned,
            snapshot.Files > snapshot.Scanned, snapshot.Items.Count, matches.Length, matches.Length - findings.Length,
            snapshot.Items.GroupBy(item => item.State).OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count()),
            snapshot.Failures, findings, "Lease freshness is not proof of process liveness; PID/chat ownership is unavailable.");
    }
}
