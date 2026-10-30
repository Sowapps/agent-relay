using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class SessionScenarios
{
    public static async Task Run(string data)
    {
        var store = new SqliteWorkStore(new RelayDatabase(data));
        var executor = new Blocker();
        var sessionA = Guid.NewGuid(); var sessionB = Guid.NewGuid();
        await using (var work = new WorkService(store, executor, () => [new("codex", false, true)], maximumConcurrency: 2))
        {
            var first = await work.SubmitAsync(new(Guid.NewGuid(), "codex", "message", "first", SessionId: sessionA), CancellationToken.None);
            var second = await work.SubmitAsync(new(Guid.NewGuid(), "codex", "message", "second", SessionId: sessionB), CancellationToken.None);
            if (!work.IsSessionActive(sessionA) || !work.IsSessionActive(sessionB)) throw new Exception("Independent sessions did not run concurrently.");
            await Busy(work, sessionA); await Busy(work, Guid.NewGuid());
            executor.Complete.TrySetResult();
        }
        Console.WriteLine("PASS: independent sessions parallel; same session/host capacity refused");

        var settings = new SettingsStore(data);
        var profile = new WslProfile("codex", "Codex", "Ubuntu-24.04", "sowapps", "/srv/agent-relay-test", "codex", []);
        settings.Save(new(1, [profile]));
        var documents = new SqliteDocuments<RelaySession>(new RelayDatabase(data), "sessions");
        var record = new RelaySession(sessionA, "Named task", profile.Id, profile, "/srv/agent-relay-session-" + sessionA.ToString("N"), "created", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await documents.SaveAsync(sessionA, record, CancellationToken.None);
        var bridge = new WslBridge(); var sessions = new SessionService(data, settings, new(settings, bridge), bridge);
        if (RelayJson.Serialize(await sessions.CreateAsync(sessionA, record.Name, profile.Id, CancellationToken.None)) != RelayJson.Serialize(record)) throw new Exception("Existing session creation was not idempotent.");
        await sessions.RenameAsync(sessionA, "Renamed task", CancellationToken.None);
        if ((await sessions.FindAsync(sessionA, CancellationToken.None)).Name != "Renamed task") throw new Exception("Session name not retained.");
        if ((await sessions.ResolveAsync(sessionA, profile.Id, CancellationToken.None)).Root != record.Root) throw new Exception("Session root routing changed.");
        settings.Save(new(1, [profile with { AllowLocalWork = true }]));
        try { await sessions.ResolveAsync(sessionA, profile.Id, CancellationToken.None); throw new Exception("Permissions changed silently."); }
        catch (RelayException error) when (error.Code == "capability_denied") { }
        Console.WriteLine("PASS: persisted session metadata/idempotence/root isolation/permission change guard");

        var older = new WorkRecord(new(Guid.NewGuid(), "codex", "message", "old pending"), "running", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.SaveAsync(older, CancellationToken.None);
        for (var i = 0; i < 205; i++) await store.SaveAsync(new(new(Guid.NewGuid(), "codex", "message", "completed"), "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
        await store.RecoverAsync(CancellationToken.None);
        if ((await store.FindAsync(older.Request.Id, CancellationToken.None))?.State != "unknown") throw new Exception("Recovery omitted work outside the UI's last200 rows.");
        Console.WriteLine("PASS: recovery includes unfinished work beyond display window");

        var database = new RelayDatabase(data);
        var page = await JournalReader.ReadAsync<WorkRecord>(database, "work", new(SessionId: sessionA, Limit: 1), CancellationToken.None);
        if (page.Items.Count != 1 || page.Items[0].Request.SessionId != sessionA) throw new Exception("Session query leaked another context.");
        var all = await JournalReader.ReadAsync<WorkRecord>(database, "work", new(Limit: 100), CancellationToken.None);
        var next = await JournalReader.ReadAsync<WorkRecord>(database, "work", new(Cursor: all.NextCursor, Limit: 100), CancellationToken.None);
        if (!all.HasMore || all.Items.Select(item => item.Request.Id).Intersect(next.Items.Select(item => item.Request.Id)).Any()) throw new Exception("Cursor repeated rows.");
        var messages = new[] { new InboxMessage(Guid.NewGuid(), "codex", "codex-wsl", "windows-pilot", "independent", null, DateTimeOffset.UtcNow, sessionA),
            new InboxMessage(Guid.NewGuid(), "codex", "codex-wsl", "windows-pilot", "reply", Guid.NewGuid(), DateTimeOffset.UtcNow, sessionA) };
        foreach (var message in messages) await store.AddInboxAsync(message, CancellationToken.None);
        var firstInbox = await JournalReader.ReadAsync<InboxMessage>(database, "inbox", new(SessionId: sessionA, Limit: 1), CancellationToken.None, incremental: true);
        var secondInbox = await JournalReader.ReadAsync<InboxMessage>(database, "inbox", new(SessionId: sessionA, Cursor: firstInbox.NextCursor), CancellationToken.None, incremental: true);
        var caughtUp = await JournalReader.ReadAsync<InboxMessage>(database, "inbox", new(Cursor: secondInbox.NextCursor), CancellationToken.None, incremental: true);
        if (!firstInbox.HasMore || firstInbox.Items[0].Id != messages[0].Id || secondInbox.Items[0].Id != messages[1].Id || caughtUp.Items.Count != 0 || caughtUp.NextCursor != secondInbox.NextCursor)
            throw new Exception("Incremental inbox cursor lost or repeated a message.");
        Console.WriteLine("PASS: filtered result pages and consumer-owned incremental inbox cursor");

        await using (var interrupted = new WorkService(store, new Blocker(), () => [new("codex", false, true)]))
        {
            var request = new WorkRequest(Guid.NewGuid(), "codex", "message", "interrupt", SessionId: sessionA);
            await interrupted.SubmitAsync(request, CancellationToken.None);
            await interrupted.InterruptSessionAsync(sessionA, CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (interrupted.IsSessionActive(sessionA) && DateTime.UtcNow < deadline) await Task.Delay(10);
            if ((await interrupted.ReadAsync(request.Id, CancellationToken.None)).State != "unknown") throw new Exception("Interrupted native effects were falsely confirmed.");
            if ((await interrupted.SubmitAsync(request, CancellationToken.None)).State != "unknown") throw new Exception("Interrupted operation was replayed.");
        }
        Console.WriteLine("PASS: targeted interruption retains unknown outcome and cannot replay");
        var workspace = Path.Combine(data, "path-workspace"); Directory.CreateDirectory(workspace);
        foreach (var invalid in new[] { "../outside", "nested/../../outside", "C:\\outside.txt", "a:stream", "a//b" })
        {
            try { FileTransferService.WorkspacePath(workspace, invalid); throw new Exception("Escaping transfer path accepted."); }
            catch (RelayException error) when (error.Code == "invalid_path") { }
        }
        if (FileTransferService.WorkspacePath(workspace, "nested/file.txt") != Path.Combine(workspace, "nested", "file.txt")) throw new Exception("Valid workspace file rejected.");
        Console.WriteLine("PASS: transfer paths reject traversal/absolute paths/alternate streams");
    }

    private static async Task Busy(WorkService work, Guid session)
    {
        try { await work.SubmitAsync(new(Guid.NewGuid(), "codex", "message", "busy", SessionId: session), CancellationToken.None); throw new Exception("Expected busy refusal."); }
        catch (RelayException error) when (error.Code == "busy") { }
    }

    private sealed class Blocker : IWorkExecutor
    {
        public TaskCompletionSource Complete { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        { await Complete.Task.WaitAsync(token); return new("fixture", request.Text); }
    }
}
