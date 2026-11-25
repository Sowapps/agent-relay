using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Owns scoped execution and recovery orchestration; concrete dependencies come from ProjectCoordinator.
public sealed class ScopedWorkExecutor(RelayDatabase database, ProjectSettingsStore projects, SessionService sessions, WslBridge bridge,
    SqliteWorkStore store, FileTransferService files, ScopedFileRoutes fileRoutes, ScopedInbox inbox, ScopedCommands commands, ScopedSessionContext context)
    : IScopedWorkAdapter, IRetryPreparation, IReplyObservation
{
    public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
    {
        using var prepared = await PrepareAsync(request, token);
        return await prepared.ExecuteAsync(token);
    }

    public Task<IPreparedWork> PrepareAsync(WorkRequest request, CancellationToken token) => PrepareAsync(request, null, 1, token);

    public Task<IPreparedWork> PrepareRetryAsync(WorkRecord work, CancellationToken token) =>
        work is { Request.Kind: "message", Retry: { } retry, NativeAttempts.Length: >= 1 and <= 2 }
            ? PrepareAsync(work.Request, retry, work.NativeAttempts.Length + 1, token)
            : throw new RelayException("native_retry_unverified", "The retained retry lacks a verified native attempt.", false);

    private async Task<IPreparedWork> PrepareAsync(WorkRequest request, NativeRetry? retry, int attempt, CancellationToken token)
    {
        var scope = request.Scope ?? throw new RelayException("legacy_unassigned", "Production work needs a project scope.");
        var policy = new ProjectPolicy(projects);
        var caller = new CallerIdentity(scope.SenderId, [scope.ProjectId]);
        if (request.Kind == "command") return await commands.PrepareAsync(caller, request, token);
        var record = await context.RecordAsync(request.SessionId!.Value, token);
        var retainedScope = record.Definition!.Scope;
        if (!ProjectPolicy.IsSessionRequest(request, record.Definition))
            throw new RelayException("capability_denied", "Session and operation contexts differ.", false);
        context.ValidateDefinition(record.Definition);
        var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token);
        if (request.Kind == "transfer")
            return await fileRoutes.PrepareAsync(request, record, profile, token);
        void DemandMessage()
        {
            using var snapshot = policy.Pin();
            policy.DemandSession(caller, new(record.Id, record.Name, retainedScope, record.State), RelayCapabilities.SendMessage);
            policy.Message(caller, scope.ProjectId, scope.RecipientId, scope.WorkspaceId);
            if (record.Definition.AllowLocalWork && scope.SenderId != retainedScope.SenderId)
                policy.LocalWork(caller, scope.ProjectId, scope.WorkspaceId!.Value, scope.RecipientId);
        }
        DemandMessage();
        if (retry is null)
        {
            var identity = await WorkspaceIdentity.VerifyNativeAsync(database, profile, record.Definition.Workspace, token);
            var ready = await sessions.EnsureReadyAsync(record, profile, policy.CanControlSession(caller,
                new(record.Id, record.Name, retainedScope, record.State), RelayCapabilities.ManageSession), token);
            retry = new(DateTimeOffset.MinValue, ready.NativeRuntime?.Generation ?? "", ready.DistributionRuntime?.Generation ?? "", identity);
        }
        context.ValidateDefinition(record.Definition);
        return new ScopedPreparedWork(dispatchToken => ExecuteMessageAsync(request, record, profile, DemandMessage, retry, attempt,
            token => bridge.CallAsync(profile, "submit_message", new { request.Id, request.Text, profile.AllowLocalWork, Scope = retainedScope,
                ExpectedNativeGeneration = retry.NativeGeneration, ExpectedDistributionGeneration = retry.DistributionGeneration,
                ExpectedWorkspaceIdentity = retry.WorkspaceIdentity, NativeAttempt = attempt }, token), dispatchToken));
    }

    private async Task<WorkResult> ExecuteMessageAsync(WorkRequest request, RelaySession session, WslProfile profile,
        Action demandMessage, NativeRetry contextAtDispatch, int attempt, Func<CancellationToken, Task<System.Text.Json.JsonElement>> send, CancellationToken token)
    {
        var sender = request.Scope!.RecipientId.ToString("D");
        token.ThrowIfCancellationRequested();
        context.ValidateDefinition(session.Definition!);
        demandMessage();
        if (attempt > 1)
        {
            if (await store.FindBoundReplyAsync(request, token) is { } retained)
                return new(sender, retained.Text);
            var recovery = await bridge.CallAsync(profile, "recover", new { request.Id }, token);
            if (recovery.GetProperty("reply").ValueKind != System.Text.Json.JsonValueKind.Null)
                return await ImportReplyAsync(recovery);
        }
        var pending = new NativeReplyDispatch(contextAtDispatch.NativeGeneration, contextAtDispatch.DistributionGeneration,
            contextAtDispatch.WorkspaceIdentity);
        if (!await store.BeginNativeAttemptAsync(request.Id, attempt, token, pending))
            throw new RelayException("native_retry_interrupted", "The request changed before another native attempt was admitted.");
        System.Text.Json.JsonElement result;
        try
        {
            result = await send(token);
        }
        catch (RelayException error) when (error.EffectsPossible == false)
        {
            await store.FinishNativeAttemptAsync(request.Id, attempt, false, error.Code, null, CancellationToken.None);
            throw;
        }
        if (result.TryGetProperty("submitted", out var submitted) && submitted.ValueKind == System.Text.Json.JsonValueKind.True)
        {
            await store.MarkAwaitingReplyAsync(request.Id, CancellationToken.None);
            throw new WorkDeferredException();
        }
        var reused = result.TryGetProperty("attemptDispatched", out var dispatched) && dispatched.ValueKind == System.Text.Json.JsonValueKind.False;
        await store.FinishNativeAttemptAsync(request.Id, attempt, !reused, null, null, CancellationToken.None);
        return await ImportReplyAsync(result);

        async Task<WorkResult> ImportReplyAsync(System.Text.Json.JsonElement response)
        {
            await inbox.ImportAsync(session, response, token);
            var reply = response.GetProperty("reply");
            if (reply.GetProperty("inReplyTo").GetGuid() != request.Id) throw new InvalidDataException("Reply correlation failed.");
            return new(sender, reply.GetProperty("text").GetString()!);
        }
    }

    public async Task ObserveReplyAsync(WorkRecord work, CancellationToken token)
    {
        if (work is not { Request.SessionId: { } id, ReplyDispatch: { } dispatch, NativeAttempts: [.., { } attempt] }) return;
        var retained = await inbox.ReconcileRetainedReplyAsync(work, token);
        if (!SqliteWorkStore.IsAwaitingReply(retained)) return;
        var session = await context.RecordAsync(id, token);
        if (session.Definition is not { } definition || !ProjectPolicy.IsSessionRequest(work.Request, definition))
            throw new InvalidDataException("The retained dispatch no longer matches its original session.");
        try
        {
            var profile = await sessions.ResolveAsync(id, session.ProfileId, token, allowClosed: true);
            var response = await bridge.CallAsync(profile, "observe_message", new { work.Request.Id, NativeAttempt = attempt.Number },
                token, TimeSpan.FromSeconds(5));
            if (response.TryGetProperty("messages", out _))
            {
                await inbox.ImportAsync(session, response, token);
                return;
            }
            if (session.State == "closed")
                throw new RelayException("session_closed_before_reply", "The session closed before a reply was confirmed.");
        }
        catch (RelayException error) when (error.Code == "approval_required")
        {
            await store.RecordReplyApprovalAsync(work.Request.Id, token);
        }
        catch (RelayException error) when (error.Code is "session_closed_before_reply" or
            "native_reply_missing" or "server_overloaded" or "native_provider_rate_limited" or "native_turn_failed")
        {
            var delay = RetryDelay(attempt.Number, error);
            var retry = delay is { } wait ? new NativeRetry(DateTimeOffset.UtcNow + wait, dispatch.NativeGeneration,
                dispatch.DistributionGeneration, dispatch.WorkspaceIdentity) : null;
            await store.FinishNativeAttemptAsync(work.Request.Id, attempt.Number, true, error.Code, retry, token, error);
        }
    }

    internal static TimeSpan? RetryDelay(int attempt, Exception error)
    {
        if (attempt is < 1 or > 2 || error is not RelayException
            { Code: "server_overloaded" or "native_provider_rate_limited", EffectsPossible: false, NativeTurnEnded: true, NativeRetryDelay: { } provider })
            return null;
        var local = TimeSpan.FromSeconds(attempt == 1 ? 10 : 30);
        return provider > local ? provider : local;
    }

    public async Task<WorkRecord> ReconcileAsync(WorkRecord record, CancellationToken token)
    {
        if (record.Request.Scope is null) return record;
        if (record.Request.Kind == "message")
        {
            if (record.State == "unknown") record = await inbox.ReconcileRetainedReplyAsync(record, token);
            return WorkExecution.NeedsApprovalObservation(record) ? await ObserveApprovalTurnAsync(record, token) : record;
        }
        if (record.State != "unknown") return record;
        WorkResult? receipt = null;
        if (record.Request.Kind == "transfer") receipt = await files.ReceiptAsync(record.Request, token);
        else if (record.Request.Kind == "command") receipt = await commands.RecoverAsync(record.Request, token);
        if (receipt is null) return record;
        var updated = record with { State = receipt.ExitCode is not (null or 0) ? "failed" : "completed", Result = receipt, Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(updated, token);
        return updated;
    }

    private async Task<WorkRecord> ObserveApprovalTurnAsync(WorkRecord work, CancellationToken token)
    {
        if (work.Request.SessionId is not { } id) return work;
        var session = await context.RecordAsync(id, token);
        if (session.Definition is not { } definition || !ProjectPolicy.IsSessionRequest(work.Request, definition)) return work;
        bool ready;
        try
        {
            context.ValidateDefinition(definition);
            var profile = await sessions.ResolveAsync(id, session.ProfileId, token, allowClosed: true);
            var observed = await bridge.CallAsync(profile, "inspect_turn", new { RequestId = work.Request.Id }, token);
            if (!observed.TryGetProperty("turnEnded", out var ended) || ended.ValueKind != System.Text.Json.JsonValueKind.True) return work;
            ready = session.RetiredForCancellationId is null && session.State != "closed" &&
                observed.TryGetProperty("sessionReady", out var available) && available.ValueKind == System.Text.Json.JsonValueKind.True;
        }
        catch (Exception error) when (error is RelayException or IOException or TimeoutException)
        {
            // Inaccessible or older native histories provide no evidence of termination.
            return work;
        }
        await store.RecordApprovalTurnObservationAsync(work.Request.Id, ready, token);
        return await store.FindAsync(work.Request.Id, token) ?? work;
    }
    public async Task<TransferPartial> PartialAsync(WorkRequest request, bool remove, CancellationToken token)
    {
        var record = await context.RecordAsync(request.SessionId!.Value, token);
        context.ValidateDefinition(record.Definition!);
        var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token, allowClosed: true);
        return await fileRoutes.PartialAsync(request, record, profile, remove, token);
    }
}

internal sealed class ScopedPreparedWork(Func<CancellationToken, Task<WorkResult>> execute) : IPreparedWork
{
    public Task<WorkResult> ExecuteAsync(CancellationToken token) => execute(token);
    public void Dispose() { }
}
