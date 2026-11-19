namespace AgentRelay.Core;

public sealed record TransferRequest(string Direction, string SourcePath, string DestinationPath, bool Overwrite = false);
public sealed record WorkRequest(Guid Id, string ProfileId, string Kind, string Text = "", string? CommandId = null, Guid? SessionId = null,
    TransferRequest? Transfer = null, OperationScope? Scope = null, Guid? TaskId = null, Guid? AttemptId = null,
    ArtifactTransfer? Artifact = null, Guid? SessionOwnerId = null);
public sealed record WorkResult(string Origin, string Output, string ErrorOutput = "", int? ExitCode = null,
    IReadOnlyDictionary<string, string>? Details = null);
public sealed record WorkRecord(WorkRequest Request, string State, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    WorkResult? Result = null, string? Error = null, string? ErrorCode = null, WorkMetadata? Metadata = null, WorkProgress? Progress = null,
    DateTimeOffset? CancellationRequestedAt = null, bool DispatchBoundaryTracked = false,
    DateTimeOffset? CancellationConfirmedAt = null, DateTimeOffset? ApprovalStopRequestedAt = null,
    DateTimeOffset? ApprovalWaitingSince = null, DateTimeOffset? ApprovalExpiredAt = null,
    DateTimeOffset? SessionClosedAt = null);

public static class ApprovalExpiry
{
    public static TimeSpan WaitingLimit { get; } = TimeSpan.FromHours(2);
    public static DateTimeOffset StartedAt(WorkRecord record) => record.ApprovalWaitingSince ?? record.UpdatedAt;
}

/// <summary>Execution evidence is independent of a result received during an explicit stop.</summary>
public static class WorkExecution
{
    public static bool ApprovalStopPending(WorkRecord record) =>
        record.ApprovalStopRequestedAt is not null && record.CancellationConfirmedAt is null;

    public static bool MayContinue(WorkRecord record) =>
        ApprovalStopPending(record) || record.CancellationConfirmedAt is null && record.State is not ("completed" or "failed");
}
public sealed record WorkProgress(long Bytes, long TotalBytes, string Phase, DateTimeOffset UpdatedAt);
public sealed record WorkEvent(Guid Id, Guid RequestId, Guid? ProjectId, Guid? TaskId, Guid? AttemptId, Guid? SessionId,
    Guid? EndpointId, string State, DateTimeOffset OccurredAt, string? ErrorCode = null);
public sealed record WorkMetadata(string Label, string Context);
public sealed record InboxMessage(Guid Id, string ProfileId, string Sender, string Recipient, string Text, Guid? InReplyTo, DateTimeOffset ReceivedAt, Guid? SessionId = null,
    Guid? ProjectId = null, Guid? SenderEndpointId = null, Guid? RecipientEndpointId = null);
public sealed record WorkCapability(string ProfileId, bool Commands, bool Messages, bool Files = false);

public interface IWorkStore
{
    Task<WorkRecord?> FindAsync(Guid id, CancellationToken token);
    Task SaveAsync(WorkRecord record, CancellationToken token);
    Task<IReadOnlyList<WorkRecord>> ListAsync(CancellationToken token);
    Task RecoverAsync(CancellationToken token);
    Task RequestCancellationAsync(Guid id, CancellationToken token) => Task.CompletedTask;
    Task<IReadOnlyList<WorkRecord>> RetainedApprovalsAsync(Guid sessionId, CancellationToken token) =>
        Task.FromResult<IReadOnlyList<WorkRecord>>([]);
    Task<bool> BeginApprovalStopAsync(Guid id, CancellationToken token) =>
        throw new RelayException("cancellation_unavailable", "This work store cannot retain a pending approval stop.", false);
    Task ClearApprovalStopAsync(Guid id, CancellationToken token) =>
        throw new RelayException("cancellation_unavailable", "This work store cannot clear a pending approval stop.", false);
    Task RecordApprovalSessionClosureAsync(Guid id, bool stopped, CancellationToken token) =>
        throw new RelayException("cancellation_unavailable", "This work store cannot retain the session closure.", false);
    Task RecordApprovalInterruptionAsync(Guid id, CancellationToken token) => Task.CompletedTask;
}

