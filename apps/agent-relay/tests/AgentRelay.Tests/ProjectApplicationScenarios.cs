using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class ProjectApplicationScenarios
{
    public static async Task Run(string data)
    {
        var project = Guid.NewGuid(); var otherProject = Guid.NewGuid(); var owner = Guid.NewGuid(); var worker = Guid.NewGuid(); var workspace = Guid.NewGuid();
        var configuration = new ProjectConfiguration(1, [new(project, "Scoped A"), new(otherProject, "Scoped B")],
            [new(owner, "Main", "memory", "main"), new(worker, "Secondary", "memory", "secondary")], [new(workspace, worker, "Workspace", "memory://project-a")],
            [new(project, owner, "main", [RelayCapabilities.SendMessage, RelayCapabilities.ReceiveMessage, RelayCapabilities.ManageSession, RelayCapabilities.ReadHistory, RelayCapabilities.ReadFile], [new(workspace, true)]),
             new(project, worker, "secondary", [RelayCapabilities.ReceiveMessage, RelayCapabilities.SendMessage], [])]);
        var settings = new ProjectSettingsStore(data); settings.Save(configuration);
        var store = new SqliteWorkStore(new RelayDatabase(data)); var adapter = new MemoryAdapter(store);
        await using var work = new WorkService(store, adapter, () => [new(worker.ToString("D"), false, true)]);
        var app = new RelayApplication(new(settings), work, adapter, new ProjectJournal(new RelayDatabase(data)));
        var caller = new CallerIdentity(owner, [project]);
        var session = await app.OpenSessionAsync(caller, project, worker, workspace, Guid.NewGuid(), "Memory contract", CancellationToken.None);
        var request = Guid.NewGuid(); var exact = "line one\n\tline two — été";
        await app.MessageAsync(caller, session.Id, request, exact, null, CancellationToken.None);
        var deadline = DateTime.UtcNow.AddSeconds(5); WorkRecord result;
        do { await Task.Delay(10); result = await app.ResultAsync(caller, request, CancellationToken.None); } while (result.State is "pending" or "running" && DateTime.UtcNow < deadline);
        if (result.State != "completed" || result.Result?.Output != exact || result.Request.Scope?.ProjectId != project || result.Request.TaskId != request || result.Request.AttemptId != request)
            throw new Exception("Neutral application contract lost text, context or identities.");
        var inbox = await app.InboxAsync(caller, new(project), CancellationToken.None);
        if (inbox.Items.Single().RecipientEndpointId != owner || inbox.Items.Single().InReplyTo != request) throw new Exception("Memory message reached another participant.");
        var foreign = new WorkRecord(new(Guid.NewGuid(), worker.ToString("D"), "message", "private", Scope: new(otherProject, owner, worker)), "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await store.SaveAsync(foreign, CancellationToken.None);
        try { await app.ResultAsync(caller, foreign.Request.Id, CancellationToken.None); throw new Exception("Foreign result exposed."); }
        catch (RelayException error) when (error.Code == "capability_denied") { }
        var history = await app.ResultsAsync(caller, new(project, Search: "line two"), CancellationToken.None);
        if (history.Items.Count != 1 || history.Items[0].Request.Id != request) throw new Exception("Project/content query leaked another operation.");
        var events = await app.EventsAsync(caller, new(project, TaskId: request), CancellationToken.None);
        if (events.Items.Count != 3 || !events.Items.Select(item => item.State).ToHashSet().SetEquals(["pending", "running", "completed"]) || events.Items.Any(item => item.RequestId != request || item.ProjectId != project))
            throw new Exception("Durable task/attempt transition history is incomplete or cross-project.");
        await app.MessageAsync(caller, session.Id, request, exact, null, CancellationToken.None);
        if ((await app.EventsAsync(caller, new(project, TaskId: request), CancellationToken.None)).Items.Count != 3) throw new Exception("Idempotent lookup added another transition/attempt.");
        var unicode = string.Concat(Enumerable.Repeat("🚀", 2000));
        for (var index = 0; index < 24; index++) await store.AddInboxAsync(new(Guid.NewGuid(), worker.ToString("D"), worker.ToString("D"), owner.ToString("D"), unicode, null, DateTimeOffset.UtcNow, session.Id, project, worker, owner), CancellationToken.None);
        var bounded = await app.InboxAsync(caller, new(project, Limit: 100), CancellationToken.None);
        if (!bounded.HasMore || System.Text.Encoding.UTF8.GetByteCount(RelayJson.Serialize(bounded)) > 420 * 1024) throw new Exception("Unicode inbox page exceeded the protocol budget.");
        Console.WriteLine("PASS: non-platform adapter runs actual neutral application, exact text, scoped result/inbox/query");

        // Exercise authentication through the actual production pipe host, without an endpoint process.
        var id = Guid.NewGuid(); var connections = new ConnectionStore(data);
        await connections.CreateAsync(id, owner, [project], false, CancellationToken.None);
        await work.DisposeAsync(); // Release the local work service before an independently owned test host starts.
        var errors = new List<Exception>();
        await using (var host = RelayRuntime.Start(data, errors.Add))
        {
            var client = new RelayPipeClient(data, id);
            try { await client.RequestAsync(new("show_window"), CancellationToken.None); throw new Exception("Participant activated the administrative window."); }
            catch (RelayException error) when (error.Code == "capability_denied") { }
            var visible = (await client.RequestAsync(new("project", ProjectAction: new("configuration")), CancellationToken.None)).Project!.Configuration!;
            if (visible.Projects.Length != 1 || visible.Projects[0].Id != project || visible.Endpoints.Any(endpoint => endpoint.BindingId != "protected")) throw new Exception("Scoped discovery exposed unrelated context/binding.");
            try { await client.RequestAsync(new("configuration"), CancellationToken.None); throw new Exception("Unscoped profile API bypassed project identity."); }
            catch (RelayException error) when (error.Code == "legacy_unassigned") { }
            try { await client.RequestAsync(new("configure_projects", ProjectsConfiguration: configuration), CancellationToken.None); throw new Exception("Agent changed central authority."); }
            catch (RelayException error) when (error.Code == "legacy_unassigned") { }
            try { await client.RequestAsync(new("project", ProjectAction: new("get_result", RequestId: foreign.Request.Id)), CancellationToken.None); throw new Exception("Foreign result bypassed the gateway."); }
            catch (RelayException error) when (error.Code == "capability_denied") { }
            try { await client.RequestAsync(new("project", ProjectAction: new("observe_operations", Query: new(project))), CancellationToken.None); throw new Exception("Agent entered administrator observation API."); }
            catch (RelayException error) when (error.Code == "invalid_operation") { }
            var administrator = new RelayPipeClient(data);
            var observed = (await administrator.RequestAsync(new("project", ProjectAction: new("observe_operations", Query: new(project, Limit: 1))), CancellationToken.None)).Project!.Operations!;
            if (observed.Items.Count != 1 || observed.Items.Any(item => item.Request.Scope!.ProjectId != project)) throw new Exception("Administrator observation is unbounded or unscoped.");
            var exactObserved = (await administrator.RequestAsync(new("project", ProjectAction: new("observe_operation", ProjectId: project, RequestId: request)), CancellationToken.None)).Project!.Operation!;
            if (exactObserved.Request.Text != exact) throw new Exception("Exact observation lost message content.");
            try { await administrator.RequestAsync(new("project", ProjectAction: new("observe_operation", ProjectId: project, RequestId: foreign.Request.Id)), CancellationToken.None); throw new Exception("Exact observation crossed project boundary."); }
            catch (RelayException error) when (error.Code == "not_found") { }
            var monitor = new AgentRelay.Windows.Monitoring.PipeMonitorSource(data);
            var observation = new AgentRelay.Windows.Monitoring.ProjectObservation(project);
            await monitor.LoadAsync(observation, false, CancellationToken.None);
            if (!observation.MoreMessages) throw new Exception("Unicode observation did not retain a bounded older-page cursor.");
            await monitor.LoadAsync(observation, true, CancellationToken.None);
            var tailCursor = observation.MessageCursor; var loadedCount = observation.Messages.Count;
            await monitor.LoadAsync(observation, false, CancellationToken.None);
            if (observation.MessageCursor != tailCursor || observation.Messages.Count != loadedCount || observation.Messages.Select(item => item.Id).Distinct().Count() != loadedCount)
                throw new Exception("Head refresh lost a byte-bounded older history cursor or duplicated messages.");
            var original = observation.Operations.Single(item => item.Request.Id == request);
            observation.Operations[observation.Operations.IndexOf(original)] = original with { State = "running" };
            for (var burst = 0; burst < 55; burst++)
                await store.SaveAsync(exactObserved with { Request = exactObserved.Request with { Id = Guid.NewGuid(), TaskId = Guid.NewGuid() } }, CancellationToken.None);
            await monitor.LoadAsync(observation, false, CancellationToken.None);
            if (!observation.MoreOperations || observation.OperationCursor is null || observation.Operations.Single(item => item.Request.Id == request).State != "completed")
                throw new Exception("A nonoverlapping head burst hid a history gap or an older operation completion.");
            await connections.RevokeAsync(id, CancellationToken.None);
            try { await client.RequestAsync(new("project", ProjectAction: new("configuration")), CancellationToken.None); throw new Exception("Revocation bypassed active pipe client."); }
            catch (RelayException error) when (error.Code == "authentication_required") { }
        }
        if (errors.Count != 0) throw new Exception("Unexpected gateway diagnostic: " + errors[0].Message);
        Console.WriteLine("PASS: production pipe bound identity, project-only discovery/read, admin refusal and live revocation");
    }

    private sealed class MemoryAdapter(SqliteWorkStore store) : IRelaySessions, IWorkExecutor
    {
        private readonly Dictionary<Guid, SessionSnapshot> sessions = new();
        public Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token)
        { var session = new SessionSnapshot(definition.Id, definition.Name, definition.Scope, "ready", "memory-conversation"); sessions.Add(session.Id, session); return Task.FromResult(session); }
        public Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => Task.FromResult(sessions[id]);
        public Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token) => GetAsync(id, token);
        public Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid owner, CancellationToken token) => Task.FromResult(new ReadPage<SessionSnapshot>(sessions.Values.Where(session => session.Scope.ProjectId == query.ProjectId && session.Scope.SenderId == owner).ToArray(), null, false));
        public Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token) => GetAsync(id, token);
        public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token) => Task.FromResult<IReadOnlyList<string>>([]);
        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        {
            var scope = request.Scope!;
            await store.AddInboxAsync(new(Guid.NewGuid(), request.ProfileId, scope.RecipientId.ToString("D"), scope.SenderId.ToString("D"), request.Text, request.Id,
                DateTimeOffset.UtcNow, request.SessionId, scope.ProjectId, scope.RecipientId, scope.SenderId), token);
            return new("memory", request.Text);
        }
    }
}
