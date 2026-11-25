using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

/// <summary>Select the installed session adapter from the retained endpoint without changing the application port.</summary>
internal sealed class SessionAdapterRouter(IRelaySessions retained, IReadOnlyDictionary<string, IRelaySessions> adapters,
    Func<Guid, CancellationToken, Task<string>> retainedAdapter) : IRelaySessions
{
    private IRelaySessions Select(string adapterId) => adapters.TryGetValue(adapterId, out var adapter) ? adapter :
        throw new RelayException("adapter_unavailable", "No session adapter is installed for this endpoint.", false);

    private async Task<IRelaySessions> SelectAsync(Guid id, CancellationToken token) =>
        Select(await retainedAdapter(id, token));

    public Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token) =>
        Select(definition.Endpoint.AdapterId).OpenAsync(definition, token);

    public Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => retained.GetAsync(id, token);
    public Task<ReadPage<SessionSnapshot>> AllAsync(ReadQuery query, CancellationToken token) => retained.AllAsync(query, token);
    public Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token) =>
        retained.ListAsync(query, ownerId, token);

    public async Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token) =>
        await (await SelectAsync(id, token)).InspectAsync(id, token);

    public async Task<SessionSnapshot> InspectForArchiveAsync(Guid id, CancellationToken token) =>
        await (await SelectAsync(id, token)).InspectForArchiveAsync(id, token);

    public async Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token) =>
        await (await SelectAsync(id, token)).ControlAsync(id, action, token);

    public async Task<SessionTerminal> ReadTerminalAsync(Guid id, CancellationToken token) =>
        await (await SelectAsync(id, token)).ReadTerminalAsync(id, token);

    public async Task<TerminalSignalReceipt> SignalTerminalAsync(Guid id, string action, string? revision, CancellationToken token) =>
        await (await SelectAsync(id, token)).SignalTerminalAsync(id, action, revision, token);

    public async Task<bool> CloseApprovalSessionAsync(WorkRequest request, CancellationToken token) =>
        await (await SelectAsync(request.SessionId!.Value, token)).CloseApprovalSessionAsync(request, token);
    public async Task<bool> VerifyDormantApprovalAsync(WorkRequest request, CancellationToken token) =>
        await (await SelectAsync(request.SessionId!.Value, token)).VerifyDormantApprovalAsync(request, token);
    public async Task<SessionSnapshot> ReopenPinnedAsync(Guid id, CancellationToken token) =>
        await (await SelectAsync(id, token)).ReopenPinnedAsync(id, token);

    public async Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token)
    {
        if (sessionId is { } id)
            return await (await SelectAsync(id, token)).RefreshMessagesAsync(projectId, recipientId, id, token);
        var warnings = new List<string>();
        foreach (var adapter in adapters.Values.Distinct())
            warnings.AddRange(await adapter.RefreshMessagesAsync(projectId, recipientId, null, token));
        return warnings;
    }
}
