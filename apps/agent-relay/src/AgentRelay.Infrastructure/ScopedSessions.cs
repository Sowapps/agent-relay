using System.Security.Cryptography;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record NativeInboxCursor(long After, string? LastError = null);
public sealed record CommandDispatch(OperationScope Scope, WslProfile Profile, string Workspace, CommandRule Rule, string Nonce);

// WSL adapter implementing neutral application ports; platform details stay here.
public sealed class ScopedSessions : IRelaySessions, IWorkExecutor, IOperationRecovery, ITransferMaintenance
{
    private readonly string data;
    private readonly SettingsStore technical;
    private readonly ProjectSettingsStore projects;
    private readonly SessionService sessions;
    private readonly WslBridge bridge = new();
    private readonly AgentControl agents;
    private readonly SqliteWorkStore store;
    private readonly ProjectJournal journal;
    private readonly SqliteDocuments<NativeInboxCursor> cursors;
    private readonly FileTransferService files;
    private readonly SqliteDocuments<CommandDispatch> dispatches;
    private readonly Func<Guid, bool> active;

    public ScopedSessions(string data, SettingsStore technical, ProjectSettingsStore projects, SqliteWorkStore store, Func<Guid, bool> active)
    {
        this.data = data; this.technical = technical; this.projects = projects; this.store = store; this.active = active;
        agents = new(technical, bridge); sessions = new(data, technical, agents, bridge);
        var database = new RelayDatabase(data); journal = new(database); cursors = new(database, "cursors"); dispatches = new(database, "dispatches"); files = new(sessions, bridge, database, store, technical.Load().Transfers, data);
    }

