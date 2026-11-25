using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record RelaySession(Guid Id, string Name, string ProfileId, WslProfile Template, string Root,
    string State, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string? Diagnostic = null, string? WindowsWorkspace = null,
    SessionDefinition? Definition = null, Guid? RetiredForCancellationId = null);
public sealed record SessionDetail(RelaySession Session, AgentStatus? Agent = null, bool WorkActive = false);

public sealed class SessionService
{
    private readonly string dataDirectory;
    private readonly SettingsStore settings;
    private readonly ISessionRuntime agents;
    private readonly WslBridge bridge;
    private readonly SqliteDocuments<RelaySession> store;
    private readonly SqliteDocuments<SessionRuntimeObservation> observations;
    private readonly SemaphoreSlim observationGate = new(1, 1);
    private static readonly RuntimeStamp Host = new(Guid.NewGuid().ToString("N"), AgentRelay.Protocol.McpProcessIdentity.StartedAt);
    private readonly SemaphoreSlim creation = new(1, 1);

    public SessionService(string dataDirectory, SettingsStore settings, ISessionRuntime agents, WslBridge bridge)
        : this(new RelayDatabase(dataDirectory), settings, agents, bridge) { }

    internal SessionService(RelayDatabase database, SettingsStore settings, ISessionRuntime agents, WslBridge bridge)
    {
        dataDirectory = database.DataDirectory; this.settings = settings; this.agents = agents; this.bridge = bridge;
        store = new(database, "sessions");
        observations = new(database, "session_runtime");
    }

