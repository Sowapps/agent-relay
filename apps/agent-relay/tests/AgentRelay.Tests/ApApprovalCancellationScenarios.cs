using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class ApApprovalCancellationScenarios
{
    internal static async Task RunAsync(string data)
    {
        var project = Guid.NewGuid(); var owner = Guid.NewGuid(); var peer = Guid.NewGuid(); var workspace = Guid.NewGuid();
        var scope = new OperationScope(project, owner, peer, workspace);
        var session = new SessionSnapshot(Guid.NewGuid(), "Fixture", scope, "ready");
        var database = new RelayDatabase(data);
        var store = new SqliteWorkStore(database);
        var settings = new ProjectSettingsStore(data);
        settings.Save(new(1, [new(project, "Fixture project")], [new(owner, "Owner", "fixture", "owner"), new(peer, "Peer", "fixture", "peer")],
            [new(workspace, peer, "Fixture workspace", data)],
            [new(project, owner, "main", [RelayCapabilities.ManageSession, RelayCapabilities.ReadHistory, RelayCapabilities.RunCommand, RelayCapabilities.ReadFile], [new(workspace, true)], ["list"]),
             new(project, peer, "secondary", [], [])]));
        var backend = new Fixture(session);
        await using var work = new WorkService(store, backend, () => [new(peer.ToString("D"), true, true)]);
        var app = new RelayApplication(new(settings), work, backend, new ProjectJournal(database));
        var caller = new CallerIdentity(owner, [project]);
        var token = CancellationToken.None;
        var request = new WorkRequest(Guid.NewGuid(), peer.ToString("D"), "message", "Fixture", SessionId: session.Id, Scope: scope);
        var now = DateTimeOffset.UtcNow;
        var finished = new WorkRecord(request with { Id = Guid.NewGuid() }, "completed", now, now, Result: new("fixture", "done"));
        await store.SaveAsync(finished, token);
        await RefusesAsync("operation_already_finished", () => app.CancelAsync(caller, finished.Request.Id, token));
        Check(await store.FindAsync(finished.Request.Id, token) == finished, "Cancellation changed a finished result.");

        await store.SaveAsync(new(request, "awaiting_approval", now, now, ErrorCode: "approval_required"), token);
        await RefusesAsync("operation_cancellation_required", () => app.ControlSessionAsync(caller, session.Id, "close", token));
        var cancelled = await app.CancelAsync(caller, request.Id, token);
        Check(cancelled is { State: "awaiting_approval", CancellationRequestedAt: not null, CancellationConfirmedAt: null } && backend.Stops == 0,
            "Cancellation intent was lost or stopped the terminal without an explicit close.");
        Check((await app.CancelAsync(caller, request.Id, token)).CancellationRequestedAt == cancelled.CancellationRequestedAt,
            "Repeated cancellation rewrote its original timestamp.");
        await RefusesAsync("session_awaiting_approval", () => work.SubmitAsync(request with { Id = Guid.NewGuid() }, token));
        await RefusesAsync("cancellation_unconfirmed", () => app.ControlSessionAsync(caller, session.Id, "close", token));
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            try
            {
                await app.CommandAsync(caller, project, peer, workspace, "list", Guid.NewGuid(), deadline.Token);
                await app.CommandAsync(caller, project, peer, workspace, "list", Guid.NewGuid(), deadline.Token);
                await backend.ParallelCommands.Task.WaitAsync(deadline.Token);
                await RefusesAsync("session_awaiting_approval", () => work.SubmitAsync(request with { Id = Guid.NewGuid() }, token));
                await RefusesAsync("session_awaiting_approval", () => app.ControlSessionAsync(caller, session.Id, "start", token));
                await RefusesAsync("session_awaiting_approval", () => app.ControlSessionAsync(caller, session.Id, "reopen", token));
            }
            finally { backend.ReleaseCommands.TrySetResult(); }
        }

        backend.OwnedStopVerified = true;
        Check((await app.ControlSessionAsync(caller, session.Id, "close", token)).State == "closed", "Verified native stop did not finish the session.");
        var outcome = await app.ResultAsync(caller, request.Id, token);
        Check(outcome is { State: "unknown", ErrorCode: "cancelled_effects_unconfirmed", CancellationConfirmedAt: not null },
            "The stop must keep historical effects uncertain and record confirmation.");
        await RefusesAsync("operation_already_finished", () => app.CancelAsync(caller, request.Id, token));
        await app.ControlSessionAsync(caller, session.Id, "close", token);
        Check(backend.Stops == 1, "Repeating an acknowledged close stopped the terminal again.");

        // The worker has stopped reporting and the native stop is interrupted before proof arrives.
        var race = request with { Id = Guid.NewGuid() };
        await store.SaveAsync(new(race, "awaiting_approval", now, now), token);
        await app.CancelAsync(caller, race.Id, token);
        backend.OwnedStopVerified = false;
        await RefusesAsync("cancellation_unconfirmed", () => app.ControlSessionAsync(caller, session.Id, "close", token));
        var late = new WorkResult("fixture", "original late result");
        await store.AttachNativeReplyAsync(race.Id, scope, session.Id, late, token);
        var restarted = new RelayDatabase(data);
        var lateOutcome = (await new SqliteWorkStore(restarted).FindAsync(race.Id, token))!;
        Check(lateOutcome.Result == late && lateOutcome.State == "completed" && WorkExecution.ApprovalStopPending(lateOutcome),
            "The late result erased a durable unverified stop.");
        await RefusesAsync("session_awaiting_approval", () => work.SubmitAsync(request with { Id = Guid.NewGuid() }, token));
        backend.OwnedStopVerified = true;
        await RefusesAsync("approval_stop_mismatch", () => app.ControlSessionAsync(caller, session.Id, "close", token, finished.Request.Id));
        await app.ControlSessionAsync(caller, session.Id, "close", token, race.Id);
        var final = await app.ResultAsync(caller, race.Id, token);
        Check(final.Result == late && final.State == "completed" && final.CancellationConfirmedAt is not null && !WorkExecution.MayContinue(final),
            "Verified stop must preserve the genuine result.");
        await app.ControlSessionAsync(caller, session.Id, "close", token, race.Id);
        Check(backend.Stops == 2, "Retry repeated a verified stop.");

        await store.ConfirmApprovalCancellationAsync(finished.Request.Id, token);
        Check(await store.FindAsync(finished.Request.Id, token) == finished, "Stop confirmation overwrote a final result.");
        var native = new SessionService(data, null!, null!, null!);
        var profile = new WslProfile("fixture", "Fixture", "fixture", "fixture", "/fixture", "codex", []);
        await new SqliteDocuments<RelaySession>(database, "sessions").SaveAsync(session.Id,
            new(session.Id, session.Name, profile.Id, profile, profile.Root, "closed", now, now, RetiredForCancellationId: request.Id), token);
        await RefusesAsync("session_retired_for_cancellation", () => native.ReopenAsync(session.Id, token));
        var legacy = finished with { Request = finished.Request with { Id = Guid.NewGuid() }, CancellationRequestedAt = now };
        await store.SaveAsync(legacy, token);
        await new SqliteDocuments<RelaySession>(database, "sessions").SaveAsync(session.Id,
            new(session.Id, session.Name, profile.Id, profile, profile.Root, "created", now, now, RetiredForCancellationId: legacy.Request.Id), token);
        var adopted = await new SqliteWorkStore(new RelayDatabase(data)).FindAsync(legacy.Request.Id, token);
        Check(adopted is not null && WorkExecution.ApprovalStopPending(adopted), "An older retained session fence was discarded during upgrade.");
        Console.WriteLine("AP_APPROVAL_CANCELLATION=PASS (parallel commands on the same workspace during uncertain stop, guarded session and late reply)");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static async Task RefusesAsync<T>(string code, Func<Task<T>> action)
    {
        try { await action(); }
        catch (RelayException error) when (error.Code == code && error.EffectsPossible == false) { return; }
        throw new InvalidOperationException("Expected " + code);
    }

    private sealed class Fixture(SessionSnapshot session) : IRelaySessions, IWorkExecutor
    {
        private SessionSnapshot retained = session;
        public bool OwnedStopVerified { get; set; }
        public int Stops { get; private set; }
        private int commands;
        public TaskCompletionSource ParallelCommands { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCommands { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token) => Task.FromResult(retained);
        public Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => Task.FromResult(retained);
        public Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token) => GetAsync(id, token);
        public Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token) => GetAsync(id, token);
        public Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token) => Task.FromResult(new ReadPage<SessionSnapshot>([retained], null, false));
        public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        {
            if (request.Kind != "command" || request.Scope != retained.Scope) throw new InvalidOperationException("Unexpected fixture dispatch.");
            if (Interlocked.Increment(ref commands) == 2) ParallelCommands.TrySetResult();
            await ReleaseCommands.Task.WaitAsync(token);
            return new("fixture", "done");
        }
        public Task ConfirmApprovalStopAsync(WorkRequest request, CancellationToken token)
        {
            if (!OwnedStopVerified) throw new RelayException("cancellation_unconfirmed", "Fixture writer remains.", false);
            Stops++;
            retained = retained with { State = "closed" };
            return Task.CompletedTask;
        }
    }
}
