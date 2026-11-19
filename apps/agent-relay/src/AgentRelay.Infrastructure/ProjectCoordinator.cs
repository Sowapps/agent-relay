using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record ProjectAction(string Name, Guid? ProjectId = null, Guid? EndpointId = null, Guid? WorkspaceId = null,
    Guid? SessionId = null, Guid? RequestId = null, string? Text = null, string? DisplayName = null, string? CommandId = null,
    string? Control = null, ReadQuery? Query = null, ArtifactTransfer? Transfer = null, Guid? TaskId = null, Guid? ActorId = null,
    string? State = null, string? ExpectedState = null, string? ApprovalRevision = null, ProjectAccessChange? Access = null);
public sealed record ProjectReply(ProjectConfiguration? Configuration = null, SessionSnapshot? Session = null, WorkRecord? Operation = null,
    ReadPage<SessionSnapshot>? Sessions = null, ReadPage<WorkRecord>? Operations = null, ReadPage<InboxMessage>? Messages = null, HostReport? Host = null, ReadPage<WorkEvent>? Events = null, TransferPartial? Partial = null, RelayProject? Project = null,
    CancellationProgress? Cancellation = null, SessionTerminal? Terminal = null, TerminalSignalReceipt? TerminalSignal = null,
    ProjectAccessEntry? ProjectAccess = null)
{
    internal static ProjectReply WithCancellation(WorkRecord operation) => new(Operation: operation, Cancellation: CancellationProgress.From(operation));
}

/// <summary>Participant-facing projection of cancellation for this one request.</summary>
public sealed record CancellationProgress(Guid SessionId, string State, string? NextAction, bool SessionRequiresResolution)
{
    internal static CancellationProgress? From(WorkRecord operation)
    {
        if (operation.Request.SessionId is not { } sessionId || operation is
            { CancellationRequestedAt: null, ApprovalExpiredAt: null, ApprovalStopRequestedAt: null, CancellationConfirmedAt: null }) return null;
        if (operation.CancellationConfirmedAt is not null) return new(sessionId, "stop_confirmed", null, false);
        if (WorkExecution.ApprovalStopPending(operation))
            return operation.SessionClosedAt is null ? new(sessionId, "stop_unconfirmed", "close_session", true) :
                new(sessionId, "stop_unconfirmed", null, false);
        return operation.State switch
        {
            "awaiting_approval" => new(sessionId, "close_required", "close_session", true),
            "approval_expired" => new(sessionId, "approval_expired", "close_session", true),
            "completed" or "failed" => new(sessionId, "finished", null, false),
            "unknown" => new(sessionId, "stop_unconfirmed", null, false),
            _ => new(sessionId, "requested", "get_result", false)
        };
    }
}

public sealed class ProjectCoordinator : IAsyncDisposable
{
    /// <summary>Local administrator recovery for a project already draining work; the database is opened read-only.</summary>
    public static async Task<RelayProject> RecheckDormantArchiveAsync(string data, Guid projectId, CancellationToken token)
    {
        new AgentRelay.Protocol.McpDeploymentAdmission(data).DemandOpen();
        var settings = new ProjectSettingsStore(data);
        RelayProject Selected() => settings.Load().Projects.SingleOrDefault(project => project.Id == projectId)
            ?? throw new RelayException("not_found", "Unknown project.");
        var project = Selected();
        if (!project.Enabled) return project;
        if (!project.ArchiveRequested)
            throw new RelayException("archive_unverified", "The project must already be draining under the running relay.", false);
        var database = new RelayDatabase(data, readOnly: true);
        var store = new SqliteWorkStore(database);
        var technical = new SettingsStore(data);
        var bridge = new WslBridge();
        var agents = new AgentControl(technical, bridge);
        var sessions = new SessionService(database, technical, agents, bridge);
        var context = new ScopedSessionContext(settings, sessions);
        var journal = new ProjectJournal(database);
        var inbox = new ScopedInbox(sessions, bridge, store, journal, new(database, "cursors"), context);
        var adapter = new ScopedSessions(sessions, agents, settings, journal, inbox, context, _ => false, detachedArchiveCheck: true);
        await using var work = new WorkService(store, new ArchiveReadOnlyExecutor(), () => []);
        var application = new RelayApplication(new(settings), work, adapter, journal, projectStates: settings);
        await application.ReconcileProjectArchiveAsync(projectId, token);
        return Selected();
    }

