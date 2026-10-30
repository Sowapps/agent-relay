using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public static class ProjectMaintenance
{
    // Called only by the authenticated administrative host route or the exclusive offline owner.
    public static async Task<PipeResponse> ApplyAsync(string data, PipeRequest request, CancellationToken token)
    {
        var settings = new ProjectSettingsStore(data);
        if (request.Operation == "configure_projects" && request.ProjectsConfiguration is { } configuration)
        { settings.Save(configuration); return new(Project: new(Configuration: settings.Load())); }
        if (request.Operation != "connection" || request.Connection is not { } binding) throw new RelayException("invalid_request", "Incomplete administrative request.");
        var connections = new ConnectionStore(data);
        if (binding.Revoke) await connections.RevokeAsync(binding.Id, token);
        else
        {
            var value = settings.Load(); ProjectPolicy.Validate(value);
            if (binding.ProjectIds.Any(id => !value.Memberships.Any(member => member.ProjectId == id && member.EndpointId == binding.EndpointId)))
                throw new RelayException("capability_denied", "Connection scope requires an existing central project membership.");
            await connections.CreateAsync(binding.Id, binding.EndpointId, binding.ProjectIds, false, token);
        }
        return new(ConnectionId: binding.Id);
    }
}
