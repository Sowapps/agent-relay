using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record ProjectAction(string Name, Guid? ProjectId = null, Guid? EndpointId = null, Guid? WorkspaceId = null,
    Guid? SessionId = null, Guid? RequestId = null, string? Text = null, string? DisplayName = null, string? CommandId = null,
    string? Control = null, ReadQuery? Query = null, ArtifactTransfer? Transfer = null, Guid? TaskId = null, Guid? ActorId = null);
public sealed record ProjectReply(ProjectConfiguration? Configuration = null, SessionSnapshot? Session = null, WorkRecord? Operation = null,
    ReadPage<SessionSnapshot>? Sessions = null, ReadPage<WorkRecord>? Operations = null, ReadPage<InboxMessage>? Messages = null, HostReport? Host = null, ReadPage<WorkEvent>? Events = null, TransferPartial? Partial = null);

public sealed class ProjectCoordinator : IAsyncDisposable
{
    public ProjectSettingsStore Settings { get; }
    public WorkService Work { get; }
    public RelayApplication Application { get; }
    private readonly SqliteWorkStore store;
    private readonly ScopedSessions adapter;
    private readonly ProjectJournal observation;

    public ProjectCoordinator(string dataDirectory)
    {
        Settings = new(dataDirectory); var database = new RelayDatabase(dataDirectory); store = new(database);
        observation = new(database);
        var technical = new SettingsStore(dataDirectory);
        WorkService? activity = null;
        adapter = new(dataDirectory, technical, Settings, store, id => activity?.IsSessionActive(id) ?? false);
        Work = new(store, adapter, () => Settings.Load().Endpoints.Select(endpoint => new WorkCapability(endpoint.Id.ToString("D"), true, true, true)).ToArray(), technical.Load().MaximumConcurrency);
        activity = Work;
        Application = new(new(Settings), Work, adapter, new ProjectJournal(database), new EndpointDiagnostics(technical), adapter, adapter);
    }

    public async Task<ProjectReply> DispatchAsync(AuthenticatedConnection connection, ProjectAction action, CancellationToken token)
    {
        var caller = connection.Caller;
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
            "inspect_host" => new(Host: await Application.InspectHostAsync(caller, Required(action.ProjectId, "projectId"), Required(action.EndpointId, "endpointId"), token)),
            "open_session" => new(Session: await Application.OpenSessionAsync(caller, Required(action.ProjectId, "projectId"), Required(action.EndpointId, "endpointId"), Required(action.WorkspaceId, "workspaceId"), Required(action.SessionId, "sessionId"), action.DisplayName ?? "", token)),
            "get_session" => new(Session: await Application.SessionAsync(caller, Required(action.SessionId, "sessionId"), token)),
            "list_sessions" => new(Sessions: await Application.SessionsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "control_session" => new(Session: await Application.ControlSessionAsync(caller, Required(action.SessionId, "sessionId"), action.Control ?? "", token)),
            "send_message" => new(Operation: await Application.MessageAsync(caller, Required(action.SessionId, "sessionId"), identifier, action.Text ?? "", action.TaskId, token)),
            "execute_command" => new(Operation: await Application.CommandAsync(caller, Required(action.ProjectId, "projectId"), Required(action.EndpointId, "endpointId"), Required(action.WorkspaceId, "workspaceId"), action.CommandId ?? "", identifier, token)),
            "transfer_file" => new(Operation: await Application.TransferAsync(caller, Required(action.SessionId, "sessionId"), action.Transfer ?? throw new RelayException("invalid_request", "Source and destination artifacts are required."), identifier, token)),
            "get_result" => new(Operation: await Application.ResultAsync(caller, Required(action.RequestId, "requestId"), token)),
            "cancel_operation" => new(Operation: await Application.CancelAsync(caller, Required(action.RequestId, "requestId"), token)),
            "inspect_transfer" or "cleanup_transfer" => new(Partial: await Application.TransferPartialAsync(caller, Required(action.RequestId, "requestId"), action.Name == "cleanup_transfer", token)),
            "list_results" => new(Operations: await Application.ResultsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "list_events" => new(Events: await Application.EventsAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            "read_inbox" => new(Messages: await Application.InboxAsync(caller, action.Query ?? new(Required(action.ProjectId, "projectId")), token)),
            _ => throw new RelayException("invalid_operation", "Unsupported project operation.")
        };
    }

    private ReadPage<SessionSnapshot> Convert(ReadPage<RelaySession> page) => new(page.Items.Where(item => item.Definition is not null)
        .Select(item => new SessionSnapshot(item.Id, item.Name, item.Definition!.Scope, item.State, Diagnostic: item.Diagnostic, WorkActive: Work.IsSessionActive(item.Id))).ToArray(), page.NextCursor, page.HasMore);

    public ValueTask DisposeAsync() => Work.DisposeAsync();
}
