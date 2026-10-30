namespace AgentRelay.Core;

public sealed record TransferRequest(string Direction, string SourcePath, string DestinationPath, bool Overwrite = false);
public sealed record WorkRequest(Guid Id, string ProfileId, string Kind, string Text = "", string? CommandId = null, Guid? SessionId = null,
    TransferRequest? Transfer = null, OperationScope? Scope = null, Guid? TaskId = null, Guid? AttemptId = null,
    ArtifactTransfer? Artifact = null);
public sealed record WorkResult(string Origin, string Output, string ErrorOutput = "", int? ExitCode = null,
    IReadOnlyDictionary<string, string>? Details = null);
public sealed record WorkRecord(WorkRequest Request, string State, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    WorkResult? Result = null, string? Error = null, string? ErrorCode = null, WorkMetadata? Metadata = null, WorkProgress? Progress = null,
    DateTimeOffset? CancellationRequestedAt = null);
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
}

public interface IWorkExecutor
{
    Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token);
}

public sealed class WorkService : IAsyncDisposable
{
    private readonly IWorkStore store;
    private readonly IWorkExecutor executor;
    private readonly Func<IReadOnlyCollection<WorkCapability>> capabilities;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim admission = new(1, 1);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task> running = new();
    private readonly Dictionary<string, CancellationTokenSource> cancellations = new();
    private readonly Dictionary<Guid, string> operationKeys = new();
    private readonly int maximumConcurrency;
    private int disposeState;
    public bool IsActive => running.Values.Any(task => !task.IsCompleted);
    public bool IsSessionActive(Guid sessionId) => running.TryGetValue("session:" + sessionId, out var task) && !task.IsCompleted;

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
            throw new RelayException("invalid_work", "The operation, identifier or message is invalid.");
        var capability = capabilities().SingleOrDefault(item => item.ProfileId == request.ProfileId);
        if (capability is null || !(request.Kind switch { "command" => capability.Commands, "message" => capability.Messages, _ => capability.Files }))
            throw new RelayException("capability_denied", "This profile does not permit the requested operation.");
        await admission.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (stop.IsCancellationRequested) throw new RelayException("unavailable", "The relay is stopping.");
            var existing = await store.FindAsync(request.Id, token).ConfigureAwait(false);
            if (existing is not null)
            {
                if (existing.Request != request) throw new RelayException("id_conflict", "This identifier belongs to another operation.");
                return existing;
            }
            var key = request.SessionId is { } sessionId ? "session:" + sessionId : "profile:" + request.ProfileId;
            if (running.TryGetValue(key, out var active) && !active.IsCompleted) throw new RelayException("busy", "This session already has an active operation.");
            if (running.Values.Count(task => !task.IsCompleted) >= maximumConcurrency) throw new RelayException("busy", "The configured host concurrency limit is reached.");
            foreach (var completed in running.Where(pair => pair.Value.IsCompleted).Select(pair => pair.Key).ToArray())
            {
                running.TryRemove(completed, out _); cancellations.Remove(completed, out var previous); previous?.Dispose();
                foreach (var id in operationKeys.Where(pair => pair.Value == completed).Select(pair => pair.Key).ToArray()) operationKeys.Remove(id);
            }
            var record = new WorkRecord(request, "pending", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Metadata: metadata);
            await store.SaveAsync(record, token).ConfigureAwait(false);
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            cancellations[key] = cancellation;
            operationKeys[request.Id] = key;
            running[key] = Task.Run(() => ExecuteAsync(record, cancellation.Token));
            return record;
        }
        finally { admission.Release(); }
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

    private async Task ExecuteAsync(WorkRecord record, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await store.SaveAsync(record with { State = "running", UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None).ConfigureAwait(false);
            var result = await executor.ExecuteAsync(record.Request, token).ConfigureAwait(false);
            await store.SaveAsync(record with { State = result.ExitCode is not (null or 0) ? "failed" : "completed", Result = result,
                UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            var state = error is RelayException { EffectsPossible: true } ? "unknown" : error is RelayException { Code: "approval_required" } ? "awaiting_approval" :
                error is RelayException { EffectsPossible: false } ? "failed" :
                error is RelayException { Code: "unavailable" or "command_denied" or "authentication_required" or "agent_authentication_required" or "credential_reference_unavailable" or "capability_denied" or "invalid_path" or "destination_exists" or "busy" } ? "failed" : "unknown";
            await store.SaveAsync(record with { State = state, Error = error.Message,
                ErrorCode = error is RelayException relay ? relay.Code : error is TimeoutException ? "timeout" : "execution_unknown",
                UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None).ConfigureAwait(false);
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
        var pending = running.Values.ToArray();
        admission.Release();
        try { await Task.WhenAll(pending).ConfigureAwait(false); }
        finally { foreach (var cancellation in cancellations.Values) cancellation.Dispose(); stop.Dispose(); admission.Dispose(); }
    }
}
