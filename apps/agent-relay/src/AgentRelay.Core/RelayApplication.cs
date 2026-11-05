namespace AgentRelay.Core;

public sealed record SessionDefinition(Guid Id, string Name, OperationScope Scope, RelayEndpoint Endpoint,
    RelayWorkspace Workspace, bool AllowLocalWork);
public sealed record SessionSnapshot(Guid Id, string Name, OperationScope Scope, string State, string? NativeConversationId = null,
    string? Diagnostic = null, bool WorkActive = false);
public sealed record ReadQuery(Guid ProjectId, Guid? EndpointId = null, Guid? SessionId = null, Guid? TaskId = null,
    string? Kind = null, string? State = null, string? Search = null, DateTimeOffset? Since = null, DateTimeOffset? Until = null,
    long? Cursor = null, int Limit = 50);
public sealed record ReadPage<T>(IReadOnlyList<T> Items, long? NextCursor, bool HasMore, IReadOnlyList<string>? Warnings = null);
public sealed record HostReport(Guid EndpointId, IReadOnlyDictionary<string, string> Facts);
public interface IHostInspector { Task<HostReport> InspectAsync(RelayEndpoint endpoint, CancellationToken token); }
public interface IOperationRecovery { Task<WorkRecord> ReconcileAsync(WorkRecord record, CancellationToken token); }
public sealed record TransferPartial(Guid RequestId, string RelativePath, long Bytes, bool Exists, bool Removed);
public interface ITransferMaintenance { Task<TransferPartial> PartialAsync(WorkRequest request, bool remove, CancellationToken token); }

public interface IRelaySessions
{
    Task<ReadPage<SessionSnapshot>> AllAsync(ReadQuery query, CancellationToken token) => throw new RelayException("adapter_unavailable", "Project-wide session verification is unavailable; archival is refused.");
    Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token);
    Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token);
    Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token);
    Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token);
    Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token);
    Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token);
}

public interface IRelayJournal
{
    Task<ReadPage<WorkRecord>> OperationsAsync(ReadQuery query, CancellationToken token);
    Task<ReadPage<InboxMessage>> MessagesAsync(ReadQuery query, Guid recipientId, CancellationToken token);
    Task<ReadPage<WorkEvent>> EventsAsync(ReadQuery query, CancellationToken token);
}

// The public production use cases: no client, operating system or transport decisions.
public sealed partial class RelayApplication(ProjectPolicy policy, WorkService work, IRelaySessions sessions, IRelayJournal journal, IHostInspector? hosts = null, IOperationRecovery? recovery = null, ITransferMaintenance? transfers = null, IProjectStateStore? projectStates = null)
{
    public ProjectAdmission Admission { get; } = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, SemaphoreSlim> sessionGates = new();
    private async Task<IDisposable> Gate(Guid id, CancellationToken token)
    {
        var gate = sessionGates.GetOrAdd(id, _ => new(1, 1));
        if (!await gate.WaitAsync(0, token)) throw new RelayException("busy", "This session has a lifecycle/submission change in progress.");
        return new SessionLease(gate);
    }
    private sealed class SessionLease(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }
    public ProjectConfiguration Discover(CallerIdentity caller)
    {
        using var policySnapshot = policy.Pin();
        var value = policy.Configuration();
        var projects = value.Projects.Where(project => caller.ProjectIds.Contains(project.Id) &&
            value.Memberships.Any(member => member.ProjectId == project.Id && member.EndpointId == caller.EndpointId)).ToArray();
        var memberships = value.Memberships.Where(member => projects.Any(project => project.Id == member.ProjectId)).ToArray();
        var workspaceIds = memberships.Where(member => member.EndpointId == caller.EndpointId).SelectMany(member => member.Workspaces).Select(access => access.WorkspaceId).ToHashSet();
        return value with { Projects = projects, Memberships = memberships.Where(member => member.EndpointId == caller.EndpointId).ToArray(),
            Endpoints = value.Endpoints.Where(endpoint => memberships.Any(member => member.EndpointId == endpoint.Id)).Select(endpoint => endpoint with { BindingId = "protected" }).ToArray(),
            Workspaces = value.Workspaces.Where(workspace => workspaceIds.Contains(workspace.Id)).ToArray() };
    }

