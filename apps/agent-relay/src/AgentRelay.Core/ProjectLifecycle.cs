namespace AgentRelay.Core;

public sealed partial class RelayApplication
{
    public Task<RelayProject> SetProjectStateAsync(CallerIdentity caller, Guid projectId, bool enabled, bool expectedEnabled, CancellationToken token)
        => ChangeProjectState(caller, projectId, enabled, expectedEnabled, token);
    // Only the authenticated local administrative boundary calls this entry point.
    public Task<RelayProject> SetProjectStateAsAdministratorAsync(Guid projectId, bool enabled, bool expectedEnabled, CancellationToken token)
        => ChangeProjectState(null, projectId, enabled, expectedEnabled, token);

    private async Task<RelayProject> ChangeProjectState(CallerIdentity? caller, Guid projectId, bool enabled, bool expectedEnabled, CancellationToken token)
    {
        using var admission = Admission.Enter(projectId);
        using var snapshot = policy.Pin();
        if (caller is not null) policy.Demand(caller, projectId, RelayCapabilities.ManageProject);
        var project = policy.Configuration().Projects.SingleOrDefault(project => project.Id == projectId)
            ?? throw new RelayException("not_found", "Unknown project.");
        if (project.Enabled == enabled) return project; // Safe retry after a lost response; no work is replayed.
        if (project.Enabled != expectedEnabled) throw new RelayException("state_conflict", "Project state changed. Refresh before deciding on a transition.");
        var writer = projectStates ?? throw new RelayException("adapter_unavailable", "Project state persistence is unavailable.");
        if (!enabled)
        {
            if (work.IsProjectActive(projectId)) throw new RelayException("archive_blocked", "Project work is still active; no state was changed.");
            long? cursor = null;
            do
            {
                var page = await journal.OperationsAsync(new(projectId, Cursor: cursor, Limit: 100), token);
                if (page.Warnings?.Count > 0 || page.Items.Any(item => item.Request.Scope?.ProjectId != projectId)) throw new RelayException("archive_unverified", "Project work verification is incomplete.");
                var blocking = page.Items.FirstOrDefault(item => item.State is not ("completed" or "failed"));
                if (blocking is not null) throw new RelayException("archive_blocked", "Archival refused: an operation is active, awaiting approval or unconfirmed. Inspect operation " + blocking.Request.Id + " before continuing.");
                cursor = Next(page.HasMore, page.NextCursor, cursor);
            } while (cursor is not null);
            do
            {
                var page = await sessions.AllAsync(new(projectId, Cursor: cursor, Limit: 100), token);
                if (page.Warnings?.Count > 0 || page.Items.Any(item => item.Scope.ProjectId != projectId)) throw new RelayException("archive_unverified", "Project session verification is incomplete.");
                foreach (var retained in page.Items)
                {
                    if (retained.WorkActive || work.IsSessionActive(retained.Id)) throw new RelayException("archive_blocked", "A project session still has active work.");
                    var observed = await sessions.InspectAsync(retained.Id, token);
                    if (observed.WorkActive || observed.State is not ("closed" or "not_started"))
                        throw new RelayException("archive_blocked", "Close the session or resolve its uncertain state before archival: " + retained.Name + ". No session is closed automatically.");
                }
                cursor = Next(page.HasMore, page.NextCursor, cursor);
            } while (cursor is not null);
            if (work.IsProjectActive(projectId)) throw new RelayException("archive_blocked", "Project work is still finishing; no state was changed.");
        }
        token.ThrowIfCancellationRequested();
        return writer.SetEnabled(projectId, enabled, expectedEnabled);
    }
    private static long? Next(bool more, long? next, long? previous)
    {
        if (!more) return null;
        if (next is null || next <= 0 || previous is not null && next >= previous) throw new RelayException("archive_unverified", "Incomplete lifecycle verification; no state was changed.");
        return next;
    }
}
