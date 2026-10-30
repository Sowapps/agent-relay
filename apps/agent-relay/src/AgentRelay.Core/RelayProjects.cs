namespace AgentRelay.Core;

public static class RelayCapabilities
{
    public const string SendMessage = "message.send", ReceiveMessage = "message.receive", ReadHistory = "history.read",
        ManageSession = "session.manage", RunCommand = "command.execute", InspectHost = "host.inspect",
        ReadFile = "file.read", WriteFile = "file.write", ReplaceFile = "file.replace", LocalWork = "agent.local-work";
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    { SendMessage, ReceiveMessage, ReadHistory, ManageSession, RunCommand, InspectHost, ReadFile, WriteFile, ReplaceFile, LocalWork };
}

public sealed record RelayProject(Guid Id, string Name, bool Enabled = true);
public sealed record RelayEndpoint(Guid Id, string Name, string AdapterId, string BindingId);
public sealed record RelayWorkspace(Guid Id, Guid EndpointId, string Name, string Location, bool ConcurrentWrites = false);
public sealed record WorkspaceAccess(Guid WorkspaceId, bool Read, bool Write = false, bool Replace = false);
public sealed record ProjectMembership(Guid ProjectId, Guid EndpointId, string Role, string[] Capabilities,
    WorkspaceAccess[] Workspaces, string[]? CommandIds = null);
public sealed record ProjectConfiguration(int Version, RelayProject[] Projects, RelayEndpoint[] Endpoints,
    RelayWorkspace[] Workspaces, ProjectMembership[] Memberships);
public sealed record CallerIdentity(Guid EndpointId, Guid[] ProjectIds);
public sealed record OperationScope(Guid ProjectId, Guid SenderId, Guid RecipientId, Guid? WorkspaceId = null, long PolicyRevision = 0);
public sealed record ArtifactLocation(Guid EndpointId, Guid WorkspaceId, string RelativePath);
public sealed record ArtifactTransfer(ArtifactLocation Source, ArtifactLocation Destination, bool Replace = false);

public interface IProjectConfigurationStore
{
    ProjectConfiguration Load();
}

