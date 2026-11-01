using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class ProjectSettingsStore(string dataDirectory) : IProjectStateStore
{
    private readonly string path = Path.Combine(dataDirectory, "relay-projects.json");
    public ProjectConfiguration Load() => File.Exists(path) ? RelayJson.Deserialize<ProjectConfiguration>(File.ReadAllText(path)) : new(1, [], [], [], []);
    public void Save(ProjectConfiguration configuration)
    {
        ProjectPolicy.Validate(configuration);
        using var ownership = WriteLease();
        var previous = Load();
        if (previous.Projects.Any(project => !configuration.Projects.Any(item => item.Id == project.Id && item.Enabled == project.Enabled && item.ArchiveRequested == project.ArchiveRequested && item.ArchiveDiagnostic == project.ArchiveDiagnostic)))
            throw new RelayException("project_lifecycle_required", "Preserve existing projects and use the guarded lifecycle action to archive/reactivate them.");
        SaveCore(configuration);
    }
    public RelayProject SetEnabled(Guid projectId, bool enabled, bool expectedEnabled)
    {
        var project = Load().Projects.Single(item => item.Id == projectId);
        if (project.Enabled != expectedEnabled) throw new RelayException("state_conflict", "Project state changed.");
        return SetLifecycle(project, enabled, false, null);
    }
    public RelayProject SetLifecycle(RelayProject expected, bool enabled, bool archiveRequested, string? diagnostic)
    {
        using var ownership = WriteLease();
        var configuration = Load(); ProjectPolicy.Validate(configuration);
        var project = configuration.Projects.SingleOrDefault(item => item.Id == expected.Id) ?? throw new RelayException("not_found", "Unknown project.");
        if (project.Enabled != expected.Enabled || project.ArchiveRequested != expected.ArchiveRequested)
            throw new RelayException("state_conflict", "Project state changed; no update was written.");
        var updated = project with { Enabled = enabled, ArchiveRequested = archiveRequested, ArchiveDiagnostic = diagnostic };
        if (updated != project) SaveCore(configuration with { Projects = configuration.Projects.Select(item => item.Id == project.Id ? updated : item).ToArray() });
        return updated;
    }
    private FileStream WriteLease()
    {
        Directory.CreateDirectory(dataDirectory);
        try { return new FileStream(Path.Combine(dataDirectory, "projects-write.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new RelayException("busy", "Another project configuration write is in progress: " + error.Message); }
    }
    private void SaveCore(ProjectConfiguration configuration)
    {
        if (File.Exists(path)) File.Copy(path, path + ".before-" + DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ"));
        var temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { var bytes = System.Text.Encoding.UTF8.GetBytes(RelayJson.Serialize(configuration)); output.Write(bytes); output.Flush(flushToDisk: true); }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
