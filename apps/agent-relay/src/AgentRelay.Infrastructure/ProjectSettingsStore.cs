using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class ProjectSettingsStore(string dataDirectory) : IProjectStateStore, IProjectAccessStore
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

    public ProjectAccessEntry ReadAccess(CallerIdentity caller, Guid projectId, Guid endpointId)
    {
        var configuration = Load();
        DemandAccessAdministration(configuration, caller, projectId);
        return AccessEntry(SelectMembership(configuration, projectId, endpointId));
    }

    public async Task<ProjectAccessEntry> SetAccessAsync(CallerIdentity caller, Guid projectId, Guid endpointId, string expectedRevision,
        ProjectAccessChange change, CancellationToken token)
    {
        using var ownership = WriteLease();
        var configuration = Load();
        DemandAccessAdministration(configuration, caller, projectId);
        if (caller.EndpointId == endpointId)
            throw new RelayException("capability_denied", "Project access administrators cannot change their own grants.", false);
        var previous = SelectMembership(configuration, projectId, endpointId);
        var current = AccessEntry(previous);
        if (!string.Equals(current.Revision, expectedRevision, StringComparison.Ordinal))
            throw new RelayException("state_conflict", "The participant's access changed. Read the current revision before retrying.", false);
        var administrative = previous.Capabilities.Where(IsAdministrative);
        if (change.Capabilities.Any(IsAdministrative))
            throw new RelayException("capability_denied", "Administrative grants require the relay administrator.", false);
        var projectEndpoints = configuration.Memberships.Where(item => item.ProjectId == projectId).Select(item => item.EndpointId).ToHashSet();
        if (change.Workspaces.Any(access => !configuration.Workspaces.Any(workspace => workspace.Id == access.WorkspaceId && projectEndpoints.Contains(workspace.EndpointId))))
            throw new RelayException("capability_denied", "Every workspace must belong to a participant in this project.", false);
        var records = new SqliteDocuments<RelaySession>(new RelayDatabase(dataDirectory), "sessions");
        foreach (var mandate in change.SessionMandates)
        {
            var target = await records.FindAsync(mandate.SessionId, token);
            if (target?.Definition?.Scope is not { } scope || scope.ProjectId != projectId || scope.SenderId != mandate.OwnerId)
                throw new RelayException("capability_denied", "A session mandate must match an existing session and its recorded owner in this project.", false);
        }
        var updated = previous with { Capabilities = [..change.Capabilities, ..administrative], Workspaces = change.Workspaces,
            CommandIds = change.CommandIds, MessageTargets = change.MessageTargets, SessionMandates = change.SessionMandates };
        var result = AccessEntry(updated);
        if (current.Revision == result.Revision) return current;
        var next = configuration with { Memberships = configuration.Memberships.Select(item => item == previous ? updated : item).ToArray() };
        ProjectPolicy.Validate(next);
        token.ThrowIfCancellationRequested();
        SaveCore(next);
        return result;
    }

    private static bool IsAdministrative(string capability) =>
        capability is RelayCapabilities.ManageProject or RelayCapabilities.ManageProjectAccess or RelayCapabilities.ManageProjectReactions or RelayCapabilities.ManageProjectRequests;

    private static ProjectAccessEntry AccessEntry(ProjectMembership membership) => new(membership.EndpointId, membership.Role,
        AgentRelay.Protocol.McpStableProtocol.Hash(RelayJson.Serialize(membership)),
        new(membership.Capabilities.Where(item => !IsAdministrative(item)).ToArray(), membership.Workspaces,
            membership.CommandIds ?? [], membership.MessageTargets, membership.SessionMandates ?? []));

    private static ProjectMembership SelectMembership(ProjectConfiguration configuration, Guid projectId, Guid endpointId) =>
        configuration.Memberships.SingleOrDefault(item => item.ProjectId == projectId && item.EndpointId == endpointId) ??
        throw new RelayException("not_found", "The selected participant does not belong to this project.", false);

    private static void DemandAccessAdministration(ProjectConfiguration configuration, CallerIdentity caller, Guid projectId) =>
        new ProjectPolicy(new ConfigurationSnapshot(configuration)).Demand(caller, projectId, RelayCapabilities.ManageProjectAccess);

    private sealed record ConfigurationSnapshot(ProjectConfiguration Configuration) : IProjectConfigurationStore
    {
        public ProjectConfiguration Load() => Configuration;
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
