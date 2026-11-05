using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Owns retained scope and revocation checks shared across native capabilities.
public sealed class ScopedSessionContext(ProjectSettingsStore projects, SessionService sessions)
{
    public async Task<RelaySession> RecordAsync(Guid id, CancellationToken token)
    {
        var record = await sessions.FindAsync(id, token);
        if (record.Definition is null) throw new RelayException("legacy_unassigned", "The legacy session has no scoped project mapping.");
        return record;
    }
    public void ValidateDefinition(SessionDefinition definition)
    {
        var value = projects.Load();
        ProjectPolicy.Validate(value);
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
            var policy = new ProjectPolicy(projects);
            var caller = new CallerIdentity(definition.Scope.SenderId, [definition.Scope.ProjectId]);
            policy.LocalWork(caller, definition.Scope.ProjectId, definition.Workspace.Id, definition.Endpoint.Id);
        }
    }
}