    private sealed class ArchiveReadOnlyExecutor : IWorkExecutor
    {
        public Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token) =>
            throw new RelayException("capability_denied", "The detached archive check cannot execute work.", false);
    }
    public ProjectSettingsStore Settings { get; }
    public WorkService Work { get; }
    public RelayApplication Application { get; }
    private readonly SqliteWorkStore store;
    private readonly IRelaySessions adapter;
    private readonly ProjectJournal observation;

    private readonly CancellationTokenSource archiveStop = new();
    private readonly Task archiveLoop;

    public ProjectCoordinator(string dataDirectory, Action<Exception>? reportError = null)
        : this(dataDirectory, reportError, new PromotionAdmission(dataDirectory)) { }

    internal ProjectCoordinator(string dataDirectory, Action<Exception>? reportError, PromotionAdmission promotion)
    {
        Settings = new(dataDirectory); var database = new RelayDatabase(dataDirectory); store = new(database);
        observation = new(database);
        var technical = new SettingsStore(dataDirectory);
        WorkService? activity = null;
        var native = NativeAdapterComposition.Create(dataDirectory, technical, Settings, store, id => activity?.IsSessionActive(id) ?? false);
        adapter = native.Sessions;
        var executor = native.Executor;
        Work = new(store, executor, () => Settings.Load().Endpoints.Select(endpoint => new WorkCapability(endpoint.Id.ToString("D"), true, true, true)).ToArray(), technical.Load().MaximumConcurrency);
        activity = Work;
        Application = new(new(Settings), Work, adapter, new ProjectJournal(database), new EndpointDiagnostics(technical), executor, executor, Settings);
        archiveLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            var nextApprovalSweep = DateTimeOffset.MinValue;
            try
            {
                while (await timer.WaitForNextTickAsync(archiveStop.Token))
                {
                    try
                    {
                        using var admission = promotion.TryEnter();
                        if (admission is null) continue;
                        var now = DateTimeOffset.UtcNow;
                        if (now >= nextApprovalSweep)
                        {
                            nextApprovalSweep = now.AddSeconds(30);
                            await store.ExpireDueApprovalsAsync(now, archiveStop.Token);
                        }
                        await Application.ReconcileArchivesAsync(archiveStop.Token);
                    }
                    catch (Exception error) when (!archiveStop.IsCancellationRequested) { reportError?.Invoke(error); }
                }
            }
            catch (OperationCanceledException) when (archiveStop.IsCancellationRequested) { }
        });
    }

    public async Task<ProjectReply> DispatchAsync(AuthenticatedConnection connection, ProjectAction action, CancellationToken token)
    {
        var caller = connection.Caller;
        if (!connection.Administrator && action.ActorId is { } suppliedActor && suppliedActor != caller.EndpointId)
            throw new RelayException("capability_denied", "Caller identity cannot be supplied by an agent request.");
        if (action.Name == "set_project_state")
        {
            if (action.ProjectId is not { } id || id == Guid.Empty || action.State is not ("active" or "archived") || action.ExpectedState is not ("active" or "archived"))
                throw new RelayException("invalid_request", "Explicit project, state and expectedState (active/archived) are required.");
            var enabled = action.State == "active"; var expected = action.ExpectedState == "active";
            return new(Project: connection.Administrator
                ? await Application.SetProjectStateAsAdministratorAsync(id, enabled, expected, token)
                : await Application.SetProjectStateAsync(caller, id, enabled, expected, token));
        }
        if (connection.Administrator)
        {
            if (action.Name == "configuration") return new(Configuration: Settings.Load());
            if (action.Name.StartsWith("observe_", StringComparison.Ordinal))
            {
                if (action.Name == "observe_operation")
                {
                    var record = await store.FindAsync(action.RequestId ?? Guid.Empty, token);
                    if (record?.Request.Scope?.ProjectId != action.ProjectId || action.ProjectId is null) throw new RelayException("not_found", "Unknown project operation.");
                    return new(Operation: record);
                }
                var query = action.Query ?? throw new RelayException("invalid_query", "An explicit project observation is required.");
                if (!Settings.Load().Projects.Any(project => project.Id == query.ProjectId)) throw new RelayException("not_found", "Unknown project.");
                var journal = observation;
                return action.Name switch
                {
                    "observe_operations" => new(Operations: await journal.OperationsAsync(query, token)),
                    "observe_messages" => new(Messages: await journal.ObserveMessagesAsync(query, token)),
                    "observe_events" => new(Events: await journal.EventsAsync(query, token)),
                    "observe_sessions" => new(Sessions: Convert(await journal.ObserveSessionsAsync(query, token))),
                    _ => throw new RelayException("invalid_operation", "Unknown observation.")
                };
            }
            var actor = action.ActorId ?? throw new RelayException("authentication_required", "Select the acting project participant for an administrative console action.");
            caller = new(actor, Settings.Load().Memberships.Where(member => member.EndpointId == actor).Select(member => member.ProjectId).ToArray());
        }
        else if (action.ActorId is { } actor && actor != caller.EndpointId) throw new RelayException("capability_denied", "Caller identity cannot be supplied by an agent request.");
        Guid Required(Guid? value, string name) => value is { } id && id != Guid.Empty ? id : throw new RelayException("invalid_request", name + " is required.");
        var identifier = action.RequestId ?? Guid.NewGuid();
        return action.Name switch
        {
            "configuration" => new(Configuration: Application.Discover(caller)),
            "get_project_access" => new(ProjectAccess: Application.ReadProjectAccess(caller, Required(action.ProjectId, "projectId"), Required(action.EndpointId, "endpointId"))),
            "set_project_access" => new(ProjectAccess: await Application.SetProjectAccessAsync(caller, Required(action.ProjectId, "projectId"),
                Required(action.EndpointId, "endpointId"), action.ExpectedState ?? "",
                action.Access ?? throw new RelayException("invalid_request", "Project access fields are required.", false), token)),
            "inspect_host" => new(Host: await Application.InspectHostAsync(caller, Required(action.ProjectId, "projectId"), Required(action.EndpointId, "endpointId"), token)),
            "open_session" => new(Session: await Application.OpenSessionAsync(caller, Required(action.ProjectId, "projectId"), Required(action.EndpointId, "endpointId"), Required(action.WorkspaceId, "workspaceId"), Required(action.SessionId, "sessionId"), action.DisplayName ?? "", token)),
            "get_session" => new(Session: await Application.SessionAsync(caller, Required(action.SessionId, "sessionId"), token)),
            "list_sessions" => new(Sessions: await Application.SessionsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "control_session" => new(Session: await Application.ControlSessionAsync(caller, Required(action.SessionId, "sessionId"), action.Control ?? "", token, action.RequestId)),
            "read_session_terminal" => new(Terminal: await Application.ReadSessionTerminalAsync(caller, Required(action.SessionId, "sessionId"), token)),
            "signal_session_terminal" => new(TerminalSignal: await Application.SignalSessionTerminalAsync(caller,
                Required(action.SessionId, "sessionId"), action.Control ?? "", action.ApprovalRevision, token)),
            "send_message" => new(Operation: await Application.MessageAsync(caller, Required(action.SessionId, "sessionId"), identifier, action.Text ?? "", action.TaskId, token)),
            "execute_command" => new(Operation: await Application.CommandAsync(caller, Required(action.ProjectId, "projectId"), Required(action.EndpointId, "endpointId"), Required(action.WorkspaceId, "workspaceId"), action.CommandId ?? "", identifier, token)),
            "transfer_file" => new(Operation: await Application.TransferAsync(caller, Required(action.SessionId, "sessionId"), action.Transfer ?? throw new RelayException("invalid_request", "Source and destination artifacts are required."), identifier, token)),
            "get_result" => ProjectReply.WithCancellation(await Application.ResultAsync(caller, Required(action.RequestId, "requestId"), token)),
            "cancel_operation" => ProjectReply.WithCancellation(await Application.CancelAsync(caller, Required(action.RequestId, "requestId"), token)),
            "inspect_transfer" or "cleanup_transfer" => new(Partial: await Application.TransferPartialAsync(caller, Required(action.RequestId, "requestId"), action.Name == "cleanup_transfer", token)),
            "list_results" => new(Operations: await Application.ResultsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "list_events" => new(Events: await Application.EventsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "read_inbox" => new(Messages: await Application.InboxAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            _ => throw new RelayException("invalid_operation", "Unsupported project operation.")
        };
    }

    private ReadPage<SessionSnapshot> Convert(ReadPage<RelaySession> page) => new(page.Items.Where(item => item.Definition is not null)
        .Select(item => new SessionSnapshot(item.Id, item.Name, item.Definition!.Scope, item.State, Diagnostic: item.Diagnostic, WorkActive: Work.IsSessionActive(item.Id))).ToArray(), page.NextCursor, page.HasMore);

    public async ValueTask DisposeAsync()
    {
        archiveStop.Cancel();
        try { await archiveLoop; }
        finally { archiveStop.Dispose(); await Work.DisposeAsync(); }
    }
}
