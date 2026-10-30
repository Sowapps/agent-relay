using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record WorkSummary(Guid Id, string ProfileId, string Kind, string State, string Preview, DateTimeOffset CreatedAt,
    string? Label = null, string? Context = null, Guid? SessionId = null);

public sealed class MvpCoordinator : IAsyncDisposable
{
    public SettingsStore Settings { get; }
    public WorkService Work { get; }
    private readonly WslBridge bridge = new();
    private readonly SqliteWorkStore store;
    private readonly WslWorkExecutor executor;
    private readonly AgentControl agents;
    private readonly SessionService sessions;
    private readonly FileTransferService files;
    private readonly SemaphoreSlim changes = new(1, 1);
    private readonly RelayDatabase database;

    public MvpCoordinator(string dataDirectory)
    {
        Settings = new(dataDirectory);
        SettingsStore.Validate(Settings.Load());
        database = new(dataDirectory);
        store = new(database);
        store.RecoverAsync(CancellationToken.None).GetAwaiter().GetResult();
        agents = new(Settings, bridge);
        sessions = new(dataDirectory, Settings, agents, bridge);
        executor = new(Settings, bridge, store, ResolveProfileAsync);
        files = new(sessions, bridge, database);
        Work = new(store, new RelayWorkExecutor(executor, files), () => Settings.Load().Profiles.Select(profile =>
            new WorkCapability(profile.Id, profile.Client == "command", profile.Client != "command", profile.Client != "command")).ToArray(), Settings.Load().MaximumConcurrency);
    }

    public async Task<PipeResponse> DispatchAsync(PipeRequest request, CancellationToken token)
    {
        var mutation = request.Operation is "submit" or "configure" or "start_profile" or "open_session" or
            "session_start" or "session_reopen" or "session_rename" or "session_close" or "session_interrupt" or "session_send" or "session_transfer";
        if (mutation && !await changes.WaitAsync(0, token)) throw new RelayException("busy", "A configuration, startup or submission change is already in progress.");
        try
        {
        switch (request.Operation)
        {
            case "submit" when request.Work is not null:
                return new(Work: await SubmitAsync(request.Work, token));
            case "result" when request.RequestId is { } id:
                var retainedWork = await Work.ReadAsync(id, token);
                if (retainedWork is { State: "unknown", Request.Kind: "transfer" } && await files.ReceiptAsync(retainedWork.Request, token) is { } fileResult)
                {
                    retainedWork = retainedWork with { State = "completed", Result = fileResult, Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow };
                    await store.SaveAsync(retainedWork, token);
                }
                return new(Work: retainedWork);
            case "history":
                var profiles = Settings.Load().Profiles.ToDictionary(profile => profile.Id);
                var namedSessions = (await sessions.ListAsync(token)).ToDictionary(session => session.Id);
                return new(History: (await Work.ListAsync(token)).Select(item => Summary(item, profiles, namedSessions)).ToArray());
            case "query_results":
                var page = await JournalReader.ReadAsync<WorkRecord>(database, "work", request.Query ?? new(), token);
                var currentProfiles = Settings.Load().Profiles.ToDictionary(profile => profile.Id);
                var currentSessions = (await sessions.ListAsync(token)).ToDictionary(session => session.Id);
                return new(ResultsPage: new(page.Items.Select(item => Summary(item, currentProfiles, currentSessions)).ToArray(), page.NextCursor, page.HasMore));
            case "query_inbox": return new(InboxPage: await JournalReader.ReadAsync<InboxMessage>(database, "inbox", request.Query ?? new(), token, incremental: true));
            case "query_sessions": return new(SessionsPage: await JournalReader.ReadAsync<RelaySession>(database, "sessions", request.Query ?? new(), token));
            case "configuration": return new(Configuration: Settings.Load(), Profiles: Settings.Load().Profiles.Select(AgentControl.Context).ToArray());
            case "agent_status" when request.ProfileId is not null: return new(Agent: await agents.StatusAsync(request.ProfileId, token));
            case "open_session" when request.ProfileId is not null && request.Name is not null:
                var opened = await sessions.CreateAsync(request.SessionId ?? Guid.NewGuid(), request.Name, request.ProfileId, token);
                return new(Session: opened.State == "created" ? await sessions.StartAsync(opened.Id, token) : new(opened));
            case "list_sessions": return new(Sessions: (await sessions.ListAsync(token)).Select(session => new SessionDetail(session, WorkActive: Work.IsSessionActive(session.Id))).ToArray());
            case "session_status" when request.SessionId is { } sessionId: return new(Session: await sessions.StatusAsync(sessionId, Work.IsSessionActive(sessionId), token));
            case "session_start" when request.SessionId is { } startId: return new(Session: await sessions.StartAsync(startId, token));
            case "session_reopen" when request.SessionId is { } reopenId: return new(Session: await sessions.ReopenAsync(reopenId, token));
            case "session_rename" when request.SessionId is { } renameId && request.Name is not null: return new(Session: new(await sessions.RenameAsync(renameId, request.Name, token)));
            case "session_close" or "session_interrupt" when request.SessionId is { } controlId:
                if (request.Operation == "session_close" && Work.IsSessionActive(controlId)) throw new RelayException("busy", "Interrupt the session and inspect the pending operation before closing it.");
                var controlled = await sessions.ControlAsync(controlId, request.Operation == "session_close", token);
                if (request.Operation == "session_interrupt") await Work.InterruptSessionAsync(controlId, token);
                return new(Session: new(controlled, WorkActive: Work.IsSessionActive(controlId)));
            case "session_send" when request.SessionId is { } sendId && request.Message is not null:
                var destination = await sessions.FindAsync(sendId, token);
                var task = new WorkRequest(request.RequestId ?? Guid.NewGuid(), destination.ProfileId, "message", request.Message.Body, SessionId: sendId);
                return new(Work: await SubmitAsync(task, token));
            case "session_transfer" when request.SessionId is { } transferId && request.Work is { Kind: "transfer" }:
                var fileSession = await sessions.FindAsync(transferId, token);
                return new(Work: await SubmitAsync(request.Work with { ProfileId = fileSession.ProfileId, SessionId = transferId }, token));
            case "configure" when request.Configuration is not null:
                if (Work.IsActive) throw new RelayException("busy", "Wait until the active operation finishes before changing permissions/configuration.");
                Settings.Save(request.Configuration); return new(Configuration: Settings.Load());
            case "inbox":
                return new(Inbox: (await store.InboxAsync(token)).Take(50).ToArray());
            case "refresh_inbox":
                var warnings = new List<string>();
                foreach (var profile in Settings.Load().Profiles.Where(profile => request.Query?.SessionId is null &&
                    (request.Query?.ProfileId is null || profile.Id == request.Query.ProfileId)))
                {
                    try
                    {
                        if (profile.Client != "command")
                        {
                            var response = await bridge.CallAsync(profile, "inbox", new { }, token);
                            await executor.ImportMessagesAsync(profile, response, token);
                        }
                        await executor.RecoverAsync(profile, token);
                    }
                    catch (Exception error) when (error is not OperationCanceledException) { warnings.Add(profile.Name + ": " + error.Message); }
                }
                foreach (var session in (await sessions.ListAsync(token)).Where(session => session.State is not ("preparing" or "setup_failed") &&
                    (request.Query?.SessionId is null || session.Id == request.Query.SessionId) &&
                    (request.Query?.ProfileId is null || session.ProfileId == request.Query.ProfileId)))
                {
                    try
                    {
                        var profile = await sessions.ResolveAsync(session.Id, session.ProfileId, token, allowClosed: true);
                        await executor.ImportMessagesAsync(profile, await bridge.CallAsync(profile, "inbox", new { }, token), token, session.Id);
                        await executor.RecoverAsync(profile, token, session.Id);
                    }
                    catch (Exception error) when (error is not OperationCanceledException) { warnings.Add(session.Name + ": " + error.Message); }
                }
                return request.Query is null ? new(Inbox: (await store.InboxAsync(token)).Take(50).ToArray(), Warnings: warnings) :
                    new(InboxPage: await JournalReader.ReadAsync<InboxMessage>(database, "inbox", request.Query, token, incremental: true), Warnings: warnings);
            case "start_profile" when request.ProfileId is not null:
                return new(Agent: await agents.StartAsync(request.ProfileId, token));
            default: throw new RelayException("invalid_operation", "Unsupported MVP operation.");
        }
        }
        finally { if (mutation) changes.Release(); }
    }

