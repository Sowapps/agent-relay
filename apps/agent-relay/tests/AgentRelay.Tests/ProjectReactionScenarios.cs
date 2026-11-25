using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class ProjectReactionScenarios
{
    internal static async Task RunAsync(string data)
    {
        if (Directory.Exists(data)) throw new ArgumentException("Select a new qualification directory.");
        Directory.CreateDirectory(data);
        var project = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var sender = Guid.NewGuid();
        var windows = Guid.NewGuid();
        var workspace = Guid.NewGuid();
        var session = Guid.NewGuid();
        var scope = new OperationScope(project, actor, sender, workspace);
        var endpoint = new RelayEndpoint(windows, "Windows", "windows-command", "fixture-windows");
        var projects = new ProjectSettingsStore(data);
        projects.Save(new(1, [new(project, "Existing project")],
            [new(actor, "Project operator", "mcp", "fixture"), new(sender, "WSL source", "wsl-agent", "fixture"), endpoint],
            [new(workspace, windows, "Isolated workspace", data)],
            [new(project, actor, "main", [RelayCapabilities.RunCommand, RelayCapabilities.ReadFile], [new(workspace, true)], ["success", "deny", "other"]),
             new(project, sender, "secondary", [], []), new(project, windows, "secondary", [], [])]));
        var technical = new SettingsStore(data);
        technical.Save(new(1, [], WindowsCommands: [new("fixture-windows", [
            new("success", "Read the fixture location", Path.Combine(Environment.SystemDirectory, "where.exe"), ["relay-reaction-proof.txt"]),
            new("deny", "Windows exit code 2", Path.Combine(Environment.SystemDirectory, "where.exe"), ["/InvalidQualificationOption"]),
            new("other", "Windows exit code 1", Path.Combine(Environment.SystemDirectory, "findstr.exe"), ["/C:unmatched", "relay-reaction-proof.txt"])
        ])]));
        await File.WriteAllTextAsync(Path.Combine(data, "relay-reaction-proof.txt"), "isolated fixture");
        var database = new RelayDatabase(data);
        var store = new SqliteWorkStore(database);
        var reactionStore = new SqliteProjectReactions(database);
        var adapters = NativeAdapterComposition.Create(data, technical, projects, store, _ => false);
        await using var work = new WorkService(store, adapters.Executor, () => [new(windows.ToString("D"), true, false)]);
        var evidence = new ReleaseEvidence();
        var admission = new ProjectAdmission();
        var policy = new ProjectPolicy(projects);
        ProjectReactionService Reconnect() => new(reactionStore, policy, work, evidence,
            (target, rule) => ScopedCommands.ValidateTarget(technical.Load(), target, rule), admission.EnterWork);
        var reactions = Reconnect();

        async Task<Guid> Completed(Guid sourceSession, bool correlated = true)
        {
            var id = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;
            if (correlated) await store.AddInboxAsync(new(Guid.NewGuid(), "fixture", "WSL", "MCP", "confirmed", id, now,
                sourceSession, project, sender, actor), default);
            await store.SaveAsync(new(new(id, sender.ToString("D"), "message", SessionId: sourceSession, Scope: scope), "completed", now, now,
                new("native", "confirmed")), default);
            return id;
        }

        var old = await Completed(session);
        var rules = new[]
        {
            new ProjectReaction(Guid.NewGuid(), session, actor, windows, workspace, "success", "async", true, "windows", "fixture-windows"),
            new ProjectReaction(Guid.NewGuid(), session, actor, windows, workspace, "deny", "blocking", true, "windows", "fixture-windows"),
            new ProjectReaction(Guid.NewGuid(), session, actor, windows, workspace, "other", "blocking", true, "windows", "fixture-windows"),
            new ProjectReaction(Guid.NewGuid(), session, actor, windows, workspace, "deny", "async", true, "windows", "fixture-windows")
        };
        Require((await reactions.SaveAsync(project, new(0, rules), default)).Revision == 1, "existing project settings");
        await Reject("state_conflict", () => reactions.SaveAsync(project, new(0, rules), default));
        await Reject("reaction_target_invalid", () => reactions.SaveAsync(project, new(1, [rules[0] with { Destination = "wsl" }]), default));
        var ignored = await Completed(Guid.NewGuid());
        var withoutReceipt = await Completed(session, false);
        var source = await Completed(session);
        await reactions.SweepAsync(DateTimeOffset.UtcNow, default);
        Require((await reactions.ResultsAsync(project, old, default)).Count == 0 &&
                (await reactions.ResultsAsync(project, ignored, default)).Count == 0 &&
                (await reactions.ResultsAsync(project, withoutReceipt, default)).Count == 0, "watermark, session filter and correlated receipt");
        var pending = await reactions.ResultsAsync(project, source, default);
        Require(pending.Count == 4 && pending.All(run => run.Pending), "confirmed receipt alone cannot dispatch");
        foreach (var run in pending) await Reject("not_found", () => work.ReadAsync(run.Id, default));
        await Reject("reaction_pending", () => reactions.DemandContinuationAsync(project, source, default));
        evidence.Confirmed.Add(source);
        reactions = Reconnect();
        await reactions.SweepAsync(DateTimeOffset.UtcNow.AddMinutes(3), default);
        IReadOnlyList<ReactionRun> settled = [];
        for (var n = 0; n < 100; n++)
        {
            await reactions.SweepAsync(DateTimeOffset.UtcNow, default);
            settled = await reactions.ResultsAsync(project, source, default);
            if (settled.Count == 4 && settled.All(run => !run.Pending)) break;
            await Task.Delay(40);
        }
        Require(settled.Count == 4 && settled.Count(run => run.Rejects) == 1, "exit code 2 alone rejects and only for blocking");
        Require(settled.Single(run => run.Rule.CommandId == "other").ExitCode == 1 &&
                settled.Count(run => run.ExitCode == 2) == 2, "native exit code receipts");
        var success = settled.Single(run => run.Rule.CommandId == "success");
        Require(success.ExitCode == 0 && (await work.ReadAsync(success.Id, default)).Result!.Output.Contains(data, StringComparison.OrdinalIgnoreCase),
            "WSL-origin fixture to actual declared Windows command in the selected workspace");
        await Reject("reaction_rejected", () => reactions.DemandContinuationAsync(project, source, default));
        await reactions.SweepAsync(DateTimeOffset.UtcNow, default);
        Require((await store.ListAsync(default)).Count(item => item.Request.Kind == "command") == 4, "no duplicate dispatch after repeated scans");
        await reactions.SaveAsync(project, new(1, [rules[2]]), default);
        var unverified = await Completed(session);
        await reactions.SweepAsync(DateTimeOffset.UtcNow.AddMinutes(3), default);
        Require((await reactions.ResultsAsync(project, unverified, default)).Single() is { Pending: false, Diagnostic: "release_unverified" },
            "unverifiable release remains visible and allows continuation");
        await reactions.DemandContinuationAsync(project, unverified, default);
        var descriptor = McpToolCatalog.Operations.Single(item => item.Id == "get_project_reactions");
        Require(descriptor.Revision == 1 && McpToolCatalog.Operations.Any(item => item.Id == "send_session_message" && item.Revision == 2),
            "MCP exposes reaction read and opt-in continuation revision");
        Console.WriteLine("PROJECT_REACTIONS=PASS Windows process, exit 0/1/2, release gate, revision, filtering, deduplication");
    }

    private sealed class ReleaseEvidence : IReactionRelease
    {
        public HashSet<Guid> Confirmed { get; } = [];
        public Task<bool> ConfirmAsync(Guid session, Guid request, CancellationToken token) => Task.FromResult(Confirmed.Contains(request));
    }

    private static async Task Reject(string code, Func<Task> action)
    {
        try { await action(); }
        catch (RelayException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }

    private static void Require(bool condition, string invariant)
    {
        if (!condition) throw new InvalidOperationException(invariant);
    }
}