    public async Task<HostReport> InspectHostAsync(CallerIdentity caller, Guid projectId, Guid endpointId, CancellationToken token)
    {
        using var projectLease = Admission.Enter(projectId);
        using var policySnapshot = policy.Pin();
        policy.Demand(caller, projectId, RelayCapabilities.InspectHost);
        var configuration = policy.Configuration();
        if (!configuration.Memberships.Any(member => member.ProjectId == projectId && member.EndpointId == endpointId))
            throw new RelayException("capability_denied", "This host endpoint does not belong to the authorized project.");
        var endpoint = configuration.Endpoints.Single(item => item.Id == endpointId);
        return await (hosts ?? throw new RelayException("adapter_unavailable", "No host-inspection adapter is installed.")).InspectAsync(endpoint, token);
    }

    /// <summary>Open an authorized conversation using the shared session identity.</summary>
    /// <seealso cref="ControlSessionAsync"/>
    public async Task<SessionSnapshot> OpenSessionAsync(CallerIdentity caller, Guid projectId, Guid endpointId, Guid workspaceId,
        Guid sessionId, string name, CancellationToken token)
    {
        using var projectLease = Admission.Enter(projectId);
        using var lease = await Gate(sessionId, token);
        using var policySnapshot = policy.Pin();
        policy.Demand(caller, projectId, RelayCapabilities.ManageSession);
        var scope = policy.Message(caller, projectId, endpointId, workspaceId);
        var workspace = policy.Workspace(caller, projectId, workspaceId, false, endpointId);
        var value = policy.Configuration();
        var endpoint = value.Endpoints.Single(item => item.Id == endpointId);
        var localWork = value.Memberships.Single(item => item.ProjectId == projectId && item.EndpointId == endpointId).Capabilities.Contains(RelayCapabilities.LocalWork);
        if (localWork)
        {
            policy.LocalWork(caller, projectId, workspaceId, endpointId);
        }
        if (sessionId == Guid.Empty)
            throw new RelayException("invalid_session", "A stable session identifier and readable name are required.");
        return await sessions.OpenAsync(new(sessionId, SessionNames.Normalize(name), scope, endpoint, workspace, localWork), token);
    }

    public async Task<SessionSnapshot> SessionAsync(CallerIdentity caller, Guid id, CancellationToken token)
    {
        var retained = await sessions.GetAsync(id, token);
        using var projectLease = Admission.Enter(retained.Scope.ProjectId);
        using var policySnapshot = policy.Pin();
        var session = await sessions.GetAsync(id, token);
        policy.Demand(caller, session.Scope.ProjectId, RelayCapabilities.ReadHistory);
        return policy.Configuration().Projects.Single(project => project.Id == session.Scope.ProjectId).Enabled ? await sessions.InspectAsync(session.Id, token) : session;
    }

    public async Task<ReadPage<SessionSnapshot>> SessionsAsync(CallerIdentity caller, ReadQuery query, CancellationToken token)
    {
        using var policySnapshot = policy.Pin();
        policy.Demand(caller, query.ProjectId, RelayCapabilities.ReadHistory);
        return await sessions.ListAsync(query, caller.EndpointId, token);
    }

    /// <summary>Control the same owned conversation opened by OpenSessionAsync; never substitute identity.</summary>
    /// <seealso cref="OpenSessionAsync"/>
    public async Task<SessionSnapshot> ControlSessionAsync(CallerIdentity caller, Guid id, string action, CancellationToken token)
    {
        using var projectLease = Admission.Enter((await sessions.GetAsync(id, token)).Scope.ProjectId);
        using var policySnapshot = policy.Pin();
        using var lease = await Gate(id, token);
        var session = await OwnedAsync(caller, id, RelayCapabilities.ManageSession, token);
        if (policy.Configuration().Projects.Single(project => project.Id == session.Scope.ProjectId).ArchiveRequested && action is not ("close" or "interrupt"))
            throw new RelayException("project_draining", "New session changes are suspended during archival; closing or interrupting existing work remains available.");
        if (action is not ("start" or "reopen" or "close" or "interrupt") && !action.StartsWith("rename:", StringComparison.Ordinal))
            throw new RelayException("invalid_operation", "Unknown session action.");
        if (action.StartsWith("rename:", StringComparison.Ordinal)) action = "rename:" + SessionNames.Normalize(action[7..]);
        if (action == "close" && work.IsSessionActive(id)) throw new RelayException("busy", "Inspect or interrupt the active operation before closing the session.");
        var result = await sessions.ControlAsync(session.Id, action, token);
        if (action == "interrupt") await work.InterruptSessionAsync(id, token);
        return result;
    }

