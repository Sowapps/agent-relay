using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Session capability only; command execution/recovery and inbox import have separate owners.
public sealed class ScopedSessions(SessionService sessions, AgentControl agents, ProjectSettingsStore projects,
    ProjectJournal journal, ScopedInbox inbox, ScopedSessionContext context, Func<Guid, bool> active) : IRelaySessions
{
    public async Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token)
    {
        if (definition.Endpoint.AdapterId != "wsl-agent") throw new RelayException("adapter_unavailable", "This endpoint has no installed native-session adapter.");
        var record = await sessions.CreateAsync(definition.Id, definition.Name, definition.Endpoint.BindingId, token, definition);
        context.ValidateDefinition(definition);
        new ProjectPolicy(projects).Demand(new(definition.Scope.SenderId, [definition.Scope.ProjectId]), definition.Scope.ProjectId, RelayCapabilities.ManageSession);
        if (record.Definition is null || record.Definition.Scope != definition.Scope || record.Definition.Workspace != definition.Workspace)
            throw new RelayException("id_conflict", "The session belongs to a different project, participant or workspace.");
        if (record.State == "created") await sessions.StartAsync(record.Id, token);
        return await InspectAsync(record.Id, token);
    }
    public async Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => Snapshot(await context.RecordAsync(id, token));
    public async Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token)
    {
        var record = await context.RecordAsync(id, token);
        context.ValidateDefinition(record.Definition!);
        if (record.State is "closed" or "preparing" or "setup_failed") return Snapshot(record);
        var agent = await agents.StatusAsync(await sessions.ResolveAsync(id, record.ProfileId, token), token);
        return Snapshot(record) with { State = agent.State, Diagnostic = agent.Message, NativeConversationId = agent.NativeConversationId };
    }

    public async Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token)
    {
        var page = await journal.SessionsAsync(query, ownerId, token);
        return new(page.Items.Select(Snapshot).ToArray(), page.NextCursor, page.HasMore);
    }
    public async Task<ReadPage<SessionSnapshot>> AllAsync(ReadQuery query, CancellationToken token)
    {
        var page = await journal.ObserveSessionsAsync(query, token);
        return new(page.Items.Select(Snapshot).ToArray(), page.NextCursor, page.HasMore);
    }

    public async Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token)
    {
        var record = await context.RecordAsync(id, token);
        context.ValidateDefinition(record.Definition!);
        if (action.StartsWith("rename:", StringComparison.Ordinal)) await sessions.RenameAsync(id, action[7..], token);
        else if (action == "start") await sessions.StartAsync(id, token);
        else if (action == "reopen") await sessions.ReopenAsync(id, token);
        else await sessions.ControlAsync(id, action == "close", token);
        return await InspectAsync(id, token);
    }

    private SessionSnapshot Snapshot(RelaySession record) => new(record.Id, record.Name, record.Definition!.Scope, record.State, Diagnostic: record.Diagnostic, WorkActive: active(record.Id));
    public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token)
        => inbox.RefreshAsync(projectId, recipientId, sessionId, token);
}
