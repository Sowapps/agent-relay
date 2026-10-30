using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record RelaySession(Guid Id, string Name, string ProfileId, WslProfile Template, string Root,
    string State, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Diagnostic = null, string? WindowsWorkspace = null,
    SessionDefinition? Definition = null);
public sealed record SessionDetail(RelaySession Session, AgentStatus? Agent = null, bool WorkActive = false);

public sealed class SessionService
{
    private readonly string dataDirectory;
    private readonly SettingsStore settings;
    private readonly AgentControl agents;
    private readonly WslBridge bridge;
    private readonly SqliteDocuments<RelaySession> store;
    private readonly SemaphoreSlim creation = new(1, 1);

    public SessionService(string dataDirectory, SettingsStore settings, AgentControl agents, WslBridge bridge)
    {
        this.dataDirectory = dataDirectory; this.settings = settings; this.agents = agents; this.bridge = bridge;
        store = new(new RelayDatabase(dataDirectory), "sessions");
    }

    public Task<IReadOnlyList<RelaySession>> ListAsync(CancellationToken token) => store.ListAsync(token);
    public async Task<RelaySession> FindAsync(Guid id, CancellationToken token) =>
        await store.FindAsync(id, token) ?? throw new RelayException("not_found", "Unknown relay session.");

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120 || name.Any(char.IsControl))
            throw new RelayException("invalid_session", "A session needs a readable name of at most120 characters.");
    }

    public async Task<RelaySession> CreateAsync(Guid id, string name, string profileId, CancellationToken token, SessionDefinition? definition = null)
    {
        ValidateName(name);
        name = name.Trim();
        if (id == Guid.Empty) throw new RelayException("invalid_session", "A session identifier is required.");
        await creation.WaitAsync(token);
        try
        {
            var record = await store.FindAsync(id, token);
            if (record is not null && (record.ProfileId != profileId || record.Name != name)) throw new RelayException("id_conflict", "This session identifier already has another definition.");
            if (record is not null && record.State is not ("preparing" or "setup_failed")) return record;
            var template = settings.Load().Profiles.SingleOrDefault(profile => profile.Id == profileId)
                ?? throw new RelayException("not_found", "The configured profile does not exist.");
            if (definition is not null) template = template with { AllowLocalWork = definition.AllowLocalWork, Workspace = definition.Workspace.Location };
            if (template.Client == "command") throw new RelayException("capability_denied", "Native sessions require a Codex or Claude profile.");
            record ??= new(id, name.Trim(), profileId, template, "/srv/agent-relay-session-" + id.ToString("N"), "preparing", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                WindowsWorkspace: Path.Combine(Path.GetFullPath(dataDirectory) + "-workspaces", id.ToString("N")));
            record = record with { WindowsWorkspace = record.WindowsWorkspace ?? Path.Combine(Path.GetFullPath(dataDirectory) + "-workspaces", id.ToString("N")), Definition = definition ?? record.Definition };
            Directory.CreateDirectory(record.WindowsWorkspace!);
            await store.SaveAsync(id, record, token);
            try
            {
                var prepared = await WslProvisioner.PrepareSessionAsync(dataDirectory, id, template, token, definition);
                if (prepared.Root != record.Root) throw new InvalidDataException("Session root identity changed during preparation.");
                record = record with { State = "created", UpdatedAt = DateTimeOffset.UtcNow, Diagnostic = null };
            }
            catch (Exception error) when (error is not OperationCanceledException)
            { record = record with { State = "setup_failed", Diagnostic = error.Message, UpdatedAt = DateTimeOffset.UtcNow }; }
            await store.SaveAsync(id, record, CancellationToken.None);
            return record;
        }
        finally { creation.Release(); }
    }

    public async Task<WslProfile> ResolveAsync(Guid id, string profileId, CancellationToken token, bool allowClosed = false)
    {
        var session = await FindAsync(id, token);
        if (session.ProfileId != profileId) throw new RelayException("capability_denied", "Session does not belong to this profile.");
        if ((!allowClosed && session.State == "closed") || session.State is "preparing" or "setup_failed") throw new RelayException("unavailable", "Session is closed or not prepared.");
        var template = settings.Load().Profiles.SingleOrDefault(profile => profile.Id == profileId)
            ?? throw new RelayException("capability_denied", "Session profile was removed.");
        var previous = session.Template;
        if (template.Client != previous.Client || template.Distribution != previous.Distribution || template.User != previous.User ||
            template.Root != previous.Root || (session.Definition is null && template.AllowLocalWork != previous.AllowLocalWork))
            throw new RelayException("capability_denied", "Session permissions or template context changed. Open a new session instead of silently changing the existing one.");
        return template with { Root = session.Root, Name = session.Name, Workspace = session.Definition?.Workspace.Location ?? previous.Workspace,
            AllowLocalWork = session.Definition?.AllowLocalWork ?? template.AllowLocalWork };
    }

    public async Task<SessionDetail> StatusAsync(Guid id, bool active, CancellationToken token)
    {
        var session = await FindAsync(id, token);
        if (session.State is "closed" or "preparing" or "setup_failed") return new(session, WorkActive: active);
        return new(session, await agents.StatusAsync(await ResolveAsync(id, session.ProfileId, token), token), active);
    }

    public async Task<SessionDetail> StartAsync(Guid id, CancellationToken token)
    {
        var session = await FindAsync(id, token);
        var agent = await agents.StartAsync(await ResolveAsync(id, session.ProfileId, token), token, resume: true);
        return new(session, agent);
    }

    public async Task<SessionDetail> ReopenAsync(Guid id, CancellationToken token)
    {
        var session = await FindAsync(id, token);
        var profile = await ResolveAsync(id, session.ProfileId, token, allowClosed: true);
        session = session with { State = "created", UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(id, session, token);
        return new(session, await agents.StartAsync(profile, token, resume: true));
    }

    public async Task<RelaySession> RenameAsync(Guid id, string name, CancellationToken token)
    {
        ValidateName(name); var session = await FindAsync(id, token);
        session = session with { Name = name.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(id, session, token); return session;
    }

    public async Task<RelaySession> ControlAsync(Guid id, bool close, CancellationToken token)
    {
        var session = await FindAsync(id, token);
        if (session.State == "closed") return session;
        var profile = await ResolveAsync(id, session.ProfileId, token);
        await bridge.CallAsync(profile, close ? "stop" : "interrupt", new { }, token);
        session = session with { State = close ? "closed" : "interruption_requested", UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(id, session, token); return session;
    }
}