public interface IWorkExecutor
{
    Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token);
}

public interface IPreparedWork : IDisposable
{
    Task<WorkResult> ExecuteAsync(CancellationToken token);
}

public interface IWorkPreparation
{
    Task<IPreparedWork> PrepareAsync(WorkRequest request, CancellationToken token);
}

public sealed class WorkService : IAsyncDisposable
{
    private readonly IWorkStore store;
    private readonly IWorkExecutor executor;
    private readonly Func<IReadOnlyCollection<WorkCapability>> capabilities;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task> running = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Guid> activeProjects = new();
    private readonly Dictionary<string, CancellationTokenSource> cancellations = new();
    private readonly Dictionary<Guid, string> operationKeys = new();
    private sealed record Preparation(WorkRequest Request, string Key, TaskCompletionSource<WorkRecord> Completion);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, Preparation> preparing = new();
    private readonly int maximumConcurrency;
    private int disposeState;
    public bool IsActive => !preparing.IsEmpty || running.Values.Any(task => !task.IsCompleted);
    public bool IsProjectActive(Guid projectId) => preparing.Values.Any(item => item.Request.Scope?.ProjectId == projectId) ||
        running.Any(item => !item.Value.IsCompleted && activeProjects.GetValueOrDefault(item.Key) == projectId);
    public bool IsSessionActive(Guid sessionId) => preparing.Values.Any(item => item.Request.SessionId == sessionId) ||
        running.TryGetValue("session:" + sessionId, out var task) && !task.IsCompleted;

    public WorkService(IWorkStore store, IWorkExecutor executor, Func<IReadOnlyCollection<WorkCapability>> capabilities, int maximumConcurrency = 4)
    {
        if (maximumConcurrency is < 1 or > 64) throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        this.store = store; this.executor = executor; this.capabilities = capabilities; this.maximumConcurrency = maximumConcurrency;
    }

