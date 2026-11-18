using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Owns scoped execution and recovery orchestration; concrete dependencies come from ProjectCoordinator.
public sealed class ScopedWorkExecutor(RelayDatabase database, ProjectSettingsStore projects, SessionService sessions, WslBridge bridge,
    SqliteWorkStore store, FileTransferService files, ScopedInbox inbox, ScopedCommands commands, ScopedSessionContext context)
    : IWorkExecutor, IWorkPreparation, IOperationRecovery, ITransferMaintenance
{
    public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
    {
        using var prepared = await PrepareAsync(request, token);
        return await prepared.ExecuteAsync(token);
    }

    public async Task<IPreparedWork> PrepareAsync(WorkRequest request, CancellationToken token)
    {
        var scope = request.Scope ?? throw new RelayException("legacy_unassigned", "Production work needs a project scope.");
        var policy = new ProjectPolicy(projects);
        var caller = new CallerIdentity(scope.SenderId, [scope.ProjectId]);
        if (request.Kind == "command") return await commands.PrepareAsync(caller, request, token);
        var record = await context.RecordAsync(request.SessionId!.Value, token);
        if (record.Definition!.Scope != scope) throw new RelayException("capability_denied", "Session and operation scopes differ.");
        context.ValidateDefinition(record.Definition);
        var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token);
        var identity = await WorkspaceIdentity.VerifyNativeAsync(database, profile, record.Definition.Workspace, token);
        if (request.Kind == "transfer")
        {
            var artifact = request.Artifact ?? throw new RelayException("invalid_work", "A scoped artifact transfer is required.");
            ProjectConfiguration configuration;
            using (policy.Pin())
            {
                policy.Transfer(caller, scope.ProjectId, artifact);
                configuration = policy.Configuration();
            }
            var source = configuration.Workspaces.Single(item => item.Id == artifact.Source.WorkspaceId);
            var destination = configuration.Workspaces.Single(item => item.Id == artifact.Destination.WorkspaceId);
            var fromRemote = source.EndpointId == scope.RecipientId;
            var local = fromRemote ? destination : source;
            var remote = fromRemote ? source : destination;
            if (remote.Id != record.Definition.Workspace.Id || configuration.Endpoints.Single(item => item.Id == local.EndpointId).AdapterId != "local-files")
                throw new RelayException("adapter_unavailable", "This adapter supports its assigned native workspace and a configured local-files endpoint.");
            FileTransferService.DemandLocalWorkspace(local.Location, scoped: true);
            var transfer = new TransferRequest(fromRemote ? "download" : "upload", artifact.Source.RelativePath, artifact.Destination.RelativePath, artifact.Replace);
            return new ScopedPreparedWork(async dispatchToken =>
            {
                await WorkspaceIdentity.VerifyNativeAsync(database, profile, record.Definition.Workspace, dispatchToken);
                return await files.ExecuteAsync(request with { Transfer = transfer }, dispatchToken, profile, local.Location, uploadAuthorized: true);
            });
        }
        policy.Message(caller, scope.ProjectId, scope.RecipientId, scope.WorkspaceId);
        var membership = policy.Demand(caller, scope.ProjectId, RelayCapabilities.SendMessage);
        var ready = await sessions.EnsureReadyAsync(record, profile, membership.Capabilities.Contains(RelayCapabilities.ManageSession), token);
        context.ValidateDefinition(record.Definition);
        return new ScopedPreparedWork(async dispatchToken =>
        {
            context.ValidateDefinition(record.Definition);
            var result = await bridge.CallAsync(profile, "message", new { request.Id, request.Text, profile.AllowLocalWork, request.Scope,
                ExpectedNativeGeneration = ready.NativeRuntime?.Generation, ExpectedDistributionGeneration = ready.DistributionRuntime?.Generation,
                ExpectedWorkspaceIdentity = identity }, dispatchToken);
            await inbox.ImportAsync(record, result, dispatchToken);
            var reply = result.GetProperty("reply");
            if (reply.GetProperty("inReplyTo").GetGuid() != request.Id) throw new InvalidDataException("Reply correlation failed.");
            return new(scope.RecipientId.ToString("D"), reply.GetProperty("text").GetString()!);
        });
    }
    public async Task<WorkRecord> ReconcileAsync(WorkRecord record, CancellationToken token)
    {
        if (record.State != "unknown" || record.Request.Scope is null) return record;
        WorkResult? receipt = null;
        if (record.Request.Kind == "transfer") receipt = await files.ReceiptAsync(record.Request, token);
        else if (record.Request.Kind == "command") receipt = await commands.RecoverAsync(record.Request, token);
        if (receipt is null) return record;
        var updated = record with { State = receipt.ExitCode is not (null or 0) ? "failed" : "completed", Result = receipt, Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow };
        await store.SaveAsync(updated, token);
        return updated;
    }
    public async Task<TransferPartial> PartialAsync(WorkRequest request, bool remove, CancellationToken token)
    {
        var record = await context.RecordAsync(request.SessionId!.Value, token);
        context.ValidateDefinition(record.Definition!);
        var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token, allowClosed: true);
        var artifact = request.Artifact!;
        var configuration = projects.Load();
        var fromRemote = artifact.Source.EndpointId == request.Scope!.RecipientId;
        var local = configuration.Workspaces.Single(item => item.Id == (fromRemote ? artifact.Destination.WorkspaceId : artifact.Source.WorkspaceId));
        var remote = fromRemote ? artifact.Source : artifact.Destination;
        if (remote.WorkspaceId != record.Definition!.Workspace.Id || configuration.Endpoints.Single(item => item.Id == local.EndpointId).AdapterId != "local-files")
            throw new RelayException("capability_denied", "The transfer adapter context is no longer available.");
        await WorkspaceIdentity.VerifyNativeAsync(database, profile, record.Definition.Workspace, token);
        return await files.PartialAsync(request, fromRemote ? "download" : "upload", artifact.Destination.RelativePath, local.Location, profile, remove, token);
    }
}

internal sealed class ScopedPreparedWork(Func<CancellationToken, Task<WorkResult>> execute) : IPreparedWork
{
    public Task<WorkResult> ExecuteAsync(CancellationToken token) => execute(token);
    public void Dispose() { }
}
