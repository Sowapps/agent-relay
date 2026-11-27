using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record ProjectAction(string Name, Guid? ProjectId = null, Guid? EndpointId = null, Guid? WorkspaceId = null,
    Guid? SessionId = null, Guid? RequestId = null, string? Text = null, string? DisplayName = null, string? CommandId = null,
    string? Control = null, ReadQuery? Query = null, ArtifactTransfer? Transfer = null, Guid? TaskId = null, Guid? ActorId = null,
    string? State = null, string? ExpectedState = null, string? ApprovalRevision = null, ProjectAccessChange? Access = null,
    ProjectReactions? Reactions = null, Guid? AfterRequestId = null, RequestChange? RequestChange = null);
public sealed record ProjectReply(ProjectConfiguration? Configuration = null, SessionSnapshot? Session = null, WorkRecord? Operation = null,
    ReadPage<SessionSnapshot>? Sessions = null, ReadPage<WorkRecord>? Operations = null, ReadPage<InboxMessage>? Messages = null, HostReport? Host = null, ReadPage<WorkEvent>? Events = null, TransferPartial? Partial = null, RelayProject? Project = null,
    CancellationProgress? Cancellation = null, SessionTerminal? Terminal = null, TerminalSignalReceipt? TerminalSignal = null,
    ProjectAccessEntry? ProjectAccess = null, ApprovalProgress? Approval = null, ProjectReactions? Reactions = null,
    ReactionTarget[]? ReactionTargets = null, ReactionRun[]? ReactionRuns = null, SessionActivityObservation? SessionActivity = null)
{
    internal static ProjectReply WithCancellation(WorkRecord operation) => new(Operation: operation,
        Cancellation: CancellationProgress.From(operation), Approval: ApprovalProgress.From(operation));
}

public sealed record ReactionTarget(Guid ActorId, string Actor, Guid EndpointId, string Destination, string Environment,
    string Endpoint, Guid WorkspaceId, string Workspace, string CommandId, string Command);

