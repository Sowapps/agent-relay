using System.Diagnostics;
using AgentRelay.Infrastructure;
using AgentRelay.Protocol;

internal static class ManualCleanupScenarios
{
    public static async Task RunAsync(string data)
    {
        var logs = Path.Combine(data, "logs", "mcp");
        Directory.CreateDirectory(logs);
        var before = new DateTimeOffset(2026, 11, 2, 1, 59, 0, TimeSpan.Zero);
        var clock = new FixedTime { Now = before };
        var old = Create("20261030.log", before.AddDays(-3));
        var changed = Create("20261029.log", before.AddDays(-4));
        var yesterday = Create("20261101.log", before.AddHours(-3));
        var today = Create("20261102.log", before.AddMinutes(-8));
        var unknown = Create("unknown.log", before.AddDays(-3));
        var state = RuntimePaths.TransportStateDirectory(data);
        var closed = Guid.NewGuid();
        var live = Guid.NewGuid();
        McpTransportPresence Presence(Guid id, string status) => new(id, Environment.ProcessId, null, "fixture",
            "gateway", null, "worker", status, before.AddDays(-3), Activity: status == "closed" ? "closed" : "idle");
        McpTransportPresences.Write(state, Presence(closed, "closed"));
        McpTransportPresences.Write(state, Presence(live, "current"));
        var instances = McpTransportPresences.DirectoryFor(state);
        string Trace(Guid id) => Path.Combine(instances, id.ToString("N") + ".json");
        SetTimes(Trace(closed), before.AddDays(-3));
        SetTimes(Trace(live), before.AddDays(-3));
        var cleaner = new ManualCleanup(data, clock);
        var early = await cleaner.AnalyzeAsync(true, true, CancellationToken.None);
        Check(early.ProtectedFrom == new DateOnly(2026, 11, 1), "previous day protected before 02:00");
        Check(early.Entries.Count == 3 && early.Protected.Any(x => x.Path.EndsWith("20261101.log")), "only owned candidates and protected yesterday");
        clock.Now = before.AddMinutes(1);
        var later = await cleaner.AnalyzeAsync(true, true, CancellationToken.None);
        Check(later.ProtectedFrom == new DateOnly(2026, 11, 2) &&
            later.Entries.Any(x => x.Path.EndsWith("20261101.log")), "yesterday is eligible at 02:00");
        clock.Now = before;
        File.WriteAllText(changed, "new data");
        SetTimes(changed, before.AddDays(-4));
        var result = await cleaner.ApplyAsync(later.PlanId, CancellationToken.None);
        Check(result.Items.Count(x => x.State == "removed") == 2 && result.Items.Count(x => x.State == "skipped") == 2 &&
            !File.Exists(old) && !File.Exists(Trace(closed)), "only original eligible logs and closed diagnostics removed");
        Check(File.Exists(changed) && File.Exists(yesterday) && File.Exists(today) && File.Exists(unknown) && File.Exists(Trace(live)),
            "changed, protected and unknown files remain");
        Check(File.Exists(Path.Combine(data, result.Report)), "partial execution retained a per-item maintenance report");
        try
        {
            await cleaner.ApplyAsync(later.PlanId, CancellationToken.None);
            throw new InvalidOperationException("Applied a spent plan");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("expiré", StringComparison.Ordinal)) { }

        var admission = new McpDeploymentAdmission(data);
        var duringUpdate = await cleaner.AnalyzeAsync(true, false, CancellationToken.None);
        admission.Suspend("fixture", () => { });
        try
        {
            var retained = await cleaner.ApplyAsync(duringUpdate.PlanId, CancellationToken.None);
            Check(retained.Items.All(x => x.State == "skipped") && File.Exists(changed), "promotion protects the analyzed file");
        }
        finally { admission.Release("fixture"); }

        var spring = new FixedTime { Now = new DateTimeOffset(2026, 3, 29, 0, 59, 0, TimeSpan.Zero),
            Zone = TimeZoneInfo.FindSystemTimeZoneById("Romance Standard Time") };
        var calendar = new ManualCleanup(data, spring);
        Check((await calendar.AnalyzeAsync(false, false, CancellationToken.None)).ProtectedFrom == new DateOnly(2026, 3, 28),
            "spring clock protects the previous date at 01:59");
        spring.Now = spring.Now.AddMinutes(1);
        Check((await calendar.AnalyzeAsync(false, false, CancellationToken.None)).ProtectedFrom == new DateOnly(2026, 3, 29),
            "spring clock stops protecting yesterday once the local clock passes 02:00");

        // A large historical prefix must never hide the active connection.
        for (var index = 0; index < 260; index++)
            McpTransportPresences.Write(state, Presence(new Guid(index + 1, 0, 0, new byte[8]), "closed"));
        Check(McpTransportPresences.Read(state).Count == 261 &&
            McpTransportPresences.Read(state).Any(x => x.InstanceId == live), "transport inventory retains every live or required identity");
        var childStart = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
            RedirectStandardOutput = true };
        foreach (var argument in new[] { "/d", "/c", "pause" }) childStart.ArgumentList.Add(argument);
        using var child = Process.Start(childStart) ?? throw new InvalidOperationException("No isolated process fixture");
        try
        {
            var stale = Guid.NewGuid();
            McpTransportPresences.Write(state, Presence(stale, "current") with
            {
                ProcessId = child.Id, ProcessStartedAt = new(child.StartTime.ToUniversalTime()), BundleDirectory = data
            });
            Check(McpTransportPresences.Read(state).Single(x => x.InstanceId == stale).State == "offline" && !child.HasExited,
                "a different Windows process cannot own a retained transport");
        }
        finally
        {
            child.StandardInput.Close();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        Console.WriteLine("MANUAL_CLEANUP=PASS (02:00, protected and changed files, explicit confirmation, closed traces; full transport inventory)");

        string Create(string name, DateTimeOffset stamp)
        {
            var path = Path.Combine(logs, name);
            File.WriteAllText(path, "old data");
            SetTimes(path, stamp);
            return path;
        }
    }

    private static void SetTimes(string path, DateTimeOffset stamp)
    {
        File.SetCreationTimeUtc(path, stamp.UtcDateTime);
        File.SetLastWriteTimeUtc(path, stamp.UtcDateTime);
    }

    private static void Check(bool test, string label)
    {
        if (!test) throw new InvalidOperationException(label);
    }

    private sealed class FixedTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; }
        public TimeZoneInfo Zone { get; init; } = TimeZoneInfo.Utc;
        public override TimeZoneInfo LocalTimeZone => Zone;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