    public async Task<WorkRecord> MessageAsync(CallerIdentity caller, Guid sessionId, Guid requestId, string text, Guid? taskId, CancellationToken token)
    {
        using var projectLease = Admission.Enter((await sessions.GetAsync(sessionId, token)).Scope.ProjectId);
        using var policySnapshot = policy.Pin();
        using var lease = await Gate(sessionId, token);
        var session = await OwnedAsync(caller, sessionId, RelayCapabilities.SendMessage, token);
        var scope = policy.Message(caller, session.Scope.ProjectId, session.Scope.RecipientId, session.Scope.WorkspaceId);
        return await work.SubmitAsync(new(requestId, scope.RecipientId.ToString("D"), "message", text, SessionId: sessionId,
            Scope: scope, TaskId: taskId ?? requestId, AttemptId: requestId), token, new("Message · " + session.Name, Workspace(scope).Location));
    }

    public async Task<WorkRecord> CommandAsync(CallerIdentity caller, Guid projectId, Guid endpointId, Guid workspaceId,
        string commandId, Guid requestId, CancellationToken token)
    {
        using var projectLease = Admission.Enter(projectId);
        using var policySnapshot = policy.Pin();
        var member = policy.Demand(caller, projectId, RelayCapabilities.RunCommand);
        if (!(member.CommandIds ?? []).Contains(commandId, StringComparer.Ordinal)) throw new RelayException("command_denied", "The project does not authorize this command identifier.");
        var workspace = policy.Workspace(caller, projectId, workspaceId, false, endpointId);
        var scope = new OperationScope(projectId, caller.EndpointId, endpointId, workspaceId);
        return await work.SubmitAsync(new(requestId, endpointId.ToString("D"), "command", CommandId: commandId, Scope: scope, TaskId: requestId, AttemptId: requestId),
            token, new("Command · " + commandId, workspace.Location));
    }

    public async Task<WorkRecord> TransferAsync(CallerIdentity caller, Guid sessionId, ArtifactTransfer transfer, Guid requestId, CancellationToken token)
    {
        using var projectLease = Admission.Enter((await sessions.GetAsync(sessionId, token)).Scope.ProjectId);
        using var policySnapshot = policy.Pin();
        using var lease = await Gate(sessionId, token);
        var session = await OwnedAsync(caller, sessionId, RelayCapabilities.ReadHistory, token);
        policy.Transfer(caller, session.Scope.ProjectId, transfer);
        if (transfer.Source.EndpointId != session.Scope.RecipientId && transfer.Destination.EndpointId != session.Scope.RecipientId)
            throw new RelayException("capability_denied", "The selected session is not a participant in this transfer.");
        return await work.SubmitAsync(new(requestId, session.Scope.RecipientId.ToString("D"), "transfer", SessionId: sessionId,
            Scope: session.Scope, TaskId: requestId, AttemptId: requestId, Artifact: transfer), token,
            new("Transfer · " + transfer.Source.RelativePath, policy.Configuration().Workspaces.Single(item => item.Id == transfer.Destination.WorkspaceId).Location));
    }

    public async Task<WorkRecord> ResultAsync(CallerIdentity caller, Guid requestId, CancellationToken token)
    {
        var retained = await work.ReadAsync(requestId, token);
        using var projectLease = Admission.Enter(retained.Request.Scope?.ProjectId ?? Guid.Empty);
        return await ResultCore(caller, requestId, token);
    }

    private async Task<WorkRecord> ResultCore(CallerIdentity caller, Guid requestId, CancellationToken token)
    {
        using var policySnapshot = policy.Pin();
        var result = await work.ReadAsync(requestId, token);
        if (result.Request.Scope is not { } scope) throw new RelayException("legacy_unassigned", "This legacy operation has no authorized project assignment.");
        policy.Demand(caller, scope.ProjectId, RelayCapabilities.ReadHistory);
        return result.State == "unknown" && recovery is not null && policy.Configuration().Projects.Single(project => project.Id == scope.ProjectId).Enabled ? await recovery.ReconcileAsync(result, token) : result;
    }