/// <summary>Participant-facing projection of cancellation for this one request.</summary>
public sealed record CancellationProgress(Guid SessionId, string State, string? NextAction, bool SessionRequiresResolution)
{
    internal static CancellationProgress? From(WorkRecord operation)
    {
        if (operation.Request.SessionId is not { } sessionId || operation is
            { CancellationRequestedAt: null, ApprovalExpiredAt: null, ApprovalStopRequestedAt: null, CancellationConfirmedAt: null }) return null;
        if (operation.CancellationConfirmedAt is not null) return new(sessionId, "stop_confirmed", null, false);
        if (operation.ApprovalResumeVerifiedAt is not null) return new(sessionId, "turn_finished", null, false);
        if (WorkExecution.ApprovalStopPending(operation))
            return operation.SessionClosedAt is null ? new(sessionId, "stop_unconfirmed", "close_session", true) :
                new(sessionId, "stop_unconfirmed", null, false);
        return operation.State switch
        {
            "awaiting_approval" => new(sessionId, "close_required", "close_session", true),
            "approval_expired" => new(sessionId, "approval_expired", "close_session", true),
            "unknown" when WorkExecution.NeedsApprovalObservation(operation) => new(sessionId, "turn_unconfirmed", "get_result", true),
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
    private readonly Task replyLoop;
    private readonly Task reactionLoop;
    private readonly Task followUpLoop;
    private readonly ProjectReactionService reactions;
    private readonly SettingsStore technical;

    public ProjectCoordinator(string dataDirectory, Action<Exception>? reportError = null)
        : this(dataDirectory, reportError, new PromotionAdmission(dataDirectory)) { }

    internal ProjectCoordinator(string dataDirectory, Action<Exception>? reportError, PromotionAdmission promotion)
    {
        Settings = new(dataDirectory); var database = new RelayDatabase(dataDirectory); store = new(database);
        observation = new(database);
        technical = new SettingsStore(dataDirectory);
        WorkService? activity = null;
        var native = NativeAdapterComposition.Create(dataDirectory, technical, Settings, store, id => activity?.IsSessionActive(id) ?? false);
        adapter = native.Sessions;
        var executor = native.Executor;
        Work = new(store, executor, () => Settings.Load().Endpoints.Select(endpoint => new WorkCapability(endpoint.Id.ToString("D"), true, true, true)).ToArray(), technical.Load().MaximumConcurrency);
        activity = Work;
        Application = new(new(Settings), Work, adapter, new ProjectJournal(database), new EndpointDiagnostics(technical), executor, executor, Settings);
        reactions = new(new SqliteProjectReactions(database), new(Settings), Work, native.ReactionRelease,
            (endpoint, rule) => ScopedCommands.ValidateTarget(technical.Load(), endpoint, rule), Application.Admission.EnterWork, executor);
        reactionLoop = RunBackgroundSweepAsync(promotion, token => reactions.SweepAsync(DateTimeOffset.UtcNow, token), reportError);
        var replies = new ReplyMonitor(store, (IReplyObservation)executor, reportError);
        var followUps = new CancellationFollowUpMonitor(store, Application, reportError);
        followUpLoop = RunBackgroundSweepAsync(promotion, followUps.SweepAsync, reportError);
        replyLoop = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            try
            {
                while (await timer.WaitForNextTickAsync(archiveStop.Token))
                {
                    using var admission = promotion.TryEnter();
                    if (admission is null) continue;
                    try { await replies.SweepAsync(archiveStop.Token); }
                    catch (Exception error) when (!archiveStop.IsCancellationRequested) { reportError?.Invoke(error); }
                }
            }
            catch (OperationCanceledException) when (archiveStop.IsCancellationRequested) { }
        });
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
                        await Work.ResumeDueRetriesAsync(now, archiveStop.Token);
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

    private async Task RunBackgroundSweepAsync(PromotionAdmission promotion, Func<CancellationToken, Task> sweep, Action<Exception>? reportError)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(archiveStop.Token))
            {
                using var admission = promotion.TryEnter();
                if (admission is null) continue;
                try { await sweep(archiveStop.Token); }
                catch (Exception error) when (!archiveStop.IsCancellationRequested) { reportError?.Invoke(error); }
            }
        }
        catch (OperationCanceledException) when (archiveStop.IsCancellationRequested) { }
    }

    public async Task<ProjectReply> DispatchAsync(AuthenticatedConnection connection, ProjectAction action, CancellationToken token)
    {
        var caller = connection.Caller;
        static Guid Required(Guid? value, string name) => value is { } id && id != Guid.Empty ? id : throw new RelayException("invalid_request", name + " is required.");
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
        if (action.Name is "get_project_reactions" or "set_project_reactions")
        {
            var project = Required(action.ProjectId, "projectId");
            using var admission = action.Name == "set_project_reactions" ? Application.Admission.Enter(project) : Application.Admission.EnterWork(project);
            var policy = new ProjectPolicy(Settings);
            using var pinned = policy.Pin();
            if (!connection.Administrator) policy.Demand(caller, project, RelayCapabilities.ManageProjectReactions);
            var configuration = policy.Configuration();
            if (!configuration.Projects.Any(item => item.Id == project))
                throw new RelayException("not_found", "Unknown project.", false);
            if (action.Name == "set_project_reactions")
            {
                var replacement = action.Reactions ?? throw new RelayException("invalid_request", "Expected reaction revision and rules.", false);
                foreach (var session in (replacement.Rules ?? []).Where(rule => rule is { Enabled: true, SessionId: not null }).Select(rule => rule.SessionId!.Value).Distinct())
                    if ((await adapter.GetAsync(session, token)).Scope.ProjectId != project)
                        throw new RelayException("invalid_request", "The selected session does not belong to this project.", false);
                return new(Reactions: await reactions.SaveAsync(project, replacement, token));
            }
            return new(Reactions: await reactions.ReadAsync(project, token), ReactionTargets: ReactionTargets(configuration, project));
        }
        if (action.Name == "list_reaction_results")
        {
            var project = Required(action.ProjectId, "projectId");
            var request = Required(action.RequestId, "requestId");
            var source = connection.Administrator ? await Work.ReadAsync(request, token) : await Application.ResultAsync(caller, request, token);
            DemandReactionSource(project, source);
            return new(ReactionRuns: (await reactions.ResultsAsync(project, request, token)).ToArray());
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
        var identifier = action.RequestId ?? Guid.NewGuid();
        if (action.AfterRequestId is { } after)
        {
            var source = await Application.ResultAsync(caller, after, token);
            var target = action.Name switch
            {
                "send_message" => (await Application.SessionAsync(caller, Required(action.SessionId, "sessionId"), token)).Scope.ProjectId,
                "execute_command" => Required(action.ProjectId, "projectId"),
                _ => throw new RelayException("invalid_request", "This action cannot be linked to a reaction.", false)
            };
            DemandReactionSource(target, source);
            if (MonitorProjection.InProgress(source.State) || source.State == "awaiting_approval")
                throw new RelayException("reaction_pending", "The source message has no final result yet.", false);
            await reactions.DemandContinuationAsync(target, after, token);
        }
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
            "get_result" or "cancel_operation" => await DescribeOperationAsync(caller, action, Required(action.RequestId, "requestId"), token),
            "change_project_request" => ProjectReply.WithCancellation(await Application.ChangeRequestAsync(caller,
                Required(action.RequestId, "requestId"), action.RequestChange ?? throw new RelayException("invalid_request", "Request change is required.", false), token)),
            "reschedule_project_request" => new(Operation: await Application.RescheduleRequestAsync(caller,
                Required(action.RequestId, "requestId"), Required(action.AfterRequestId, "newRequestId"), action.SessionId, action.Text, token)),
            "inspect_transfer" or "cleanup_transfer" => new(Partial: await Application.TransferPartialAsync(caller, Required(action.RequestId, "requestId"), action.Name == "cleanup_transfer", token)),
            "list_results" => new(Operations: await Application.ResultsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "list_events" => new(Events: await Application.EventsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "read_inbox" => new(Messages: await Application.InboxAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            _ => throw new RelayException("invalid_operation", "Unsupported project operation.")
        };
    }

    private async Task<ProjectReply> DescribeOperationAsync(CallerIdentity caller, ProjectAction action, Guid request, CancellationToken token)
    {
        var operation = action.Name == "get_result" ? await Application.ResultAsync(caller, request, token)
            : await Application.CancelAsync(caller, request, token, action.Text);
        return ProjectReply.WithCancellation(operation) with
        { SessionActivity = await Application.RequestActivityAsync(caller, operation, token) };
    }

    private static void DemandReactionSource(Guid project, WorkRecord source)
    {
        if (source.Request.Scope?.ProjectId != project || source.Request.Kind != "message" || source.Request.SessionId is null)
            throw new RelayException("invalid_request", "The source must be a retained session message from the same project.", false);
    }

    private ReactionTarget[] ReactionTargets(ProjectConfiguration configuration, Guid project)
    {
        var settings = technical.Load();
        return (from member in configuration.Memberships
                where member.ProjectId == project && member.Capabilities.Contains(RelayCapabilities.RunCommand)
                    && member.Capabilities.Contains(RelayCapabilities.ReadFile)
                let actor = configuration.Endpoints.Single(item => item.Id == member.EndpointId)
                from access in member.Workspaces where access.Read
                let workspace = configuration.Workspaces.Single(item => item.Id == access.WorkspaceId)
                where configuration.Memberships.Any(item => item.ProjectId == project && item.EndpointId == workspace.EndpointId)
                let target = configuration.Endpoints.Single(item => item.Id == workspace.EndpointId)
                let destination = ScopedCommands.Destination(target)
                where destination != null
                from command in ScopedCommands.Declared(settings, target)
                where (member.CommandIds ?? []).Contains(command.Id, StringComparer.Ordinal)
                select new ReactionTarget(actor.Id, actor.Name, target.Id, destination, target.BindingId, target.Name,
                    workspace.Id, workspace.Name, command.Id, command.Name)).ToArray();
    }

    private ReadPage<SessionSnapshot> Convert(ReadPage<RelaySession> page) => new(page.Items.Where(item => item.Definition is not null)
        .Select(item => new SessionSnapshot(item.Id, item.Name, item.Definition!.Scope, item.State, Diagnostic: item.Diagnostic, WorkActive: Work.IsSessionActive(item.Id))).ToArray(), page.NextCursor, page.HasMore);

    public async ValueTask DisposeAsync()
    {
        archiveStop.Cancel();
        try { await Task.WhenAll(archiveLoop, replyLoop, reactionLoop, followUpLoop); }
        finally { archiveStop.Dispose(); await Work.DisposeAsync(); }
    }
}
