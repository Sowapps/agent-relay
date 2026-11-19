using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

internal enum FileWorkspaceKind { Host, Wsl }

/// <summary>Resolve the installed filesystem combination before using the common transfer and publication lifecycle.</summary>
public sealed class ScopedFileRoutes
{
    private readonly RelayDatabase database;
    private readonly SettingsStore technical;
    private readonly ProjectSettingsStore projects;
    private readonly WslBridge bridge;
    private readonly FileTransferService files;
    private readonly IReadOnlyDictionary<string, FileWorkspaceKind> adapters;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> preparation = new();

    internal ScopedFileRoutes(RelayDatabase database, SettingsStore technical, ProjectSettingsStore projects, WslBridge bridge,
        FileTransferService files, IReadOnlyDictionary<string, FileWorkspaceKind> adapters)
    {
        this.database = database;
        this.technical = technical;
        this.projects = projects;
        this.bridge = bridge;
        this.files = files;
        this.adapters = adapters;
    }

    private sealed record Route(TransferRequest Transfer, WslProfile PublishingProfile, string HostWorkspace,
        RelayWorkspace NativeWorkspace, WslProfile? NativeSource = null, RelayWorkspace? SourceWorkspace = null);

    private ProjectConfiguration Demand(WorkRequest request, RelaySession session)
    {
        if (request is not { Artifact: { } artifact, Scope: { } scope } || session.Definition is not { } definition ||
            !ProjectPolicy.IsSessionRequest(request, definition))
            throw new RelayException("invalid_work", "A transfer must identify the retained session and its workspace.", false);
        var caller = new CallerIdentity(scope.SenderId, [scope.ProjectId]);
        var policy = new ProjectPolicy(projects);
        using var snapshot = policy.Pin();
        policy.DemandSession(caller, new(session.Id, session.Name, definition.Scope, session.State), RelayCapabilities.ReadHistory);
        policy.Transfer(caller, scope.ProjectId, artifact);
        return policy.Configuration();
    }

    internal async Task<IPreparedWork> PrepareAsync(WorkRequest request, RelaySession session, WslProfile profile, CancellationToken token)
    {
        var route = await ResolveAsync(request, session, profile, prepare: true, token);
        return new ScopedPreparedWork(async dispatchToken =>
        {
            Demand(request, session);
            await WorkspaceIdentity.VerifyNativeAsync(database, route.PublishingProfile, route.NativeWorkspace, dispatchToken);
            if (route.NativeSource is { } source && route.SourceWorkspace is { } workspace)
            {
                await WorkspaceIdentity.VerifyNativeAsync(database, source, workspace, dispatchToken);
                var receipt = await bridge.CallAsync(source, "transfer_inspect", new { request.Id, Direction = "download",
                    LinuxPath = route.Transfer.SourcePath, Overwrite = false, FileTransferService.MaximumBytes }, dispatchToken);
                var path = FileTransferService.WorkspacePath(route.HostWorkspace, route.Transfer.SourcePath);
                if (receipt.GetProperty("workspace").GetString() != workspace.Location ||
                    receipt.GetProperty("path").GetString() is not { } inspected ||
                    !string.Equals(FileTransferService.WslPath(source.Distribution, inspected), path, StringComparison.Ordinal))
                    throw new RelayException("workspace_identity_changed", "The source transfer path did not match the verified WSL workspace.", false);
            }
            return await files.ExecuteAsync(request with { Transfer = route.Transfer }, dispatchToken, route.PublishingProfile,
                route.HostWorkspace, uploadAuthorized: true);
        });
    }

    internal async Task<TransferPartial> PartialAsync(WorkRequest request, RelaySession session, WslProfile profile, bool remove, CancellationToken token)
    {
        var route = await ResolveAsync(request, session, profile, prepare: false, token);
        await WorkspaceIdentity.VerifyNativeAsync(database, route.PublishingProfile, route.NativeWorkspace, token);
        return await files.PartialAsync(request, route.Transfer.Direction, route.Transfer.DestinationPath,
            route.HostWorkspace, route.PublishingProfile, remove, token);
    }

    private async Task<Route> ResolveAsync(WorkRequest request, RelaySession session, WslProfile profile, bool prepare, CancellationToken token)
    {
        var configuration = Demand(request, session);
        var artifact = request.Artifact!;
        var definition = session.Definition!;
        var source = configuration.Workspaces.Single(item => item.Id == artifact.Source.WorkspaceId);
        var target = configuration.Workspaces.Single(item => item.Id == artifact.Destination.WorkspaceId);
        var fromSession = source.Id == definition.Workspace.Id;
        if (!fromSession && target.Id != definition.Workspace.Id)
            throw new RelayException("capability_denied", "The transfer must include the workspace assigned to this session.", false);
        var other = fromSession ? target : source;
        var endpoint = configuration.Endpoints.Single(item => item.Id == other.EndpointId);
        if (!adapters.TryGetValue(endpoint.AdapterId, out var adapter))
            throw new RelayException("adapter_unavailable", "This endpoint does not have an installed file adapter.", false);
        if (adapter == FileWorkspaceKind.Host)
        {
            FileTransferService.DemandLocalWorkspace(other.Location, scoped: true);
            return new(new(fromSession ? "download" : "upload", artifact.Source.RelativePath, artifact.Destination.RelativePath, artifact.Replace),
                profile, other.Location, definition.Workspace);
        }

        var otherProfile = await FilesystemProfileAsync(definition.Scope.ProjectId, endpoint, other, profile, prepare, token);
        var sourceProfile = fromSession ? profile : otherProfile;
        var targetProfile = fromSession ? otherProfile : profile;
        return new(new("upload", artifact.Source.RelativePath, artifact.Destination.RelativePath, artifact.Replace),
            targetProfile, FileTransferService.WslPath(sourceProfile.Distribution, source.Location), target, sourceProfile, source);
    }

    private async Task<WslProfile> FilesystemProfileAsync(Guid projectId, RelayEndpoint endpoint, RelayWorkspace workspace,
        WslProfile known, bool prepare, CancellationToken token)
    {
        var binding = technical.Load().Profiles.SingleOrDefault(item => item.Id == endpoint.BindingId);
        if (binding is null || !string.Equals(binding.Distribution, known.Distribution, StringComparison.OrdinalIgnoreCase) || binding.User != known.User)
            throw new RelayException("adapter_unavailable", "The installed native file route requires both workspaces in the same WSL distribution and user context.", false);
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"file-runtime:{projectId:D}:{endpoint.Id:D}:{workspace.Id:D}")).AsSpan(0, 16));
        var template = binding with { Client = "command", NativeExecutable = null, Workspace = workspace.Location };
        if (!prepare) return template with { Root = RuntimePaths.NativeSession(id) };
        await WorkspaceIdentity.VerifyNativeAsync(database, template, workspace, token);
        var definition = new SessionDefinition(id, workspace.Name, new(projectId, endpoint.Id, endpoint.Id, workspace.Id), endpoint, workspace, false);
        var gate = preparation.GetOrAdd(id, _ => new(1, 1));
        await gate.WaitAsync(token);
        try
        {
            var provisioned = await WslProvisioner.PrepareSessionAsync(database.DataDirectory, id, template, token, definition);
            return template with { Root = provisioned.Root };
        }
        finally { gate.Release(); }
    }

}