    public async Task<WorkRecord> CancelAsync(CallerIdentity caller, Guid requestId, CancellationToken token)
    {
        var retained = await work.ReadAsync(requestId, token);
        using var projectLease = Admission.Enter(retained.Request.Scope?.ProjectId ?? Guid.Empty);
        using var policySnapshot = policy.Pin();
        var record = await ResultCore(caller, requestId, token);
        if (record.Request.SessionId is not { } id) throw new RelayException("capability_denied", "Only session operations support targeted cancellation.");
        using var lease = await Gate(id, token);
        await OwnedAsync(caller, id, RelayCapabilities.ManageSession, token);
        if (await work.IsOperationActiveAsync(requestId, token))
        {
            if (record.Request.Kind == "message") await sessions.ControlAsync(id, "interrupt", token);
            await work.InterruptSessionAsync(id, token);
        }
        return await work.ReadAsync(requestId, token);
    }

    public async Task<TransferPartial> TransferPartialAsync(CallerIdentity caller, Guid requestId, bool remove, CancellationToken token)
    {
        var retained = await work.ReadAsync(requestId, token);
        using var projectLease = Admission.Enter(retained.Request.Scope?.ProjectId ?? Guid.Empty);
        using var policySnapshot = policy.Pin();
        var record = await ResultCore(caller, requestId, token);
        if (record.Request.Kind != "transfer" || record.Request.SessionId is not { } id || record.Request.Artifact is not { } artifact)
            throw new RelayException("invalid_operation", "Select a scoped file-transfer operation.");
        using var lease = await Gate(id, token);
        await OwnedAsync(caller, id, RelayCapabilities.ManageSession, token);
        policy.Transfer(caller, record.Request.Scope!.ProjectId, artifact);
        if (work.IsSessionActive(id)) throw new RelayException("busy", "Wait for the session operation before inspecting or cleaning its partial file.");
        return await (transfers ?? throw new RelayException("adapter_unavailable", "No transfer-maintenance adapter is installed.")).PartialAsync(record.Request, remove, token);
    }

    public Task<ReadPage<WorkRecord>> ResultsAsync(CallerIdentity caller, ReadQuery query, CancellationToken token)
    { using var policySnapshot = policy.Pin(); policy.Demand(caller, query.ProjectId, RelayCapabilities.ReadHistory); return journal.OperationsAsync(query, token); }
    public Task<ReadPage<WorkEvent>> EventsAsync(CallerIdentity caller, ReadQuery query, CancellationToken token)
    { using var policySnapshot = policy.Pin(); policy.Demand(caller, query.ProjectId, RelayCapabilities.ReadHistory); return journal.EventsAsync(query, token); }

    public async Task<ReadPage<InboxMessage>> InboxAsync(CallerIdentity caller, ReadQuery query, CancellationToken token)
    {
        using var projectLease = Admission.Enter(query.ProjectId);
        using var policySnapshot = policy.Pin();
        policy.Demand(caller, query.ProjectId, RelayCapabilities.ReceiveMessage);
        if (query.SessionId is { } id)
        {
            var session = await OwnedAsync(caller, id, RelayCapabilities.ReceiveMessage, token);
            if (session.Scope.ProjectId != query.ProjectId) throw new RelayException("capability_denied", "Session project mismatch.");
        }
        var warnings = policy.Configuration().Projects.Single(project => project.Id == query.ProjectId).Enabled
            ? await sessions.RefreshMessagesAsync(query.ProjectId, caller.EndpointId, query.SessionId, token) : [];
        return (await journal.MessagesAsync(query, caller.EndpointId, token)) with { Warnings = warnings };
    }

    private RelayWorkspace Workspace(OperationScope scope) => policy.Configuration().Workspaces.Single(item => item.Id == scope.WorkspaceId);
    private async Task<SessionSnapshot> OwnedAsync(CallerIdentity caller, Guid id, string capability, CancellationToken token)
    {
        var session = await sessions.GetAsync(id, token); policy.Demand(caller, session.Scope.ProjectId, capability);
        if (session.Scope.SenderId != caller.EndpointId) throw new RelayException("capability_denied", "This connection does not own the selected session.");
        return session;
    }
}
