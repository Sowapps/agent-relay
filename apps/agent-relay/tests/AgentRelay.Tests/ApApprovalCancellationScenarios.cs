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
            [new(project, owner, "main", [RelayCapabilities.ManageSession, RelayCapabilities.ReadHistory], []),
             new(project, peer, "secondary", [], [])]));
        var backend = new Fixture(session);
        await using var work = new WorkService(store, backend, () => [new(peer.ToString("D"), false, true)]);
        var app = new RelayApplication(new(settings), work, backend, new ProjectJournal(database));
        var caller = new CallerIdentity(owner, [project]);
        var token = CancellationToken.None;
        var request = new WorkRequest(Guid.NewGuid(), peer.ToString("D"), "message", "Fixture", SessionId: session.Id, Scope: scope);
        var now = DateTimeOffset.UtcNow;
        var finished = new WorkRecord(request with { Id = Guid.NewGuid() }, "completed", now, now, Result: new("fixture", "done"));
        await store.SaveAsync(finished, token);
        await RefusesAsync("operation_already_finished", () => app.CancelAsync(caller, finished.Request.Id, token));
        Check(await store.FindAsync(finished.Request.Id, token) == finished, "Cancellation changed a finished result.");

        using (WorkspaceLease.Acquire(database, request.Id, scope, session.Id, "owned-surface"))
            await store.SaveAsync(new(request, "awaiting_approval", now, now, ErrorCode: "approval_required"), token);
        await RefusesAsync("operation_cancellation_required", () => app.ControlSessionAsync(caller, session.Id, "close", token));
        var cancelled = await app.CancelAsync(caller, request.Id, token);
        Check(cancelled is { State: "awaiting_approval", CancellationRequestedAt: not null, CancellationConfirmedAt: null } && backend.Stops == 0,
            "Cancellation intent was lost or stopped the terminal without an explicit close.");
        Check((await app.CancelAsync(caller, request.Id, token)).CancellationRequestedAt == cancelled.CancellationRequestedAt,
            "Repeated cancellation rewrote its original timestamp.");
        await RefusesAsync("session_awaiting_approval", () => work.SubmitAsync(request with { Id = Guid.NewGuid() }, token));
        var writer = Guid.NewGuid();
        await RefusesAsync("cancellation_unconfirmed", () => app.ControlSessionAsync(caller, session.Id, "close", token));
        await RefusesAsync("workspace_busy", () => Task.FromResult(WorkspaceLease.Acquire(database, writer, scope, Guid.NewGuid(), "owned-surface")));
        using (WorkspaceLease.Acquire(database, writer, scope, Guid.NewGuid(), "independent-surface")) { }

        backend.OwnedStopVerified = true;
        Check((await app.ControlSessionAsync(caller, session.Id, "close", token)).State == "closed", "Verified native stop did not finish the session.");
        var outcome = await app.ResultAsync(caller, request.Id, token);
        Check(outcome is { State: "unknown", ErrorCode: "cancelled_effects_unconfirmed", CancellationConfirmedAt: not null },
            "The stop must keep historical effects uncertain and record confirmation.");
        using (WorkspaceLease.Acquire(new RelayDatabase(data), writer, scope, Guid.NewGuid(), "owned-surface")) { }
        await RefusesAsync("operation_already_finished", () => app.CancelAsync(caller, request.Id, token));
        await app.ControlSessionAsync(caller, session.Id, "close", token);
        Check(backend.Stops == 1, "Repeating an acknowledged close stopped the terminal again.");

        // A correlated final outcome that wins the storage race cannot be overwritten by a stop confirmation.
        await store.ConfirmApprovalCancellationAsync(finished.Request.Id, token);
        Check(await store.FindAsync(finished.Request.Id, token) == finished, "Stop confirmation overwrote a final result.");
        var native = new SessionService(data, null!, null!, null!);
        var profile = new WslProfile("fixture", "Fixture", "fixture", "fixture", "/fixture", "codex", []);
        await new SqliteDocuments<RelaySession>(database, "sessions").SaveAsync(session.Id,
            new(session.Id, session.Name, profile.Id, profile, profile.Root, "closed", now, now, RetiredForCancellationId: request.Id), token);
        await RefusesAsync("session_retired_for_cancellation", () => native.ReopenAsync(session.Id, token));
        Console.WriteLine("AP_APPROVAL_CANCELLATION=PASS (terminal refusal, durable intent, retained lease, verified close, late-result protection and reopen fence)");
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
        public Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token) => Task.FromResult(retained);
        public Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => Task.FromResult(retained);
        public Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token) => GetAsync(id, token);
        public Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token) => GetAsync(id, token);
        public Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token) => Task.FromResult(new ReadPage<SessionSnapshot>([retained], null, false));
        public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token) => throw new InvalidOperationException("The test must not dispatch work.");
        public Task ConfirmApprovalStopAsync(WorkRequest request, CancellationToken token)
        {
            if (!OwnedStopVerified) throw new RelayException("cancellation_unconfirmed", "Fixture writer remains.", false);
            Stops++;
            retained = retained with { State = "closed" };
            return Task.CompletedTask;
        }
    }
}
