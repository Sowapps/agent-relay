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
            [new(project, owner, "main", [RelayCapabilities.ManageSession, RelayCapabilities.ReadHistory, RelayCapabilities.RunCommand, RelayCapabilities.ReadFile, RelayCapabilities.SendMessage], [new(workspace, true)], ["list"]),
             new(project, peer, "secondary", [RelayCapabilities.ReceiveMessage], [])]));
        var backend = new Fixture(session);
        await using var work = new WorkService(store, backend, () => [new(peer.ToString("D"), true, true)]);
        var router = new SessionAdapterRouter(backend, new Dictionary<string, IRelaySessions> { ["fixture"] = backend },
            (_, _) => Task.FromResult("fixture"));
        var app = new RelayApplication(new(settings), work, router, new ProjectJournal(database));
        var caller = new CallerIdentity(owner, [project]);
        var token = CancellationToken.None;
        var request = new WorkRequest(Guid.NewGuid(), peer.ToString("D"), "message", "Fixture", SessionId: session.Id, Scope: scope);
        var now = DateTimeOffset.UtcNow;
        var finished = new WorkRecord(request with { Id = Guid.NewGuid() }, "completed", now, now, Result: new("fixture", "done"));
        await store.SaveAsync(finished, token);
        await RefusesAsync("operation_already_finished", () => app.CancelAsync(caller, finished.Request.Id, token));
        Check(await store.FindAsync(finished.Request.Id, token) == finished, "Cancellation changed a finished result.");

        var dormant = request with { Id = Guid.NewGuid() };
        await store.SaveAsync(new(dormant, "approval_expired", now, now,
            ErrorCode: "approval_expired", ApprovalExpiredAt: now, ApprovalStopRequestedAt: now), token);
        await RefusesAsync("session_awaiting_approval", () => app.ControlSessionAsync(caller, session.Id, "reopen", token));
        backend.DormancyVerified = true;
        await app.ControlSessionAsync(caller, session.Id, "reopen", token);
        backend.DormancyVerified = false;
        Check(backend.PinnedReopens == 1 && await store.FindAsync(dormant.Id, token) is
            { State: "approval_expired", ApprovalResumeVerifiedAt: not null, NativeTurnEnded: false, CancellationConfirmedAt: null },
            "Verified dormancy keeps expired history and uncertainty, and reopens the pinned session without a replay.");
        var dormantEvents = await new ProjectJournal(database).EventsAsync(new(project), token);
        Check(dormantEvents.Items.Any(item => item.RequestId == dormant.Id && item.State == "approval_dormancy_verified"),
            "Dormancy evidence is retained in the work journal.");

        var undisplayed = request with { Id = Guid.NewGuid() };
        await store.SaveAsync(new(undisplayed, "awaiting_approval", now, now), token);
        Check((await app.ReadSessionTerminalAsync(caller, session.Id, token)).Approval is null,
            "Unknown prompt was presented as a recognized approval.");
        Check((await app.SignalSessionTerminalAsync(caller, session.Id, "escape", null, token)).KeySent,
            "An owned terminal with an unknown prompt did not accept Escape.");
        Check((await work.ReadAsync(undisplayed.Id, token)) is
            { State: "unknown", ErrorCode: "terminal_interruption_unconfirmed", CancellationConfirmedAt: null } &&
            (await work.RetainedApprovalsAsync(session.Id, token)).Single().Request.Id == undisplayed.Id,
            "Escape prematurely released the unverified turn or confirmed cancellation.");
        await store.AttachNativeReplyAsync(undisplayed.Id, scope, session.Id, new("fixture", "reply after Escape"), now, token);

        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var active = Guid.NewGuid();
            await app.MessageAsync(caller, session.Id, active, "Active fixture", null, deadline.Token);
            await backend.ActiveMessage.Task.WaitAsync(deadline.Token);
            await RefusesAsync("approval_stop_mismatch",
                () => app.ControlSessionAsync(caller, session.Id, "close", deadline.Token, finished.Request.Id));
            Check(work.IsSessionActive(session.Id) && (await work.ReadAsync(active, deadline.Token)).CancellationRequestedAt is null,
                "A targeted close for a finished request interrupted another operation.");
            await RefusesAsync("busy", () => app.ControlSessionAsync(caller, session.Id, "close", deadline.Token));
            while (work.IsSessionActive(session.Id))
                await Task.Delay(10, deadline.Token);
            var cancelledByClose = await work.ReadAsync(active, token);
            Check(cancelledByClose is { State: "unknown", CancellationRequestedAt: not null, CancellationConfirmedAt: null },
                "Closure did not request cancellation and retain in-flight uncertainty.");
            await store.AttachNativeReplyAsync(active, scope, session.Id, new("fixture", "completed before interruption"),
                DateTimeOffset.UtcNow, token);
        }

        await store.SaveAsync(new(request, "awaiting_approval", now, now, ErrorCode: "approval_required"), token);
        await RefusesAsync("cancellation_unconfirmed", () => app.ControlSessionAsync(caller, session.Id, "close", token));
        Check((await work.ReadAsync(request.Id, token)).CancellationRequestedAt is not null,
            "Closing a session with a retained approval did not request cancellation.");
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
        backend.OwnedStopVerified = false;
        backend.TerminalClosed = true;
        Check((await app.ControlSessionAsync(caller, session.Id, "close", token)).State == "closed",
            "Terminal closure was blocked by an unverified process stop.");
        var uncertain = await app.ResultAsync(caller, race.Id, token);
        Check(uncertain is { State: "unknown", ErrorCode: "session_closed_stop_unconfirmed", SessionClosedAt: not null,
            CancellationRequestedAt: not null, CancellationConfirmedAt: null },
            "Closing the terminal must end the approval wait and keep the request unresolved.");
        Check(ProjectReply.WithCancellation(uncertain).Cancellation is { State: "stop_unconfirmed", NextAction: null, SessionRequiresResolution: false },
            "The MCP result still treated the terminal as requiring resolution.");
        await app.ControlSessionAsync(caller, session.Id, "close", token);
        Check(backend.CloseAttempts == 4, "Repeating a terminal close attempted native confirmation again.");
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
        backend.RecognizedApproval = true;
        var withoutGrant = await app.ReadSessionTerminalAsync(caller, session.Id, token);
        Check(withoutGrant is { Approval: not null, ObservedAt: not null, CanDecideApproval: false, MissingCapability: RelayCapabilities.DecideApproval },
            "An observed native prompt did not identify the missing approval right.");
        var configuration = settings.Load();
        settings.Save(configuration with { Memberships = configuration.Memberships.Select(member => member.EndpointId == owner
            ? member with { Capabilities = [.. member.Capabilities, RelayCapabilities.DecideApproval] } : member).ToArray() });
        var reviewed = await app.ReadSessionTerminalAsync(caller, session.Id, token);
        Check(reviewed.CanDecideApproval == true &&
            (await app.SignalSessionTerminalAsync(caller, session.Id, "refuse", reviewed.Approval!.Revision, token)).KeySent,
            "The existing right did not allow the exact reviewed refusal.");
        var refusal = ProjectReply.WithCancellation(await app.ResultAsync(caller, expiring.Id, token)).Approval;
        Check(refusal is { ExpiredAt: not null, RefusedAt: not null, TurnEnded: false, SessionClosedAt: null, StopConfirmedAt: null },
            "Expiry, sent refusal and stop evidence were conflated.");
        backend.RecognizedApproval = false;
        await app.ControlSessionAsync(caller, session.Id, "close", token, expiring.Id);
        Check((await app.ResultAsync(caller, expiring.Id, token)) is { State: "approval_expired", CancellationConfirmedAt: not null },
            "Explicitly verified stop erased expiry history.");
        await store.AttachNativeReplyAsync(expiring.Id, scope, session.Id, late, now, token);
        var resolvedExpiry = await app.ResultAsync(caller, expiring.Id, token);
        Check(resolvedExpiry.State == "completed" && resolvedExpiry.Result == late && resolvedExpiry.ApprovalExpiredAt == now,
            "A correlated late reply lost the expiry or its result.");

        await store.RecordApprovalSessionClosureAsync(finished.Request.Id, true, token);
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
        await VerifyRetainedReplyAsync(database, store, request, token);
        var sessionToReuse = Guid.NewGuid();
        var endedRequest = request with { Id = Guid.NewGuid(), SessionId = sessionToReuse };
        await store.SaveAsync(new(endedRequest, "approval_expired", waited, now, ApprovalExpiredAt: now), token);
        Check(await store.BeginApprovalStopAsync(endedRequest.Id, token), "Did not record the intent of the old failed close.");
        await store.RecordApprovalSignalAsync(endedRequest.Id, "escape", token);
        await store.RecordApprovalTurnObservationAsync(endedRequest.Id, ready: false, token);
        Check((await work.RetainedApprovalsAsync(sessionToReuse, token)).Count == 1 &&
            (await work.ReadAsync(endedRequest.Id, token)).NativeTurnEnded, "End of turn alone released an unverified session.");
        await store.RecordApprovalTurnObservationAsync(endedRequest.Id, ready: true, token);
        var canReuse = await work.ReadAsync(endedRequest.Id, token);
        Check((await work.RetainedApprovalsAsync(sessionToReuse, token)).Count == 0 &&
            canReuse is { State: "approval_expired", ApprovalExpiredAt: not null, ApprovalResumeVerifiedAt: not null,
                ApprovalStopRequestedAt: not null, CancellationConfirmedAt: null, TerminalEscapeSentAt: not null } &&
            ProjectReply.WithCancellation(canReuse).Cancellation is { State: "turn_finished", SessionRequiresResolution: false },
            "Reusing the verified session discarded history or claimed a confirmed stop.");
        await VerifyClosedApprovalReopenAsync(database, store, settings, scope, caller, token);
        await VerifyCancellationFollowUpAsync(database, store, settings, scope, caller, token);
        Console.WriteLine("AP_APPROVAL_CANCELLATION=PASS (active cancellation, verified closed-session reopen, late reply, expiry, retained index)");
    }

    private static async Task VerifyCancellationFollowUpAsync(RelayDatabase database, SqliteWorkStore store, ProjectSettingsStore settings,
        OperationScope scope, CallerIdentity caller, CancellationToken token)
    {
        var session = Guid.NewGuid();
        var source = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var backend = new Fixture(new(session, "Follow-up fixture", scope, "ready")) { Activity = "waiting_for_input" };
        await store.SaveAsync(new(new(source, scope.RecipientId.ToString("D"), "message", "first", SessionId: session, Scope: scope),
            "awaiting_approval", now, now, ReplyDispatch: new("test", "test", "test")), token);
        await using var work = new WorkService(store, backend, () => [new(scope.RecipientId.ToString("D"), false, true)]);
        var app = new RelayApplication(new(settings), work, backend, new ProjectJournal(database));
        var pending = await app.CancelAsync(caller, source, token, "Follow-up");
        var followUp = pending.FollowUp!;
        var activity = await app.RequestActivityAsync(caller, pending, token);
        Check(pending.CancellationConfirmedAt is null && pending.State == "awaiting_approval" && activity is
            { State: "waiting_for_input", RequestId: var observed, SessionId: var observedSession } && observed == source && observedSession == session,
            "Ready terminal masked the retained cancellation state.");
        var errors = new List<Exception>();
        var monitor = new CancellationFollowUpMonitor(store, app, errors.Add);
        await monitor.SweepAsync(token);
        Check(await store.FindAsync(followUp.RequestId, token) is null, "Terminal readiness alone sent the instruction.");
        await RefusesAsync("id_conflict", () => app.CancelAsync(caller, source, token, "Different"));
        await store.RecordApprovalTurnObservationAsync(source, true, token);
        backend.Activity = "unknown";
        await new CancellationFollowUpMonitor(store, app, errors.Add).SweepAsync(token);
        Check(await store.FindAsync(followUp.RequestId, token) is null, "Confirmed stop sent to an unavailable terminal.");
        backend.Activity = "waiting_for_input";
        await new CancellationFollowUpMonitor(store, app, errors.Add).SweepAsync(token);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var sent = await work.ReadAsync(followUp.RequestId, deadline.Token);
        while (sent.State is "pending" or "running")
        {
            await Task.Delay(10, deadline.Token);
            sent = await work.ReadAsync(followUp.RequestId, deadline.Token);
        }
        Check(sent is { State: "completed", Result.Output: "received" } && sent.Request is
            { SingleDispatch: true, PreviousRequestId: var previous, SessionId: var destination } && previous == source && destination == session,
            "The follow-up was not delivered and correlated in the original session.");
        Check((await app.CancelAsync(caller, source, token, "Follow-up")).FollowUp is { State: "submitted", RequestId: var repeated } &&
            repeated == followUp.RequestId, "Repeated cancellation assigned another follow-up ID.");
        await new CancellationFollowUpMonitor(store, app, errors.Add).SweepAsync(token);
        Check(backend.FollowUps == 1 && errors.Count == 0, "Repeated scans or requester retries resent the instruction.");
    }

    private static async Task VerifyClosedApprovalReopenAsync(RelayDatabase database, SqliteWorkStore store, ProjectSettingsStore settings,
        OperationScope scope, CallerIdentity caller, CancellationToken token)
    {
        var session = Guid.NewGuid();
        var request = Guid.NewGuid();
        var conversation = Guid.NewGuid().ToString("D");
        var now = DateTimeOffset.UtcNow;
        var profile = new WslProfile("closed-fixture", "Closed fixture", "unused", "unused", "/fixture", "codex", []);
        var sessions = new SqliteDocuments<RelaySession>(database, "sessions");
        await sessions.SaveAsync(session, new(session, "Closed fixture", profile.Id, profile, profile.Root, "closed",
            now, now, RetiredForCancellationId: request), token);
        await store.SaveAsync(new(new(request, scope.RecipientId.ToString("D"), "message", SessionId: session, Scope: scope),
            "failed", now, now, ErrorCode: "session_closed_before_reply", CancellationRequestedAt: now,
            ApprovalStopRequestedAt: now, SessionClosedAt: now), token);
        var backend = new Fixture(new(session, "Closed fixture", scope, "closed", conversation));
        await using var work = new WorkService(store, backend, () => []);
        var router = new SessionAdapterRouter(backend, new Dictionary<string, IRelaySessions> { ["fixture"] = backend },
            (_, _) => Task.FromResult("fixture"));
        var app = new RelayApplication(new(settings), work, router, new ProjectJournal(database));
        await RefusesAsync("session_runtime_still_active", () => app.ControlSessionAsync(caller, session, "reopen", token));
        Check((await sessions.FindAsync(session, token))?.RetiredForCancellationId == request && backend.PinnedReopens == 0,
            "A live old runtime released the session.");
        backend.ClosureVerified = true;
        var reopened = await app.ControlSessionAsync(caller, session, "reopen", token);
        Check(reopened.Id == session && reopened.NativeConversationId == conversation && backend.PinnedReopens == 1,
            "Verified closure did not reopen the same conversation.");
        Check(await sessions.FindAsync(session, token) is { RetiredForCancellationId: null, ReleasedApprovalRequestId: var released } && released == request &&
              await work.ReadAsync(request, token) is { State: "failed", ErrorCode: "session_closed_before_reply",
                  CancellationConfirmedAt: null, ApprovalClosureReleasedAt: not null } &&
              (await work.RetainedApprovalsAsync(session, token)).Count == 0,
            "Reopen lost the outcome or failed to clear both session gates.");
        await app.ControlSessionAsync(caller, session, "reopen", token);
        var history = await new ProjectJournal(database).EventsAsync(new(scope.ProjectId, SessionId: session), token);
        Check(history.Items.Count(item => item.State == "approval_terminal_release_verified") == 1 &&
              backend.PinnedReopens == 1 && !backend.ActiveMessage.Task.IsCompleted,
            "Reopening reprocessed the closed approval or replayed the request.");
    }

    private static async Task VerifyRetainedReplyAsync(RelayDatabase database, SqliteWorkStore store, WorkRequest template, CancellationToken token)
    {
        var request = template with { Id = Guid.NewGuid(), TaskId = Guid.NewGuid() };
        var scope = request.Scope!;
        var session = request.SessionId!.Value;
        var now = DateTimeOffset.UtcNow;
        var running = new WorkRecord(request, "running", now, now);
        await store.SaveAsync(running, token);
        var reply = new InboxMessage(Guid.NewGuid(), request.ProfileId, "fixture", "origin", "genuine", request.Id,
            now, session, scope.ProjectId, scope.RecipientId, scope.SenderId);
        await store.AddInboxAsync(reply with { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Text = "foreign" }, token);
        Check(await store.FindBoundReplyAsync(request, token) is null, "A foreign session supplied a receipt.");
        await store.AddInboxAsync(reply, token);
        await store.AttachNativeReplyAsync(request.Id, scope, session, new("fixture", "genuine"), now, token);
        Check((await store.FindAsync(request.Id, token))?.State == "running", "Import removed the active execution slot.");
        var failure = running with { State = "unknown", ErrorCode = "native_provider_rate_limited", NativeTurnEnded = true };
        await store.SaveAsync(failure, token);
        var inbox = new ScopedInbox(null!, null!, store, null!, null!, null!);
        var completed = await inbox.ReconcileRetainedReplyAsync(failure, token);
        await store.SaveAsync(failure, token);
        Check(await inbox.ReconcileRetainedReplyAsync(failure, token) == completed &&
            completed is { State: "completed", NativeTurnEnded: true, Result.Output: "genuine" },
            "A retained receipt was lost, overwritten by a stale worker, or reconciled with foreign data.");
        var events = await new ProjectJournal(database).EventsAsync(new(scope.ProjectId, TaskId: request.TaskId), token);
        Check(events.Items.Count(item => item.State == "completed") == 1 &&
            events.Items.Any(item => item.ErrorCode == "native_provider_rate_limited"), "The late result lost its error or emitted duplicate completion.");
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
                DROP INDEX work_session_unresolved_approval;
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
            Check(await plan.ReadAsync(token) && plan.GetString(3).Contains("SEARCH work USING INDEX work_session_unresolved_approval", StringComparison.Ordinal),
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
        public bool TerminalClosed { get; set; }
        public bool RecognizedApproval { get; set; }
        public string? Activity { get; set; }
        public int FollowUps { get; private set; }
        public int CloseAttempts { get; private set; }
        public int Stops { get; private set; }
        private int commands;
        public TaskCompletionSource ParallelCommands { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseCommands { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ActiveMessage { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token) => Task.FromResult(retained);
        public Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => Task.FromResult(retained);
        public Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token) => Task.FromResult(Activity is { } activity
            ? retained with { Activity = new(id, activity, DateTimeOffset.UtcNow) } : retained);
        public Task<SessionTerminal> ReadTerminalAsync(Guid id, CancellationToken token) =>
            Task.FromResult(new SessionTerminal("fixture", "Prompt", RecognizedApproval ? new(new('a', 64), "Fixture approval") : null));
        public Task<TerminalSignalReceipt> SignalTerminalAsync(Guid id, string action, string? approvalRevision, CancellationToken token) =>
            Task.FromResult(new TerminalSignalReceipt(action, action == "escape" || action == "refuse" && RecognizedApproval));
        public bool DormancyVerified { get; set; }
        public bool ClosureVerified { get; set; }
        public int PinnedReopens { get; private set; }
        public Task<bool> VerifyDormantApprovalAsync(WorkRequest request, CancellationToken token) => Task.FromResult(DormancyVerified);
        public Task VerifyClosedApprovalAsync(WorkRequest request, CancellationToken token) => ClosureVerified ? Task.CompletedTask :
            throw new RelayException("session_runtime_still_active", "Fixture old runtime remains.", false);
        public Task<SessionSnapshot> ReopenPinnedAsync(Guid id, CancellationToken token)
        {
            PinnedReopens++;
            return GetAsync(id, token);
        }
        public Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token) => GetAsync(id, token);
        public Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token) => Task.FromResult(new ReadPage<SessionSnapshot>([retained], null, false));
        public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        {
            if (request.Scope != retained.Scope) throw new InvalidOperationException("Unexpected fixture scope.");
            if (request.Kind == "message")
            {
                if (request.SingleDispatch)
                {
                    FollowUps++;
                    return new("fixture", "received");
                }
                ActiveMessage.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                throw new InvalidOperationException("Fixture message was not cancelled.");
            }
            if (request.Kind != "command") throw new InvalidOperationException("Unexpected fixture dispatch.");
            if (Interlocked.Increment(ref commands) == 2) ParallelCommands.TrySetResult();
            await ReleaseCommands.Task.WaitAsync(token);
            return new("fixture", "done");
        }
        public Task<bool> CloseApprovalSessionAsync(WorkRequest request, CancellationToken token)
        {
            CloseAttempts++;
            if (!OwnedStopVerified && !TerminalClosed) throw new RelayException("cancellation_unconfirmed", "Fixture terminal remains.", false);
            if (OwnedStopVerified) Stops++;
            retained = retained with { State = "closed" };
            return Task.FromResult(OwnedStopVerified);
        }
    }
}
