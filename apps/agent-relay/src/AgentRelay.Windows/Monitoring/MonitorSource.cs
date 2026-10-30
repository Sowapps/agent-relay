using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows.Monitoring;

internal sealed class ProjectObservation(Guid id, Guid? sessionId = null)
{
    public Guid ProjectId { get; } = id;
    public Guid? SessionId { get; } = sessionId;
    public bool Loaded { get; set; }
    public List<WorkRecord> Operations { get; } = [];
    public List<InboxMessage> Messages { get; } = [];
    public List<SessionSnapshot> Sessions { get; } = [];
    public long? OperationCursor { get; set; }
    public long? MessageCursor { get; set; }
    public long? SessionCursor { get; set; }
    public bool MoreOperations { get; set; }
    public bool MoreMessages { get; set; }
    public bool MoreSessions { get; set; }
    public bool OlderOperationsLoaded { get; set; }
    public bool OlderMessagesLoaded { get; set; }
    public bool OlderSessionsLoaded { get; set; }
    public string? Error { get; set; }
    public long Revision { get; set; }
}

internal interface IMonitorSource
{
    Task<ProjectConfiguration> ConfigurationAsync(CancellationToken token);
    Task LoadAsync(ProjectObservation project, bool older, CancellationToken token);
    Task<WorkRecord> OperationAsync(Guid projectId, Guid id, CancellationToken token);
    IReadOnlyList<EndpointDescriptor> Describe(ProjectConfiguration configuration);
}

internal sealed class PipeMonitorSource(string data) : IMonitorSource
{
    private readonly RelayPipeClient client = new(data);
    private async Task<ProjectReply> Read(ProjectAction action, CancellationToken token) =>
        (await client.RequestAsync(new("project", ProjectAction: action), token)).Project ?? throw new InvalidDataException("Missing project observation.");
    public async Task<ProjectConfiguration> ConfigurationAsync(CancellationToken token) =>
        (await Read(new("configuration"), token)).Configuration!;
    public IReadOnlyList<EndpointDescriptor> Describe(ProjectConfiguration configuration) => EndpointDescriptions.Describe(configuration, new SettingsStore(data).Load());
    public async Task LoadAsync(ProjectObservation project, bool older, CancellationToken token)
    {
        if (!older || project.MoreOperations)
        {
            var watching = older ? [] : project.Operations.Where(item => item.State is "pending" or "running").Select(item => item.Request.Id).ToArray();
            var page = (await Read(new("observe_operations", Query: new(project.ProjectId, SessionId: project.SessionId, Cursor: older ? project.OperationCursor : null, Limit: 50)), token)).Operations!;
            var overlaps = Merge(project.Operations, page.Items, item => item.Request.Id, out var changed); if (changed) project.Revision++;
            if (older || !project.OlderOperationsLoaded || !overlaps) { project.OperationCursor = page.NextCursor; project.MoreOperations = page.HasMore; }
            project.OlderOperationsLoaded |= older;
            // Row cursors describe creation order. A long-running older operation can
            // complete outside the newest page; keep watching it through passive reads.
            foreach (var id in watching.Where(id => !page.Items.Any(item => item.Request.Id == id)))
            {
                var current = await OperationAsync(project.ProjectId, id, token);
                Merge(project.Operations, [current], item => item.Request.Id, out var watchedChanged); if (watchedChanged) project.Revision++;
            }
        }
        if (!older || project.MoreMessages)
        {
            // Observation is newest-first; it never advances native inbox-consumption cursors.
            var page = (await Read(new("observe_messages", Query: new(project.ProjectId, SessionId: project.SessionId, Cursor: older ? project.MessageCursor : null, Limit: 50)), token)).Messages!;
            var overlaps = Merge(project.Messages, page.Items, item => item.Id, out var changed); if (changed) project.Revision++;
            if (older || !project.OlderMessagesLoaded || !overlaps) { project.MessageCursor = page.NextCursor; project.MoreMessages = page.HasMore; }
            project.OlderMessagesLoaded |= older;
        }
        if (project.SessionId is null && (!older || project.MoreSessions))
        {
            var page = (await Read(new("observe_sessions", Query: new(project.ProjectId, Cursor: older ? project.SessionCursor : null, Limit: 50)), token)).Sessions!;
            var overlaps = Merge(project.Sessions, page.Items, item => item.Id, out var changed); if (changed) project.Revision++;
            if (older || !project.OlderSessionsLoaded || !overlaps) { project.SessionCursor = page.NextCursor; project.MoreSessions = page.HasMore; }
            project.OlderSessionsLoaded |= older;
        }
        project.Error = null; project.Loaded = true;
    }
    public async Task<WorkRecord> OperationAsync(Guid projectId, Guid id, CancellationToken token)
    {
        // Exact retained read, not get_result (which can reconcile uncertain native execution).
        var reply = await Read(new("observe_operation", ProjectId: projectId, RequestId: id), token);
        return reply.Operation!;
    }
    internal static bool Merge<T>(List<T> retained, IEnumerable<T> incoming, Func<T, Guid> key, out bool changed)
    {
        var overlaps = false; changed = false;
        foreach (var item in incoming)
        {
            var index = retained.FindIndex(existing => key(existing) == key(item));
            if (index < 0) { retained.Add(item); changed = true; }
            else { overlaps = true; if (retained[index] is WorkRecord existing && item is WorkRecord incomingWork && existing.UpdatedAt > incomingWork.UpdatedAt) continue; if (RelayJson.Serialize(retained[index]) != RelayJson.Serialize(item)) { retained[index] = item; changed = true; } }
        }
        return overlaps;
    }
}
