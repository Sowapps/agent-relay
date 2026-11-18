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
            var commands = new[] { Guid.NewGuid(), Guid.NewGuid() };
            try
            {
                foreach (var id in commands)
                    await app.CommandAsync(caller, project, peer, workspace, "list", id, deadline.Token);
                await backend.ParallelCommands.Task.WaitAsync(deadline.Token);
                await RefusesAsync("session_awaiting_approval", () => work.SubmitAsync(request with { Id = Guid.NewGuid() }, token));
                await RefusesAsync("session_awaiting_approval", () => app.ControlSessionAsync(caller, session.Id, "start", token));
                await RefusesAsync("session_awaiting_approval", () => app.ControlSessionAsync(caller, session.Id, "reopen", token));
            }
            finally { backend.ReleaseCommands.TrySetResult(); }
            foreach (var id in commands)
            {
                WorkRecord commandOutcome;
                while ((commandOutcome = await work.ReadAsync(id, deadline.Token)).State is "pending" or "running")
                    await Task.Delay(10, deadline.Token);
                Check(commandOutcome.State == "completed", "Concurrent fixture command did not finish.");
            }
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
        await store.AttachNativeReplyAsync(race.Id, scope, session.Id, late, now, token);
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

        var expiring = request with { Id = Guid.NewGuid() };
        var waited = now - ApprovalExpiry.WaitingLimit;
        await store.SaveAsync(new(expiring, "awaiting_approval", waited, waited), token);
        await store.ExpireDueApprovalsAsync(now.AddTicks(-1), token);
        Check((await store.FindAsync(expiring.Id, token))?.State == "awaiting_approval", "Approval expired before its deadline.");
        await store.ExpireDueApprovalsAsync(now, token);
        await store.ExpireDueApprovalsAsync(now.AddSeconds(30), token);
        var expired = await app.ResultAsync(caller, expiring.Id, token);
        Check(expired is { State: "approval_expired", CancellationConfirmedAt: null } &&
            expired.ApprovalExpiredAt == now && expired.ApprovalWaitingSince == waited && backend.Stops == 2,
            "Expiry must retain the original wait and leave the native session alone.");
        var events = await new ProjectJournal(database).EventsAsync(new(project, SessionId: session.Id), token);
        Check(events.Items.Count(item => item.RequestId == expiring.Id && item.State == "approval_expired") == 1,
            "Expiry was not journalled exactly once.");
        await RefusesAsync("session_awaiting_approval", () => app.ControlSessionAsync(caller, session.Id, "start", token));
        await RefusesAsync("update_approval_pending", async () => { await SqliteWorkStore.DemandUpdateIdleAsync(data, token, now); return true; });
        await SqliteWorkStore.DemandUpdateIdleAsync(data, token, now.AddMinutes(-1), item => item.Request.Id == expiring.Id);
        await app.ControlSessionAsync(caller, session.Id, "close", token, expiring.Id);
        Check((await app.ResultAsync(caller, expiring.Id, token)) is { State: "approval_expired", CancellationConfirmedAt: not null },
            "Explicitly verified stop erased expiry history.");
        await store.AttachNativeReplyAsync(expiring.Id, scope, session.Id, late, now, token);
        var resolvedExpiry = await app.ResultAsync(caller, expiring.Id, token);
        Check(resolvedExpiry.State == "completed" && resolvedExpiry.Result == late && resolvedExpiry.ApprovalExpiredAt == now,
            "A correlated late reply lost the expiry or its result.");

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
        await VerifyReceiptTimingAsync(database, store, request, token);
        await VerifyRetainedApprovalIndexAsync(database, store, request, token);
        Console.WriteLine("AP_APPROVAL_CANCELLATION=PASS (guarded session, receipt ordering, durable expiry, retained approval index)");
    }

    private static async Task VerifyReceiptTimingAsync(RelayDatabase database, SqliteWorkStore store, WorkRequest template, CancellationToken token)
    {
        var deadline = DateTimeOffset.UtcNow.AddHours(-2);
        var started = deadline - ApprovalExpiry.WaitingLimit;
        var scope = template.Scope!;
        var session = template.SessionId!.Value;
        var result = new WorkResult("fixture", "authentic");
        var journal = new ProjectJournal(database);
        foreach (var offset in new[] { -1L, 0L, 1L, long.MinValue })
        foreach (var sweepFirst in new[] { false, true })
        {
            var request = template with { Id = Guid.NewGuid(), TaskId = Guid.NewGuid() };
            var receipt = offset == long.MinValue ? started.AddTicks(-1) : deadline.AddTicks(offset);
            await store.SaveAsync(new(request, "awaiting_approval", started, started), token);
            if (sweepFirst) await store.ExpireDueApprovalsAsync(deadline, token);
            var armedStop = sweepFirst && offset == -1;
            if (armedStop) Check(await store.BeginApprovalStopAsync(request.Id, token), "Could not arm the expired approval stop.");
            await store.AttachNativeReplyAsync(request.Id, scope, session, result, receipt, token);
            await store.ExpireDueApprovalsAsync(deadline, token);
            await store.AttachNativeReplyAsync(request.Id, scope, session, result, receipt, token);
            var saved = (await store.FindAsync(request.Id, token))!;
            var expectExpiry = offset >= 0 || offset == long.MinValue && sweepFirst;
            Check(saved.State == "completed" && saved.Result == result && saved.ApprovalExpiredAt == (expectExpiry ? deadline : (DateTimeOffset?)null),
                "Receipt timing changed when import and expiry competed.");
            if (offset != long.MinValue) Check(saved.ApprovalWaitingSince == started, "Receipt ordering changed the approval start.");
            Check(WorkExecution.ApprovalStopPending(saved) == armedStop, "A timely reply erased an armed native stop.");
            var events = (await journal.EventsAsync(new(scope.ProjectId, TaskId: request.TaskId), token)).Items;
            Check(events.Count(item => item.State == "approval_expired" && item.OccurredAt == deadline) == (sweepFirst || offset >= 0 ? 1 : 0),
                "Expiry history was duplicated or lost.");
            var diagnostic = offset == long.MinValue ? "approval_receipt_time_unverified" :
                offset == -1 && sweepFirst ? "approval_receipt_before_deadline" : null;
            Check(events.Single(item => item.State == "completed").ErrorCode == diagnostic, "Receipt diagnostic lost.");
        }
    }

    private static async Task VerifyRetainedApprovalIndexAsync(RelayDatabase database, SqliteWorkStore store, WorkRequest template, CancellationToken token)
    {
        var request = template with { Id = Guid.NewGuid(), SessionId = Guid.NewGuid() };
        var now = DateTimeOffset.UtcNow;
        await store.SaveAsync(new(request, "approval_expired", now, now, ApprovalExpiredAt: now), token);
        using (var connection = database.Open())
        using (var seed = connection.CreateCommand())
        {
            seed.CommandText = """
                DROP INDEX work_session_retained_approval;
                WITH RECURSIVE rows(id) AS (VALUES(1) UNION ALL SELECT id+1 FROM rows WHERE id < 10000)
                INSERT INTO work SELECT 'history-' || id, json_object('state','completed') FROM rows;
                """;
            await seed.ExecuteNonQueryAsync(token);
        }
        var migrated = new RelayDatabase(database.DataDirectory);
        using (var connection = migrated.Open())
        using (var explain = connection.CreateCommand())
        {
            explain.CommandText = "EXPLAIN QUERY PLAN " + SqliteWorkStore.RetainedApprovalsQuery;
            explain.Parameters.AddWithValue("$id", request.SessionId!.Value.ToString("D"));
            using var plan = await explain.ExecuteReaderAsync(token);
            Check(await plan.ReadAsync(token) && plan.GetString(3).Contains("SEARCH work USING INDEX work_session_retained_approval", StringComparison.Ordinal),
                "The retained approval lookup scans the work history.");
        }
        var approvals = await new SqliteWorkStore(migrated).RetainedApprovalsAsync(request.SessionId!.Value, token);
        Check(approvals.Single().Request.Id == request.Id, "The index changed the retained approval result.");
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