    public async Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token)
    {
        if (definition.Endpoint.AdapterId != "wsl-agent") throw new RelayException("adapter_unavailable", "This endpoint has no installed native-session adapter.");
        var record = await sessions.CreateAsync(definition.Id, definition.Name, definition.Endpoint.BindingId, token, definition);
        ValidateDefinition(definition);
        new ProjectPolicy(projects).Demand(new(definition.Scope.SenderId, [definition.Scope.ProjectId]), definition.Scope.ProjectId, RelayCapabilities.ManageSession);
        if (record.Definition is null || record.Definition.Scope != definition.Scope || record.Definition.Workspace != definition.Workspace)
            throw new RelayException("id_conflict", "The session belongs to a different project, participant or workspace.");
        if (record.State == "created") await sessions.StartAsync(record.Id, token);
        return await InspectAsync(record.Id, token);
    }

    private async Task<RelaySession> Record(Guid id, CancellationToken token)
    {
        var record = await sessions.FindAsync(id, token);
        if (record.Definition is null) throw new RelayException("legacy_unassigned", "The legacy session has no scoped project mapping.");
        return record;
    }

    public async Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => Snapshot(await Record(id, token));
    public async Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token)
    {
        var record = await Record(id, token); ValidateDefinition(record.Definition!);
        if (record.State is "closed" or "preparing" or "setup_failed") return Snapshot(record);
        var agent = await agents.StatusAsync(await sessions.ResolveAsync(id, record.ProfileId, token), token);
        return Snapshot(record) with { State = agent.State, Diagnostic = agent.Message, NativeConversationId = agent.NativeConversationId };
    }

    public async Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token)
    {
        var page = await journal.SessionsAsync(query, ownerId, token);
        return new(page.Items.Select(Snapshot).ToArray(), page.NextCursor, page.HasMore);
    }

    public async Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token)
    {
        var record = await Record(id, token); ValidateDefinition(record.Definition!);
        if (action.StartsWith("rename:", StringComparison.Ordinal)) await sessions.RenameAsync(id, action[7..], token);
        else if (action == "start") await sessions.StartAsync(id, token);
        else if (action == "reopen") await sessions.ReopenAsync(id, token);
        else await sessions.ControlAsync(id, action == "close", token);
        return await InspectAsync(id, token);
    }

    private SessionSnapshot Snapshot(RelaySession record) => new(record.Id, record.Name, record.Definition!.Scope, record.State, Diagnostic: record.Diagnostic, WorkActive: active(record.Id));

    private void ValidateDefinition(SessionDefinition definition)
    {
        var value = projects.Load(); ProjectPolicy.Validate(value);
        var endpoint = value.Endpoints.SingleOrDefault(item => item.Id == definition.Endpoint.Id);
        var workspace = value.Workspaces.SingleOrDefault(item => item.Id == definition.Workspace.Id);
        if (endpoint is null || workspace is null || endpoint.AdapterId != definition.Endpoint.AdapterId || endpoint.BindingId != definition.Endpoint.BindingId ||
            workspace.EndpointId != definition.Workspace.EndpointId || workspace.Location != definition.Workspace.Location)
            throw new RelayException("capability_denied", "Endpoint or workspace context changed; create a new session instead of silently moving an existing one.");
        var member = value.Memberships.SingleOrDefault(item => item.ProjectId == definition.Scope.ProjectId && item.EndpointId == definition.Endpoint.Id);
        if (definition.AllowLocalWork && (member is null || !member.Capabilities.Contains(RelayCapabilities.LocalWork)))
            throw new RelayException("capability_denied", "The native local-work permission was revoked; do not continue a session retaining broader tools.");
        if (definition.AllowLocalWork && !member!.Workspaces.Any(access => access.WorkspaceId == definition.Workspace.Id && access.Read && access.Write))
            throw new RelayException("capability_denied", "The destination participant's native workspace grant was revoked.");
        if (definition.AllowLocalWork)
        {
            var policy = new ProjectPolicy(projects); var caller = new CallerIdentity(definition.Scope.SenderId, [definition.Scope.ProjectId]);
            policy.LocalWork(caller, definition.Scope.ProjectId, definition.Workspace.Id, definition.Endpoint.Id);
        }
    }

    public async Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token)
    {
        // Bounded refresh. Per-session cursors advance only after Windows durably imports each page.
        var refreshKey = new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("inbox-refresh:" + projectId + ":" + recipientId)).AsSpan(0, 16));
        var rotation = sessionId is null ? await cursors.FindAsync(refreshKey, token) : null;
        var page = await journal.SessionsAsync(new(projectId, SessionId: sessionId, Cursor: rotation is { After: > 0 } ? rotation.After : null, Limit: 4), recipientId, token);
        if (page.Items.Count == 0 && rotation is { After: > 0 }) page = await journal.SessionsAsync(new(projectId, SessionId: sessionId, Limit: 4), recipientId, token);
        var warnings = new List<string>();
        foreach (var record in page.Items.Where(item => item.State is not ("preparing" or "setup_failed")))
        {
            var cursor = await cursors.FindAsync(record.Id, token) ?? new(0);
            try
            {
                ValidateDefinition(record.Definition!);
                var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token, allowClosed: true);
                var response = await bridge.CallAsync(profile, "inbox", new { After = cursor.After, Limit = 50 }, token);
                await ImportAsync(record, response, token);
                if (response.TryGetProperty("nextCursor", out var next)) await cursors.SaveAsync(record.Id, new(next.GetInt64()), token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                var code = error is RelayException relay ? relay.Code : error is TimeoutException ? "timeout" : "import_unconfirmed";
                warnings.Add(record.Name + ": " + code + "; retained Windows messages remain readable.");
                await cursors.SaveAsync(record.Id, cursor with { LastError = code }, token);
            }
        }
        if (sessionId is null) await cursors.SaveAsync(refreshKey, new(page.NextCursor ?? 0), token);
        return warnings;
    }

    private async Task ImportAsync(RelaySession record, System.Text.Json.JsonElement response, CancellationToken token)
    {
        var scope = record.Definition!.Scope;
        foreach (var value in response.GetProperty("messages").EnumerateArray())
        {
            var message = RelayJson.Deserialize<InboxMessage>(value.GetRawText());
            if (message.ProjectId != scope.ProjectId || message.SenderEndpointId != scope.RecipientId || message.RecipientEndpointId != scope.SenderId || message.SessionId != record.Id)
                throw new InvalidDataException("Native message project/session/origin binding was not confirmed.");
            message = message with { ProfileId = scope.RecipientId.ToString("D") };
            if (message.InReplyTo is { } replyId)
            {
                var original = await store.FindAsync(replyId, token);
                if (original?.Request.Scope != scope || original.Request.SessionId != record.Id) throw new InvalidDataException("Native reply does not belong to the selected task/project.");
                if (original.State is "unknown" or "awaiting_approval") await store.SaveAsync(original with { State = "completed", Result = new(message.Sender, message.Text), Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow }, token);
            }
            await store.AddInboxAsync(message, token);
        }
    }

    public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
    {
        var scope = request.Scope ?? throw new RelayException("legacy_unassigned", "Production work needs a project scope.");
        var policy = new ProjectPolicy(projects); var caller = new CallerIdentity(scope.SenderId, [scope.ProjectId]);
        if (request.Kind == "command") return await CommandAsync(caller, request, token);
        var record = await Record(request.SessionId!.Value, token);
        if (record.Definition!.Scope != scope) throw new RelayException("capability_denied", "Session and operation scopes differ.");
        ValidateDefinition(record.Definition);
        var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token);
        using var nativeLease = record.Definition.Workspace.ConcurrentWrites ? null : WorkspaceLease.Acquire(WorkspaceLease.Native(profile, record.Definition.Workspace.Location));
        if (request.Kind == "transfer")
        {
            var artifact = request.Artifact ?? throw new RelayException("invalid_work", "A scoped artifact transfer is required.");
            policy.Transfer(caller, scope.ProjectId, artifact);
            var configuration = projects.Load();
            var source = configuration.Workspaces.Single(item => item.Id == artifact.Source.WorkspaceId);
            var destination = configuration.Workspaces.Single(item => item.Id == artifact.Destination.WorkspaceId);
            var fromRemote = source.EndpointId == scope.RecipientId;
            var local = fromRemote ? destination : source; var remote = fromRemote ? source : destination;
            using var localLease = local.ConcurrentWrites ? null : WorkspaceLease.Acquire(WorkspaceLease.Local(local.Location));
            if (remote.Id != record.Definition.Workspace.Id || configuration.Endpoints.Single(item => item.Id == local.EndpointId).AdapterId != "local-files")
                throw new RelayException("adapter_unavailable", "This adapter supports its assigned native workspace and a configured local-files endpoint.");
            var transfer = new TransferRequest(fromRemote ? "download" : "upload", artifact.Source.RelativePath, artifact.Destination.RelativePath, artifact.Replace);
            return await files.ExecuteAsync(request with { Transfer = transfer }, token, profile, local.Location, uploadAuthorized: true);
        }
        policy.Message(caller, scope.ProjectId, scope.RecipientId, scope.WorkspaceId);
        var result = await bridge.CallAsync(profile, "message", new { request.Id, request.Text, profile.AllowLocalWork, request.Scope }, token);
        await ImportAsync(record, result, token);
        var reply = result.GetProperty("reply");
        if (reply.GetProperty("inReplyTo").GetGuid() != request.Id) throw new InvalidDataException("Reply correlation failed.");
        return new(scope.RecipientId.ToString("D"), reply.GetProperty("text").GetString()!);
    }

    private async Task<WorkResult> CommandAsync(CallerIdentity caller, WorkRequest request, CancellationToken token)
    {
        var scope = request.Scope!; var configuration = projects.Load(); var policy = new ProjectPolicy(projects);
        var member = policy.Demand(caller, scope.ProjectId, RelayCapabilities.RunCommand);
        if (!(member.CommandIds ?? []).Contains(request.CommandId)) throw new RelayException("command_denied", "The command grant was revoked.");
        var workspace = policy.Workspace(caller, scope.ProjectId, scope.WorkspaceId!.Value, false, scope.RecipientId);
        var endpoint = configuration.Endpoints.Single(item => item.Id == scope.RecipientId);
        var binding = technical.Load().Profiles.SingleOrDefault(item => item.Id == endpoint.BindingId && item.Client == "command")
            ?? throw new RelayException("adapter_unavailable", "A configured direct-command adapter is required.");
        var command = binding.Commands.SingleOrDefault(item => item.Id == request.CommandId) ?? throw new RelayException("command_denied", "Command is not configured.");
        var key = new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("command-runtime:" + endpoint.Id + ":" + scope.ProjectId + ":" + workspace.Id + ":" + scope.SenderId)).AsSpan(0, 16));
        var definition = new SessionDefinition(key, endpoint.Name, scope, endpoint, workspace, false);
        var prepared = await WslProvisioner.PrepareSessionAsync(data, key, binding, token, definition);
        var profile = binding with { Root = prepared.Root, Workspace = workspace.Location };
        var dispatch = new CommandDispatch(scope, profile, workspace.Location, command, Guid.NewGuid().ToString("D"));
        using var lease = workspace.ConcurrentWrites ? null : WorkspaceLease.Acquire(WorkspaceLease.Native(profile, workspace.Location));
        await dispatches.SaveAsync(request.Id, dispatch, token);
        var result = await bridge.CallAsync(profile, "command", new { request.Id, request.CommandId, nonce = dispatch.Nonce, command, Workspace = workspace.Location }, token);
        return CommandResult(request, dispatch, result);
    }

    private static WorkResult CommandResult(WorkRequest request, CommandDispatch dispatch, System.Text.Json.JsonElement result)
    {
        if (result.GetProperty("requestId").GetGuid() != request.Id || result.GetProperty("nonce").GetString() != dispatch.Nonce ||
            result.GetProperty("workspace").GetString() != dispatch.Workspace || result.GetProperty("user").GetString() != dispatch.Profile.User ||
            result.GetProperty("commandId").GetString() != request.CommandId || result.GetProperty("program").GetString() != dispatch.Rule.Program ||
            !result.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()).SequenceEqual(dispatch.Rule.Arguments))
            throw new InvalidDataException("Command identity or effective execution context was not confirmed.");
        return new("endpoint-command", result.GetProperty("stdout").GetString()!, result.GetProperty("stderr").GetString()!, result.GetProperty("exitCode").GetInt32(),
            new Dictionary<string, string> { ["workspace"] = dispatch.Workspace, ["user"] = dispatch.Profile.User, ["program"] = dispatch.Rule.Program, ["arguments"] = RelayJson.Serialize(dispatch.Rule.Arguments) });
    }

    public async Task<WorkRecord> ReconcileAsync(WorkRecord record, CancellationToken token)
    {
        if (record.State != "unknown" || record.Request.Scope is null) return record;
        WorkResult? receipt = null;
        if (record.Request.Kind == "transfer") receipt = await files.ReceiptAsync(record.Request, token);
        else if (record.Request.Kind == "command" && await dispatches.FindAsync(record.Request.Id, token) is { } dispatch && dispatch.Scope == record.Request.Scope)
        {
            var response = await bridge.CallAsync(dispatch.Profile, "recover", new { record.Request.Id, nonce = dispatch.Nonce }, token);
            if (response.TryGetProperty("receipt", out var retained) && retained.ValueKind == System.Text.Json.JsonValueKind.Object)
                receipt = CommandResult(record.Request, dispatch, retained);
        }
        if (receipt is null) return record;
        var updated = record with { State = receipt.ExitCode is not (null or 0) ? "failed" : "completed", Result = receipt, Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(updated, token);
        return updated;
    }

    public async Task<TransferPartial> PartialAsync(WorkRequest request, bool remove, CancellationToken token)
    {
        var record = await Record(request.SessionId!.Value, token); ValidateDefinition(record.Definition!);
        var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token, allowClosed: true);
        var artifact = request.Artifact!; var configuration = projects.Load();
        var fromRemote = artifact.Source.EndpointId == request.Scope!.RecipientId;
        var local = configuration.Workspaces.Single(item => item.Id == (fromRemote ? artifact.Destination.WorkspaceId : artifact.Source.WorkspaceId));
        var remote = fromRemote ? artifact.Source : artifact.Destination;
        if (remote.WorkspaceId != record.Definition!.Workspace.Id || configuration.Endpoints.Single(item => item.Id == local.EndpointId).AdapterId != "local-files")
            throw new RelayException("capability_denied", "The transfer adapter context is no longer available.");
        using var nativeLease = WorkspaceLease.Acquire(WorkspaceLease.Native(profile, record.Definition.Workspace.Location));
        using var localLease = WorkspaceLease.Acquire(WorkspaceLease.Local(local.Location));
        return await files.PartialAsync(request, fromRemote ? "download" : "upload", artifact.Destination.RelativePath, local.Location, profile, remove, token);
    }
}
