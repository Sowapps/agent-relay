using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class ProjectSettingsStore(string dataDirectory) : IProjectConfigurationStore
{
    private readonly string path = Path.Combine(dataDirectory, "relay-projects.json");
    public ProjectConfiguration Load() => File.Exists(path) ? RelayJson.Deserialize<ProjectConfiguration>(File.ReadAllText(path)) : new(1, [], [], [], []);
    public void Save(ProjectConfiguration configuration)
    {
        ProjectPolicy.Validate(configuration); Directory.CreateDirectory(dataDirectory);
        if (File.Exists(path)) File.Copy(path, path + ".before-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
        var temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, RelayJson.Serialize(configuration)); File.Move(temporary, path, overwrite: true);
    }
}
