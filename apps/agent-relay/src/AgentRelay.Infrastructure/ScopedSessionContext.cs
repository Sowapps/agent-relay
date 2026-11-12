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
        var policy = new ProjectPolicy(projects);
        using var lease = policy.Pin();
        var value = policy.Configuration();
        var endpoint = value.Endpoints.SingleOrDefault(item => item.Id == definition.Endpoint.Id);
        var workspace = value.Workspaces.SingleOrDefault(item => item.Id == definition.Workspace.Id);
        if (endpoint is null || workspace is null || endpoint.AdapterId != definition.Endpoint.AdapterId || endpoint.BindingId != definition.Endpoint.BindingId ||
            workspace.EndpointId != definition.Workspace.EndpointId || workspace.Location != definition.Workspace.Location ||
            workspace.ConcurrentWrites != definition.Workspace.ConcurrentWrites)
            throw new RelayException("capability_denied", "Endpoint or workspace context changed; create a new session instead of silently moving an existing one.");
        if (definition.AllowLocalWork)
        {
            var caller = new CallerIdentity(definition.Scope.SenderId, [definition.Scope.ProjectId]);
            policy.RetainedLocalWork(caller, definition.Scope.ProjectId, definition.Workspace.Id, definition.Endpoint.Id);
        }
    }
}
