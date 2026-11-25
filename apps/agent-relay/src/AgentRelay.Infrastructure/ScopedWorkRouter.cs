using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public interface IScopedWorkAdapter : IWorkExecutor, IWorkPreparation, IOperationRecovery, ITransferMaintenance;
internal interface IReplyObservation
{
    Task ObserveReplyAsync(WorkRecord record, CancellationToken token);
}

/// <summary>Route authorized work by technical endpoint capability while sharing the normal work lifecycle.</summary>
internal sealed class ScopedWorkRouter(ProjectSettingsStore settings, IReadOnlyDictionary<string, IScopedWorkAdapter> installed,
    IOperationRecovery transferReceipts) : IScopedWorkAdapter, IRetryPreparation, IReplyObservation
{
    private IScopedWorkAdapter? Find(WorkRequest request)
    {
        var endpoint = settings.Load().Endpoints.SingleOrDefault(item => item.Id == request.Scope?.RecipientId);
        return endpoint is not null && installed.TryGetValue(endpoint.AdapterId, out var adapter) ? adapter : null;
    }

    private IScopedWorkAdapter Select(WorkRequest request) =>
        Find(request) ?? throw new RelayException("adapter_unavailable", "No work adapter is installed for this endpoint.", false);

    public Task<IPreparedWork> PrepareAsync(WorkRequest request, CancellationToken token) =>
        Select(request).PrepareAsync(request, token);

    public Task<IPreparedWork> PrepareRetryAsync(WorkRecord record, CancellationToken token) =>
        Select(record.Request) is IRetryPreparation retry ? retry.PrepareRetryAsync(record, token) :
            throw new RelayException("adapter_unavailable", "The selected adapter cannot resume this retry.", false);

    public Task ObserveReplyAsync(WorkRecord record, CancellationToken token) =>
        Find(record.Request) is IReplyObservation observer ? observer.ObserveReplyAsync(record, token) :
            throw new RelayException("adapter_unavailable", "The selected adapter cannot observe this retained reply.");

    public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
    {
        using var prepared = await PrepareAsync(request, token);
        return await prepared.ExecuteAsync(token);
    }

    public Task<WorkRecord> ReconcileAsync(WorkRecord record, CancellationToken token)
    {
        // A previously published transfer has a common receipt even if its endpoint adapter is no longer registered.
        if (record.Request.Kind == "transfer") return transferReceipts.ReconcileAsync(record, token);
        return Find(record.Request)?.ReconcileAsync(record, token) ?? Task.FromResult(record);
    }

    public Task<TransferPartial> PartialAsync(WorkRequest request, bool remove, CancellationToken token) =>
        Select(request).PartialAsync(request, remove, token);
}
