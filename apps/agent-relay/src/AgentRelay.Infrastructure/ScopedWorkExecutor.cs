using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Owns scoped execution and recovery orchestration; concrete dependencies come from ProjectCoordinator.
public sealed class ScopedWorkExecutor(RelayDatabase database, ProjectSettingsStore projects, SessionService sessions, WslBridge bridge,
    SqliteWorkStore store, FileTransferService files, ScopedFileRoutes fileRoutes, ScopedInbox inbox, ScopedCommands commands, ScopedSessionContext context)
    : IScopedWorkAdapter
{
    public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
    {
        using var prepared = await PrepareAsync(request, token);
        return await prepared.ExecuteAsync(token);
    }

    public async Task<IPreparedWork> PrepareAsync(WorkRequest request, CancellationToken token)
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
        var identity = await WorkspaceIdentity.VerifyNativeAsync(database, profile, record.Definition.Workspace, token);
        var ready = await sessions.EnsureReadyAsync(record, profile, policy.CanControlSession(caller,
            new(record.Id, record.Name, retainedScope, record.State), RelayCapabilities.ManageSession), token);
        context.ValidateDefinition(record.Definition);
        return new ScopedPreparedWork(async dispatchToken =>
        {
            context.ValidateDefinition(record.Definition);
            DemandMessage();
            var result = await bridge.CallAsync(profile, "message", new { request.Id, request.Text, profile.AllowLocalWork, Scope = retainedScope,
                ExpectedNativeGeneration = ready.NativeRuntime?.Generation, ExpectedDistributionGeneration = ready.DistributionRuntime?.Generation,
                ExpectedWorkspaceIdentity = identity }, dispatchToken);
            await inbox.ImportAsync(record, result, dispatchToken);
            var reply = result.GetProperty("reply");
            if (reply.GetProperty("inReplyTo").GetGuid() != request.Id) throw new InvalidDataException("Reply correlation failed.");
            return new(scope.RecipientId.ToString("D"), reply.GetProperty("text").GetString()!);
        });
    }
    public async Task<WorkRecord> ReconcileAsync(WorkRecord record, CancellationToken token)
    {
        if (record.Request.Scope is null) return record;
        if (record.Request.Kind == "message")
        {
            if (record.State == "unknown") record = await inbox.ReconcileRetainedReplyAsync(record, token);
            return record is { TerminalRefusalSentAt: not null, NativeTurnEnded: false }
                ? await ObserveRefusedTurnEndAsync(record, token) : record;
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

    private async Task<WorkRecord> ObserveRefusedTurnEndAsync(WorkRecord work, CancellationToken token)
    {
        if (work.Request.SessionId is not { } id) return work;
        var session = await context.RecordAsync(id, token);
        if (session.Definition is not { } definition || !ProjectPolicy.IsSessionRequest(work.Request, definition)) return work;
        try
        {
            context.ValidateDefinition(definition);
            var profile = await sessions.ResolveAsync(id, session.ProfileId, token, allowClosed: true);
            var observed = await bridge.CallAsync(profile, "inspect_turn", new { RequestId = work.Request.Id }, token);
            if (!observed.TryGetProperty("turnEnded", out var ended) || ended.ValueKind != System.Text.Json.JsonValueKind.True) return work;
        }
        catch (Exception error) when (error is RelayException or IOException or TimeoutException)
        {
            // Inaccessible or older native histories provide no evidence of termination.
            return work;
        }
        await store.RecordNativeTurnEndAsync(work.Request.Id, token);
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
