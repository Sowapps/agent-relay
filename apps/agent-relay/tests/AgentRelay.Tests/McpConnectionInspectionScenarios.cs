using AgentRelay.Infrastructure;

internal static class McpConnectionInspectionScenarios
{
    public static void Run(string root)
    {
        if (Directory.Exists(root)) throw new ArgumentException("Use a fresh synthetic directory.");
        var now = DateTimeOffset.UtcNow;
        var absent = McpConnectionInspection.Capture(root, new(), now);
        Require(!absent.Available && absent.Files == 0 && !Directory.Exists(root), "Missing data was created or misreported.");
        Directory.CreateDirectory(root);
        McpConnectionLease Lease(string client, string state, int age) => new(Guid.NewGuid(), null, client,
            "fixture", "fixture", "contract", "generation", null, state, now.AddSeconds(-age));
        var current = Lease("codex", "current", 0);
        McpConnections.Write(root, current);
        McpConnections.Write(root, Lease("claude", "outdated", 1));
        McpConnections.Write(root, Lease("codex", "current", 60));
        McpConnections.Write(root, Lease("codex", "closed", 0));
        McpConnections.Write(root, Lease("future", "current", -120));
        var directory = McpConnections.DirectoryFor(root);
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), "{");
        File.WriteAllText(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"), "null");
        var before = Directory.GetFiles(directory).ToDictionary(path => path, File.ReadAllText);
        var summary = McpConnectionInspection.Capture(root, new(), now);
        Require(summary.Files == 7 && summary.Valid == 5 && summary.Matched == 2 && summary.Connections.Count == 2,
            "Default summary did not distinguish valid, recent and historical leases.");
        Require(summary.States["offline"] == 2 && summary.Failures.Values.Sum() == 2 &&
            summary.Connections.All(item => item.Details is null), "Freshness, invalid-file reporting or detail default failed.");
        var filtered = McpConnectionInspection.Capture(root, new(Client: "CODEX", IncludeOffline: true, Limit: 1, Details: true), now);
        Require(filtered.Matched == 3 && filtered.Omitted == 2 && filtered.Connections.Single().Details is not null,
            "Client filter, explicit details or result truncation failed.");
        var offline = McpConnectionInspection.Capture(root, new(State: "offline"), now);
        Require(offline.Matched == 2, "An explicit offline-state filter must include offline leases.");
        Require(McpConnections.List(root).Count == 5, "The shared monitoring reader lost valid records.");
        try { McpConnectionInspection.Capture(root, new(Limit: 0), now); throw new Exception("Invalid limit accepted."); }
        catch (ArgumentException) { }
        Require(before.Count == Directory.GetFiles(directory).Length && before.All(item => File.ReadAllText(item.Key) == item.Value),
            "Inspection changed lease files.");
        var many = Path.Combine(root, "scan-bound"); Directory.CreateDirectory(many);
        for (var index = 0; index < 260; index++) McpConnections.Write(many, Lease("fixture", "current", 0));
        var bounded = McpConnectionInspection.Capture(many, new(), now);
        Require(bounded.Files == 260 && bounded.Scanned == 256 && bounded.ScanTruncated &&
            bounded.Connections.Count == 10 && bounded.Omitted == 246, "Scan and presentation limits are not explicit.");
        Console.WriteLine("MCP_CONNECTION_INSPECTION=PASS");
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