    public async ValueTask DisposeAsync()
    { try { await Work.DisposeAsync(); } finally { changes.Dispose(); } }

    private async Task<WslProfile> ResolveProfileAsync(WorkRequest request, CancellationToken token) => request.SessionId is { } id
        ? await sessions.ResolveAsync(id, request.ProfileId, token)
        : Settings.Load().Profiles.SingleOrDefault(profile => profile.Id == request.ProfileId)
            ?? throw new RelayException("not_found", "The configured profile does not exist.");

    private async Task<WorkRecord> SubmitAsync(WorkRequest request, CancellationToken token)
    {
        var profile = await ResolveProfileAsync(request, token);
        var rule = profile.Commands.SingleOrDefault(command => command.Id == request.CommandId);
        var label = request.Kind == "transfer" ? request.Transfer!.Direction + " · " + request.Transfer.DestinationPath :
            request.Kind == "command" ? rule?.Name ?? request.CommandId ?? "Commande WSL" : "Message à " + profile.Name;
        return await Work.SubmitAsync(request, token, new(label, AgentControl.Context(profile).Workspace));
    }

    private static WorkSummary Summary(WorkRecord work, IReadOnlyDictionary<string, WslProfile> profiles, IReadOnlyDictionary<Guid, RelaySession> sessions)
    {
        profiles.TryGetValue(work.Request.ProfileId, out var profile);
        var session = work.Request.SessionId is { } sessionId ? sessions.GetValueOrDefault(sessionId) : null;
        var command = profile?.Commands.SingleOrDefault(rule => rule.Id == work.Request.CommandId);
        var label = work.Request.Kind == "command" ? command?.Name ?? work.Request.CommandId ?? "Commande WSL" : "Message à " + (session?.Name ?? profile?.Name ?? work.Request.ProfileId);
        var context = profile is null ? null : session is null ? AgentControl.Context(profile).Workspace : session.Root + "/" + session.Template.Client + "/workspace";
        var preview = work.Request.Kind == "command" ? label + " · " + context : work.Request.Text[..Math.Min(140, work.Request.Text.Length)];
        return new(work.Request.Id, work.Request.ProfileId, work.Request.Kind, work.State, preview, work.CreatedAt,
            work.Metadata?.Label ?? label, work.Metadata?.Context ?? context, work.Request.SessionId);
    }
}
