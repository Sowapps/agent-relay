namespace AgentRelay.Core;

public sealed record ProjectAccessChange(string[] Capabilities, WorkspaceAccess[] Workspaces, string[] CommandIds,
    Guid[]? MessageTargets, SessionMandate[] SessionMandates);

public sealed record ProjectAccessEntry(Guid EndpointId, string Role, string Revision, ProjectAccessChange Access);

public interface IProjectAccessStore
{
    ProjectAccessEntry ReadAccess(CallerIdentity caller, Guid projectId, Guid endpointId);
    Task<ProjectAccessEntry> SetAccessAsync(CallerIdentity caller, Guid projectId, Guid endpointId, string expectedRevision,
        ProjectAccessChange change, CancellationToken token);
}

public sealed partial class RelayApplication
{
    public ProjectAccessEntry ReadProjectAccess(CallerIdentity caller, Guid projectId, Guid endpointId) =>
        AccessStore().ReadAccess(caller, projectId, endpointId);

    public async Task<ProjectAccessEntry> SetProjectAccessAsync(CallerIdentity caller, Guid projectId, Guid endpointId, string expectedRevision,
        ProjectAccessChange change, CancellationToken token)
    {
        using var lease = Admission.Enter(projectId);
        return await AccessStore().SetAccessAsync(caller, projectId, endpointId, expectedRevision, change, token);
    }

    private IProjectAccessStore AccessStore() => projectStates as IProjectAccessStore ??
        throw new RelayException("adapter_unavailable", "Project access administration is unavailable.", false);
}