    public async Task<WorkRecord> SubmitAsync(WorkRequest request, CancellationToken token, WorkMetadata? metadata = null)
    {
        if (request.Id == Guid.Empty || string.IsNullOrWhiteSpace(request.ProfileId) || request.Text is null || request.Text.Length > 8000 ||
            request.Text.Any(character => char.IsControl(character) && character is not ('\n' or '\r' or '\t')) ||
            request.Kind is not ("command" or "message" or "transfer") || (request.Kind == "message" && string.IsNullOrWhiteSpace(request.Text)) ||
            (request.Kind == "transfer" && (request.SessionId is null || (request.Transfer is null && request.Artifact is null))))
            throw new RelayException("invalid_work", "The operation, identifier or message is invalid.", effectsPossible: false);
        var capability = capabilities().SingleOrDefault(item => item.ProfileId == request.ProfileId);
        if (capability is null || !(request.Kind switch { "command" => capability.Commands, "message" => capability.Messages, _ => capability.Files }))
            throw new RelayException("capability_denied", "This profile does not permit the requested operation.");
        Preparation claim;
        var ownsClaim = false;
        await admission.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (stop.IsCancellationRequested) throw new RelayException("unavailable", "The relay is stopping.");
            var existing = await store.FindAsync(request.Id, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.Request != request) throw new RelayException("id_conflict", "This identifier belongs to another operation.", false);
                return existing;
            }
            var key = request.SessionId is { } sessionId ? "session:" + sessionId :
                request is { Kind: "command", Scope: not null } ? "request:" + request.Id : "profile:" + request.ProfileId;
            if (preparing.TryGetValue(request.Id, out var inProgress))
            {
                if (inProgress.Request != request) throw new RelayException("id_conflict", "This identifier belongs to another operation.", false);
                claim = inProgress;
            }
            else
            {
                if (request.SessionId is { } targetSession) await DemandSessionResolvedAsync(targetSession, token);
                if (preparing.Values.Any(item => item.Key == key) || running.TryGetValue(key, out var active) && !active.IsCompleted)
                    throw new RelayException("busy", "This session already has an active operation.", false);
                if (preparing.Count + running.Values.Count(task => !task.IsCompleted) >= maximumConcurrency)
                    throw new RelayException("busy", "The configured host concurrency limit is reached.", false);
                claim = new(request, key, new(TaskCreationOptions.RunContinuationsAsynchronously));
                preparing[request.Id] = claim;
                ownsClaim = true;
            }
        }
        finally { admission.Release(); }
        if (!ownsClaim) return await claim.Completion.Task.WaitAsync(token).ConfigureAwait(false);

        IPreparedWork? prepared = null;
        using var preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token, token);
        try
        {
            if (executor is IWorkPreparation port)
                prepared = await port.PrepareAsync(request, preparationCancellation.Token).ConfigureAwait(false);
            await admission.WaitAsync(preparationCancellation.Token).ConfigureAwait(false);
            try
            {
                preparationCancellation.Token.ThrowIfCancellationRequested();
                foreach (var completed in running.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
                {
                    running.TryRemove(completed, out _);
                    cancellations.Remove(completed, out var previous);
                    previous?.Dispose();
                    activeProjects.TryRemove(completed, out _);
                    foreach (var id in operationKeys.Where(pair => pair.Value == completed).Select(pair => pair.Key).ToArray()) operationKeys.Remove(id);
                }
                var record = new WorkRecord(request, "pending", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Metadata: metadata,
                    DispatchBoundaryTracked: true);
                await store.SaveAsync(record, CancellationToken.None).ConfigureAwait(false);
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                cancellations[claim.Key] = cancellation;
                operationKeys[request.Id] = claim.Key;
                activeProjects[claim.Key] = request.Scope?.ProjectId ?? Guid.Empty;
                var dispatch = prepared;
                running[claim.Key] = Task.Run(() => ExecuteAsync(record, cancellation.Token, dispatch));
                prepared = null;
                preparing.TryRemove(request.Id, out _);
                claim.Completion.SetResult(record);
            }
            finally { admission.Release(); }
        }
        catch (Exception error)
        {
            try { prepared?.Dispose(); }
            finally
            {
                preparing.TryRemove(request.Id, out _);
                claim.Completion.TrySetException(error);
            }
        }
        return await claim.Completion.Task.ConfigureAwait(false);
    }

    public async Task InterruptSessionAsync(Guid sessionId, CancellationToken token)
    {
        await admission.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var key = "session:" + sessionId;
            if (cancellations.TryGetValue(key, out var cancellation))
            {
                foreach (var id in operationKeys.Where(pair => pair.Value == key).Select(pair => pair.Key)) await store.RequestCancellationAsync(id, token);
                cancellation.Cancel();
            }
        }
        finally { admission.Release(); }
    }

    public async Task<bool> IsOperationActiveAsync(Guid id, CancellationToken token)
    {
        await admission.WaitAsync(token);
        try { return operationKeys.TryGetValue(id, out var key) && running.TryGetValue(key, out var task) && !task.IsCompleted; }
        finally { admission.Release(); }
    }

    public Task<IReadOnlyList<WorkRecord>> RetainedApprovalsAsync(Guid sessionId, CancellationToken token) =>
        store.RetainedApprovalsAsync(sessionId, token);

    internal async Task DemandSessionResolvedAsync(Guid sessionId, CancellationToken token, OperationScope? expectedScope = null)
    {
        var approvals = await RetainedApprovalsAsync(sessionId, token);
        if (approvals.Any(item => expectedScope is null || item.Request.Scope is { } scope &&
            (scope with { SenderId = item.Request.SessionOwnerId ?? scope.SenderId }) == expectedScope))
            throw new RelayException("session_awaiting_approval", "A retained approval or unconfirmed stop belongs to this session. Resolve it before reusing the session.", false);
    }

    public async Task<WorkRecord> RequestCancellationAsync(Guid id, CancellationToken token)
    {
        await store.RequestCancellationAsync(id, token);
        var record = await ReadAsync(id, token);
        if (record.State is "completed" or "failed" or "approval_expired" || record.CancellationConfirmedAt is not null)
            throw new RelayException("operation_already_finished", "The operation is already finished and cannot be cancelled.", false);
        if (record.State is not ("pending" or "running" or "awaiting_approval") || record.CancellationRequestedAt is null)
            throw new RelayException("cancellation_unavailable", "This operation has no confirmed cancellable activity. Inspect the retained result.", false);
        return record;
    }

    public Task<bool> BeginApprovalStopAsync(Guid id, CancellationToken token) => store.BeginApprovalStopAsync(id, token);
    public Task ClearApprovalStopAsync(Guid id, CancellationToken token) => store.ClearApprovalStopAsync(id, token);
    public Task RecordApprovalSessionClosureAsync(Guid id, bool stopped, CancellationToken token) =>
        store.RecordApprovalSessionClosureAsync(id, stopped, token);

    internal async Task RecordTerminalInterruptionAsync(Guid sessionId, bool cancelActive, CancellationToken token)
    {
        if (cancelActive)
        {
            await InterruptSessionAsync(sessionId, token);
            // The worker may be persisting approval_required at the same time the key is sent.
            if (running.TryGetValue("session:" + sessionId, out var interrupted))
                await interrupted.WaitAsync(token);
        }
        foreach (var approval in await RetainedApprovalsAsync(sessionId, token))
            await store.RecordApprovalInterruptionAsync(approval.Request.Id, token);
    }

    private async Task ExecuteAsync(WorkRecord record, CancellationToken token, IPreparedWork? prepared)
    {
        using var reservation = prepared;
        try
        {
            if (token.IsCancellationRequested)
                throw new RelayException("cancelled_before_dispatch", "The request was cancelled before dispatch.", false);
            await store.SaveAsync(record with { State = "running", UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None).ConfigureAwait(false);
            var result = prepared is null ? await executor.ExecuteAsync(record.Request, token).ConfigureAwait(false) :
                await prepared.ExecuteAsync(token).ConfigureAwait(false);
            await store.SaveAsync(record with { State = result.ExitCode is not (null or 0) ? "failed" : "completed", Result = result,
                UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var state = error is RelayException { EffectsPossible: true } ? "unknown" : error is RelayException { Code: "approval_required" } ? "awaiting_approval" :
                error is RelayException { EffectsPossible: false } ? "failed" :
                error is RelayException { Code: "unavailable" or "command_denied" or "authentication_required" or "agent_authentication_required" or "credential_reference_unavailable" or "capability_denied" or "invalid_path" or "destination_exists" or "busy" } ? "failed" : "unknown";
            var observedAt = DateTimeOffset.UtcNow;
            await store.SaveAsync(record with { State = state, Error = error.Message,
                ErrorCode = error is RelayException relay ? relay.Code : error is TimeoutException ? "timeout" : "execution_unknown",
                UpdatedAt = observedAt, ApprovalWaitingSince = state == "awaiting_approval" ? observedAt : null }, CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task<WorkRecord> ReadAsync(Guid id, CancellationToken token) =>
        await store.FindAsync(id, token).ConfigureAwait(false) ?? throw new RelayException("not_found", "Unknown operation.");
    public Task<IReadOnlyList<WorkRecord>> ListAsync(CancellationToken token) => store.ListAsync(token);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposeState, 1) != 0) return;
        stop.Cancel();
        await admission.WaitAsync().ConfigureAwait(false);
        var admissions = preparing.Values.Select(item => item.Completion.Task).ToArray();
        var pending = running.Values.ToArray();
        admission.Release();
        foreach (var task in admissions)
        {
            try { await task.ConfigureAwait(false); }
            catch (Exception) { /* The submission already reports its own failure. */ }
        }
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        finally { foreach (var cancellation in cancellations.Values) cancellation.Dispose(); stop.Dispose(); admission.Dispose(); }
    }
}
