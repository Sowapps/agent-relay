using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Session capability only; command execution/recovery and inbox import have separate owners.
public sealed class ScopedSessions(SessionService sessions, AgentControl agents, ProjectSettingsStore projects,
    ProjectJournal journal, ScopedInbox inbox, ScopedSessionContext context, Func<Guid, bool> active, bool detachedArchiveCheck = false) : IRelaySessions
{
    public async Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token)
    {
        context.ValidateDefinition(definition);
        var record = await sessions.CreateAsync(definition.Id, definition.Name, definition.Endpoint.BindingId, token, definition);
        new ProjectPolicy(projects).Demand(new(definition.Scope.SenderId, [definition.Scope.ProjectId]), definition.Scope.ProjectId, RelayCapabilities.ManageSession);
        if (record.Definition is null || record.Definition.Scope != definition.Scope ||
            !ScopedSessionContext.SameWorkspace(record.Definition.Workspace, definition.Workspace))
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
        var agent = await agents.StatusAsync(await sessions.ResolveAsync(id, record.ProfileId, token, allowClosed: true), token);
        var observation = await sessions.ObserveAsync(id, agent, token);
        return Snapshot(record) with { State = agent.State, Diagnostic = agent.Message, NativeConversationId = agent.NativeConversationId, Runtime = observation };
    }

    public async Task<SessionSnapshot> InspectForArchiveAsync(Guid id, CancellationToken token)
    {
        var record = await context.RecordAsync(id, token);
        context.ValidateDefinition(record.Definition!);
        if (record.State is "closed" or "preparing" or "setup_failed") return Snapshot(record);
        if (await sessions.VerifyDormantRetainedRootForArchiveAsync(record, token))
            return Snapshot(record) with { State = "not_started" };
        if (detachedArchiveCheck)
            throw new RelayException("archive_unverified", "The detached archive check only verifies a dormant retained root after a template relocation.", false);
        return await InspectAsync(id, token);
    }

    public async Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token)
    {
        var page = await journal.SessionsAsync(query, ownerId, token, context.Mandates(query.ProjectId, ownerId, RelayCapabilities.ReadHistory));
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
    public async Task<SessionTerminal> ReadTerminalAsync(Guid id, CancellationToken token)
    {
        var record = await context.RecordAsync(id, token);
        context.ValidateDefinition(record.Definition!);
        return await sessions.ReadTerminalAsync(record, token);
    }
    public async Task<TerminalSignalReceipt> SignalTerminalAsync(Guid id, string action, string? approvalRevision, CancellationToken token)
    {
        var record = await context.RecordAsync(id, token);
        context.ValidateDefinition(record.Definition!);
        return await sessions.SignalTerminalAsync(record, action, approvalRevision, token);
    }
    public async Task<bool> CloseApprovalSessionAsync(WorkRequest request, CancellationToken token)
    {
        var record = await context.RecordAsync(request.SessionId!.Value, token);
        context.ValidateDefinition(record.Definition!);
        return await sessions.CloseApprovalSessionAsync(record, request, token);
    }
    public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token)
        => inbox.RefreshAsync(projectId, recipientId, sessionId, token);
}