    internal async Task<bool> VerifyDormantRetainedRootForArchiveAsync(RelaySession session, CancellationToken token)
    {
        var previous = session.Template;
        var current = settings.Load().Profiles.SingleOrDefault(profile => profile.Id == session.ProfileId);
        if (current is null || current.Root == previous.Root || current.Client != previous.Client ||
            current.Distribution != previous.Distribution || current.User != previous.User ||
            session.Definition is null || !NativeLayout.IsSession(session.Root, session.Id))
            return false;

        string ReadHelper(string name)
        {
            using var resource = typeof(SessionService).Assembly.GetManifestResourceStream("AgentRelay.Infrastructure." + name)
                ?? throw new InvalidDataException("Missing packaged verification helper.");
            using var reader = new StreamReader(resource);
            return reader.ReadToEnd();
        }
        var request = RelayJson.Serialize(new
        {
            action = "verify-dormant",
            session = new { session.Id, session.Root, template = new { previous.User } },
            previous.Client, session.Definition.Scope,
            layoutSource = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ReadHelper("native_layout.py"))),
            layoutDefinition = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(ReadHelper("native-layout.json")))
        });
        var proof = await WslMaintenance.RunAsync(new(previous.Distribution, previous.User,
            ReadHelper("session_adapter_files.py"), request, 5), token);
        if (proof.ExitCode != 0)
            throw new RelayException("archive_unverified", "The retained native terminal could not be proven stopped: " + proof.Error, false);
        var response = RelayJson.Deserialize<System.Text.Json.JsonElement>(proof.Output);
        if (response.GetProperty("sessionId").GetGuid() != session.Id || !response.GetProperty("dormant").GetBoolean())
            throw new RelayException("archive_unverified", "The retained native terminal returned a mismatched proof.", false);
        return true;
    }

    public Task<IReadOnlyList<RelaySession>> ListAsync(CancellationToken token) => store.ListAsync(token);
    public async Task<RelaySession> FindAsync(Guid id, CancellationToken token) =>
        await store.FindAsync(id, token) ?? throw new RelayException("not_found", "Unknown relay session.");

    /// <seealso cref="ControlAsync"/>
    public async Task<RelaySession> CreateAsync(Guid id, string name, string profileId, CancellationToken token, SessionDefinition? definition = null)
    {
        name = SessionNames.Normalize(name);
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
            record ??= new(id, name.Trim(), profileId, template, RuntimePaths.NativeSession(id), "preparing", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
                WindowsWorkspace: RuntimePaths.SessionWorkspace(dataDirectory, id));
            record = record with { WindowsWorkspace = record.WindowsWorkspace ?? RuntimePaths.SessionWorkspace(dataDirectory, id), Definition = definition ?? record.Definition };
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
        if (!allowClosed) DemandResumable(session);
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
        return new(session, await agents.StatusAsync(await ResolveAsync(id, session.ProfileId, token, allowClosed: true), token), active);
    }

    public async Task<SessionRuntimeObservation> ObserveAsync(Guid id, AgentStatus status, CancellationToken token)
    {
        await observationGate.WaitAsync(token);
        try
        {
            var previous = await observations.FindAsync(id, token);
            var observed = new SessionRuntimeObservation(Host, status.DistributionRuntime, status.NativeRuntime, DateTimeOffset.UtcNow, "unknown");
            // Unavailable telemetry must not erase the last confirmed identities used to classify a later observation.
            if (status.NativePresent is null || status.DistributionRuntime is null || status.NativePresent == true && status.NativeRuntime is null)
                return observed;
            var cause = previous switch
            {
                null => "initial_observation",
                _ when previous.Distribution?.Generation != status.DistributionRuntime.Generation => "wsl_restart",
                _ when previous.Native is not null && status.NativePresent == false => "native_exit",
                _ when previous.Native?.Generation != observed.Native?.Generation => "native_started_or_replaced",
                _ when previous.Host.Generation != observed.Host.Generation => "host_restart",
                _ => previous.Cause
            };
            var result = observed with { Cause = cause };
            if (previous is null || previous.Host != result.Host || previous.Distribution != result.Distribution || previous.Native != result.Native)
                await observations.SaveAsync(id, result, token);
            return result;
        }
        finally { observationGate.Release(); }
    }

    public async Task<SessionDetail> StartAsync(Guid id, CancellationToken token)
    {
        var session = await FindAsync(id, token);
        var agent = await StartNativeAsync(session, await ResolveAsync(id, session.ProfileId, token), token);
        return new(session, agent);
    }

    /// <summary>Observe readiness and restore a verified existing conversation, without issuing work.</summary>
    /// <seealso cref="StartAsync"/>
    internal async Task<AgentStatus> EnsureReadyAsync(RelaySession session, WslProfile profile, bool mayRestore, CancellationToken token)
    {
        var status = await agents.StatusAsync(profile, token);
        if (status.State == "not_started" && mayRestore && status.NativeConversationId is not null && status.DistributionRuntime is not null)
        {
            status = await StartNativeAsync(session, profile, token, requirePinnedConversation: true);
            var readinessDeadline = System.Diagnostics.Stopwatch.StartNew();
            while (status.State == "starting" && readinessDeadline.Elapsed < TimeSpan.FromSeconds(5))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), token);
                status = await agents.StatusAsync(profile, token);
            }
        }
        await ObserveAsync(session.Id, status, token);
        if (status.State != "ready")
            throw new RelayException(status.State == "awaiting_approval" ? "session_awaiting_approval" : "session_not_ready",
                $"Native session is {status.State}. {status.Message} No work was dispatched.", false);
        return status;
    }

    public async Task<SessionDetail> ReopenAsync(Guid id, CancellationToken token, bool requirePinnedConversation = false)
    {
        var session = await FindAsync(id, token);
        DemandResumable(session);
        var profile = await ResolveAsync(id, session.ProfileId, token, allowClosed: true);
        session = session with { State = "created", UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(id, session, token);
        return new(session, await StartNativeAsync(session, profile, token, requirePinnedConversation));
    }

    private async Task<AgentStatus> StartNativeAsync(RelaySession session, WslProfile profile, CancellationToken token, bool requirePinnedConversation = false)
    {
        if (session.Definition is { Scope: var scope })
        {
            var participant = new ProjectSettingsStore(dataDirectory).Load().Memberships.SingleOrDefault(
                item => item.ProjectId == scope.ProjectId && item.EndpointId == scope.RecipientId);
            if (participant?.NativeConnectionId is { } binding)
            {
                await new ConnectionStore(dataDirectory).DemandParticipantBindingAsync(binding, scope.RecipientId, scope.ProjectId, token);
                if (!File.Exists(RuntimePaths.TransportEntryScript(dataDirectory)))
                    throw new RelayException("adapter_unavailable", "Prepare the scoped MCP transport for this installation before enabling native participant access.", false);
                profile = profile with { ParticipantMcp = McpTransportInstallation.Launch(dataDirectory, binding, wslStdio: true) };
            }
        }
        return await agents.StartAsync(profile, token, resume: true, requirePinnedConversation: requirePinnedConversation);
    }

    private static void DemandResumable(RelaySession session)
    {
        if (session.RetiredForCancellationId is not null)
            throw new RelayException("session_retired_for_cancellation", "This native conversation was retired to prevent an abandoned approval from resuming. Use a new session for new work.", false);
    }

    internal async Task<SessionTerminal> ReadTerminalAsync(RelaySession session, CancellationToken token)
    {
        var profile = await ResolveAsync(session.Id, session.ProfileId, token);
        var result = await bridge.CallAsync(profile, "read_terminal", new { SessionId = session.Id }, token);
        return result.Deserialize<SessionTerminal>(RelayJson.Options)
            ?? throw new RelayException("terminal_unavailable", "The session adapter returned no terminal snapshot.", false);
    }

    internal async Task<TerminalSignalReceipt> SignalTerminalAsync(RelaySession session, string action, string? approvalRevision, CancellationToken token)
    {
        var profile = await ResolveAsync(session.Id, session.ProfileId, token);
        var result = await bridge.CallAsync(profile, "signal_terminal", new { SessionId = session.Id, Action = action, ApprovalRevision = approvalRevision }, token);
        var receipt = result.Deserialize<TerminalSignalReceipt>(RelayJson.Options);
        return receipt is { KeySent: true } && receipt.Action == action ? receipt :
            throw new RelayException("terminal_signal_unconfirmed", "The adapter did not confirm sending the key. Read the current terminal.", true);
    }

    internal async Task<bool> VerifyDormantApprovalAsync(RelaySession session, WorkRequest request, CancellationToken token)
    {
        if (session.RetiredForCancellationId is not null || session.Definition is not { } definition ||
            !ProjectPolicy.IsSessionRequest(request, definition)) return false;
        var profile = await ResolveAsync(session.Id, session.ProfileId, token, allowClosed: true);
        try
        {
            var proof = await bridge.CallAsync(profile, "verify_dormant_approval", new { requestId = request.Id, definition.Scope }, token);
            return proof.TryGetProperty("dormant", out var dormant) && dormant.ValueKind == JsonValueKind.True;
        }
        catch (RelayException error) when (error.Code == "cancellation_unconfirmed") { return false; }
    }

    internal async Task<bool> CloseApprovalSessionAsync(RelaySession session, WorkRequest request, CancellationToken token)
    {
        if (request.Kind != "message" || session.Definition is not { } definition || !ProjectPolicy.IsSessionRequest(request, definition) ||
            session.RetiredForCancellationId is { } other && other != request.Id)
            throw new RelayException("cancellation_unconfirmed", "The retained request does not identify this native approval.", false);
        var profile = await ResolveAsync(session.Id, session.ProfileId, token, allowClosed: true);
        var proofRequest = new { requestId = request.Id, definition.Scope };
        bool stopped;
        try
        {
            var prepared = await bridge.CallAsync(profile, "prepare_approval_stop", proofRequest, token);
            DemandApprovalProof(prepared, request.Id, "prepared");
            // Persist the fence before sending any signal. After interruption, closing the same request can resume the proof.
            if (session.RetiredForCancellationId is null)
            {
                session = session with { RetiredForCancellationId = request.Id, UpdatedAt = DateTimeOffset.UtcNow };
                await store.SaveAsync(session.Id, session, token);
            }
            var proof = await bridge.CallAsync(profile, "confirm_approval_stop", proofRequest, token);
            stopped = proof.TryGetProperty("stopped", out var stopResult) && stopResult.ValueKind == System.Text.Json.JsonValueKind.True;
            DemandApprovalProof(proof, request.Id, stopped ? "stopped" : "terminalClosed");
        }
        catch (RelayException error) when (error.Code == "approval_already_resolved" && session.RetiredForCancellationId == request.Id)
        {
            // The native adapter only returns this before a stop could have been sent.
            await store.SaveAsync(session.Id, session with { RetiredForCancellationId = null, UpdatedAt = DateTimeOffset.UtcNow }, token);
            throw;
        }
        if (session.State != "closed")
            await store.SaveAsync(session.Id, session with { State = "closed", UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        return stopped;
    }

    private static void DemandApprovalProof(System.Text.Json.JsonElement proof, Guid requestId, string stage)
    {
        if (!proof.TryGetProperty("requestId", out var id) || !id.TryGetGuid(out var confirmed) || confirmed != requestId ||
            !proof.TryGetProperty(stage, out var observed) || observed.ValueKind != System.Text.Json.JsonValueKind.True)
            throw new RelayException("cancellation_unconfirmed", "The native runtime did not confirm the requested cancellation phase.", false);
    }

    public async Task<RelaySession> RenameAsync(Guid id, string name, CancellationToken token)
    {
        name = SessionNames.Normalize(name); var session = await FindAsync(id, token);
        session = session with { Name = name.Trim(), UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(id, session, token); return session;
    }

    /// <seealso cref="CreateAsync"/>
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
