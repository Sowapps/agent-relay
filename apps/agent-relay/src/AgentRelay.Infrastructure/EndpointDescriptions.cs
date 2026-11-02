using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Installed adapter metadata belongs at composition, never in monitoring view branches.
public static class EndpointDescriptions
{
    // Structured display facts are a presentation projection, not a new MCP schema or a readiness probe.
    public static IReadOnlyDictionary<Guid, IReadOnlyList<KeyValuePair<string,string>>> DisplayFacts(ProjectConfiguration configuration, RelaySettings settings) =>
        configuration.Endpoints.ToDictionary(endpoint=>endpoint.Id, endpoint=>
        {
            var profile=settings.Profiles.SingleOrDefault(item=>item.Id==endpoint.BindingId);
            IReadOnlyList<KeyValuePair<string,string>> facts=endpoint.AdapterId=="local-files"
                ? [new("Hôte","Poste local")]
                : endpoint.AdapterId is "wsl-agent" or "wsl-command" && profile is not null
                ? [new("Distribution",profile.Distribution),new("Utilisateur",profile.User)]
                : [new("Contexte",endpoint.Name)];
            return facts;
        });

    public static IReadOnlyList<EndpointDescriptor> Describe(ProjectConfiguration configuration, RelaySettings settings) =>
        configuration.Endpoints.Select(endpoint =>
        {
            var profile = settings.Profiles.SingleOrDefault(item => item.Id == endpoint.BindingId);
            if (endpoint.AdapterId == "local-files") return new EndpointDescriptor(endpoint.Id, "Client MCP externe", "Local", configuration.Workspaces.FirstOrDefault(item => item.EndpointId == endpoint.Id)?.Location, []);
            return endpoint.AdapterId is "wsl-agent" or "wsl-command" && profile is not null
                ? new EndpointDescriptor(endpoint.Id, profile.Client, "WSL", profile.Distribution + " · " + profile.User,
                    endpoint.AdapterId == "wsl-agent" ? ["session.manage", "session.inspect", "message.send", "host.inspect"] : ["command.execute", "host.inspect"])
                : new EndpointDescriptor(endpoint.Id, endpoint.AdapterId, "Configuration de l’adaptateur", endpoint.Name);
        }).ToArray();
}
