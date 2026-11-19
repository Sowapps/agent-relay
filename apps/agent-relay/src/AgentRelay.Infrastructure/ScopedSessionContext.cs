using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Owns retained scope and revocation checks shared across native capabilities.
public sealed class ScopedSessionContext(ProjectSettingsStore projects, SessionService sessions)
{
    internal SessionMandate[] Mandates(Guid projectId, Guid participant, string capability) =>
        projects.Load().Memberships.SingleOrDefault(member => member.ProjectId == projectId && member.EndpointId == participant)
            ?.SessionMandates?.Where(item => item.Capabilities.Contains(capability, StringComparer.Ordinal)).ToArray() ?? [];

    internal static bool SameWorkspace(RelayWorkspace left, RelayWorkspace right) =>
        left.Id == right.Id && left.EndpointId == right.EndpointId && left.Location == right.Location;

    public async Task<RelaySession> RecordAsync(Guid id, CancellationToken token)
    {
        var record = await sessions.FindAsync(id, token);
        if (record.Definition is null) throw new RelayException("legacy_unassigned", "The legacy session has no scoped project mapping.");
        return record;
    }
    public void ValidateDefinition(SessionDefinition definition)
    {
        var policy = new ProjectPolicy(projects);
        using var lease = policy.Pin();
        var value = policy.Configuration();
        var endpoint = value.Endpoints.SingleOrDefault(item => item.Id == definition.Endpoint.Id);
        var workspace = value.Workspaces.SingleOrDefault(item => item.Id == definition.Workspace.Id);
        if (endpoint is null || workspace is null || endpoint.AdapterId != definition.Endpoint.AdapterId || endpoint.BindingId != definition.Endpoint.BindingId ||
            !SameWorkspace(workspace, definition.Workspace))
            throw new RelayException("capability_denied", "Endpoint or workspace context changed; create a new session instead of silently moving an existing one.");
        if (definition.AllowLocalWork)
        {
            var caller = new CallerIdentity(definition.Scope.SenderId, [definition.Scope.ProjectId]);
            policy.RetainedLocalWork(caller, definition.Scope.ProjectId, definition.Workspace.Id, definition.Endpoint.Id);
        }
    }
}