public sealed class ProjectPolicy(IProjectConfigurationStore configuration)
{
    private readonly AsyncLocal<ProjectConfiguration?> snapshot = new();
    public IDisposable Pin()
    {
        var previous = snapshot.Value;
        if (previous is null) { var value = configuration.Load(); Validate(value); snapshot.Value = value; }
        return new SnapshotLease(() => snapshot.Value = previous);
    }
    private sealed class SnapshotLease(Action release) : IDisposable { public void Dispose() => release(); }
    public static void Validate(ProjectConfiguration value)
    {
        if (value.Version != 1 || value.Projects is null || value.Endpoints is null || value.Workspaces is null || value.Memberships is null)
            throw new RelayException("invalid_configuration", "Unsupported project configuration.");
        static void Ids(IEnumerable<Guid> ids)
        {
            var values = ids.ToArray();
            if (values.Any(id => id == Guid.Empty) || values.Distinct().Count() != values.Length)
                throw new RelayException("invalid_configuration", "Identities must be nonempty and unique.");
        }
        static bool Name(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 200 && !text.Any(char.IsControl);
        Ids(value.Projects.Select(item => item.Id)); Ids(value.Endpoints.Select(item => item.Id)); Ids(value.Workspaces.Select(item => item.Id));
        if (value.Projects.Any(item => !Name(item.Name)) || value.Endpoints.Any(item => !Name(item.Name) || !Name(item.AdapterId) || !Name(item.BindingId)) ||
            value.Workspaces.Any(item => !Name(item.Name) || string.IsNullOrWhiteSpace(item.Location) || !value.Endpoints.Any(endpoint => endpoint.Id == item.EndpointId)) ||
            value.Memberships.Select(item => (item.ProjectId, item.EndpointId)).Distinct().Count() != value.Memberships.Length)
            throw new RelayException("invalid_configuration", "Project, endpoint, workspace or membership definition is invalid.");
        foreach (var member in value.Memberships)
        {
            if (!value.Projects.Any(project => project.Id == member.ProjectId) || !value.Endpoints.Any(endpoint => endpoint.Id == member.EndpointId) ||
                member.Role is not ("main" or "secondary" or "reviewer" or "observer") || member.Capabilities is null || member.Workspaces is null ||
                member.Capabilities.Any(capability => !RelayCapabilities.All.Contains(capability)) ||
                member.Capabilities.Distinct().Count() != member.Capabilities.Length || member.Workspaces.Select(item => item.WorkspaceId).Distinct().Count() != member.Workspaces.Length ||
                member.Workspaces.Any(access => !value.Workspaces.Any(workspace => workspace.Id == access.WorkspaceId) || (access.Replace && !access.Write)))
                throw new RelayException("invalid_configuration", "Membership references, role or explicit capabilities are invalid.");
        }
    }

    public ProjectConfiguration Configuration()
    {
        if (snapshot.Value is { } pinned) return pinned;
        var value = configuration.Load(); Validate(value); return value;
    }

    public ProjectMembership Demand(CallerIdentity caller, Guid projectId, string capability)
    {
        var value = Configuration();
        if (!caller.ProjectIds.Contains(projectId) || !value.Projects.Any(project => project.Id == projectId && project.Enabled)) Denied();
        var member = value.Memberships.SingleOrDefault(item => item.ProjectId == projectId && item.EndpointId == caller.EndpointId);
        if (member is null || !member.Capabilities.Contains(capability, StringComparer.Ordinal)) Denied();
        return member!;
    }

    public OperationScope Message(CallerIdentity caller, Guid projectId, Guid recipientId, Guid? workspaceId = null)
    {
        Demand(caller, projectId, RelayCapabilities.SendMessage);
        var receiver = Configuration().Memberships.SingleOrDefault(item => item.ProjectId == projectId && item.EndpointId == recipientId);
        if (receiver is null || !receiver.Capabilities.Contains(RelayCapabilities.ReceiveMessage, StringComparer.Ordinal)) Denied();
        if (workspaceId is { } id) Workspace(caller, projectId, id, write: false, recipientId);
        return new(projectId, caller.EndpointId, recipientId, workspaceId);
    }

    public RelayWorkspace Workspace(CallerIdentity caller, Guid projectId, Guid workspaceId, bool write, Guid? owner = null, bool replace = false)
    {
        var member = Demand(caller, projectId, write ? replace ? RelayCapabilities.ReplaceFile : RelayCapabilities.WriteFile : RelayCapabilities.ReadFile);
        var access = member.Workspaces.SingleOrDefault(item => item.WorkspaceId == workspaceId);
        var workspace = Configuration().Workspaces.SingleOrDefault(item => item.Id == workspaceId);
        if (access is null || workspace is null || !access.Read || (write && !access.Write) || (replace && !access.Replace) || (owner is { } endpoint && workspace.EndpointId != endpoint)) Denied();
        if (!Configuration().Memberships.Any(item => item.ProjectId == projectId && item.EndpointId == workspace!.EndpointId)) Denied();
        return workspace!;
    }

    public void Transfer(CallerIdentity caller, Guid projectId, ArtifactTransfer transfer)
    {
        Workspace(caller, projectId, transfer.Source.WorkspaceId, false, transfer.Source.EndpointId);
        Workspace(caller, projectId, transfer.Destination.WorkspaceId, true, transfer.Destination.EndpointId, transfer.Replace);
        Relative(transfer.Source.RelativePath); Relative(transfer.Destination.RelativePath);
    }

    public void LocalWork(CallerIdentity caller, Guid projectId, Guid workspaceId, Guid recipientId)
    {
        var sender = Demand(caller, projectId, RelayCapabilities.LocalWork);
        var value = Configuration(); var workspace = value.Workspaces.SingleOrDefault(item => item.Id == workspaceId && item.EndpointId == recipientId);
        var receiver = value.Memberships.SingleOrDefault(item => item.ProjectId == projectId && item.EndpointId == recipientId);
        bool Writes(ProjectMembership member) => member.Workspaces.Any(access => access.WorkspaceId == workspaceId && access.Read && access.Write);
        if (workspace is null || receiver is null || !receiver.Capabilities.Contains(RelayCapabilities.LocalWork) || !Writes(sender) || !Writes(receiver)) Denied();
    }

    public static void Relative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 500 || path.Any(char.IsControl) || path.StartsWith('/') || path.StartsWith('\\') || path.Contains(':') ||
            path.Split('/', '\\').Any(part => part.Length == 0 || part is "." or ".."))
            throw new RelayException("invalid_path", "Expected a nonempty relative artifact path without traversal.");
    }

    private static void Denied() => throw new RelayException("capability_denied", "This connection is not authorized for the selected project, participant or capability.");
}
