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
        token.ThrowIfCancellationRequested();
        using var admission = Admission.Enter(projectId);
        using var snapshot = policy.Pin();
        if (caller is not null) policy.Demand(caller, projectId, RelayCapabilities.ManageProject);
        var project = policy.Configuration().Projects.SingleOrDefault(project => project.Id == projectId)
            ?? throw new RelayException("not_found", "Unknown project.");
        var writer = projectStates ?? throw new RelayException("adapter_unavailable", "Project state persistence is unavailable.");
        if (enabled)
        {
            if (project.Enabled && !project.ArchiveRequested) return project;
            if (project.Enabled != expectedEnabled) throw new RelayException("state_conflict", "Project state changed. Refresh before deciding.");
            return writer.SetLifecycle(project, true, false, null); // Also cancels a pending archive; never replays work.
        }
        if (!project.Enabled || project.ArchiveRequested) return project;
        if (project.Enabled != expectedEnabled) throw new RelayException("state_conflict", "Project state changed. Refresh before deciding.");
        // Persist intent before checking work: the request outlives the caller, UI and host process.
        return writer.SetLifecycle(project, true, true, null);
    }

    public async Task ReconcileArchivesAsync(CancellationToken token)
    {
        if (projectStates is null) return;
        foreach (var project in projectStates.Load().Projects.Where(project => project.Enabled && project.ArchiveRequested))
        {
            token.ThrowIfCancellationRequested();
            try { await CompleteArchiveAsync(project.Id, token); }
            catch (RelayException error) when (error.Code is "busy" or "state_conflict") { /* Another admission/configuration won; inspect the next fresh snapshot. */ }
        }
    }

    private async Task CompleteArchiveAsync(Guid projectId, CancellationToken stopping)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        using var admission = Admission.Enter(projectId);
        using var snapshot = policy.Pin();
        var project = policy.Configuration().Projects.SingleOrDefault(project => project.Id == projectId);
        if (project is not { Enabled: true, ArchiveRequested: true }) return;
        var writer = projectStates!;
        try
        {
            if (work.IsProjectActive(projectId)) throw new RelayException("archive_waiting", "Waiting for existing project work to finish.");
            long? cursor = null;
            do
            {
                var page = await journal.OperationsAsync(new(projectId, Cursor: cursor, Limit: 100), token);
                if (page.Warnings?.Count > 0 || page.Items.Any(item => item.Request.Scope?.ProjectId != projectId)) throw new RelayException("archive_unverified", "Project work verification is incomplete.");
                var blocking = page.Items.FirstOrDefault(item => item.State is not ("completed" or "failed"));
                if (blocking is not null) throw new RelayException(blocking.State is "running" or "pending" ? "archive_waiting" : "archive_unverified",
                    "Waiting for operation " + blocking.Request.Id + " (" + blocking.State + "). No work is retried or approved automatically.");
                cursor = Next(page.HasMore, page.NextCursor, cursor);
            } while (cursor is not null);
            do
            {
                var page = await sessions.AllAsync(new(projectId, Cursor: cursor, Limit: 100), token);
                if (page.Warnings?.Count > 0 || page.Items.Any(item => item.Scope.ProjectId != projectId)) throw new RelayException("archive_unverified", "Project session verification is incomplete.");
                foreach (var retained in page.Items)
                {
                    if (retained.WorkActive || work.IsSessionActive(retained.Id)) throw new RelayException("archive_waiting", "Waiting for existing session work to finish.");
                    var observed = await sessions.InspectAsync(retained.Id, token);
                    if (observed.Scope.ProjectId != projectId) throw new RelayException("archive_unverified", "Session scope could not be confirmed.");
                    if (observed.WorkActive) throw new RelayException("archive_waiting", "Waiting for existing native session work to finish.");
                    if (observed.State is not ("closed" or "not_started" or "ready"))
                        throw new RelayException("archive_unverified", "Resolve the session state before archival: " + retained.Name + " (" + observed.State + "). No session is closed automatically.");
                }
                cursor = Next(page.HasMore, page.NextCursor, cursor);
            } while (cursor is not null);
            if (work.IsProjectActive(projectId)) throw new RelayException("archive_waiting", "Project work is still finishing.");
            token.ThrowIfCancellationRequested();
            writer.SetLifecycle(project, false, false, null);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            var code = error is RelayException relay ? relay.Code : "archive_unverified";
            var diagnostic = code + ": " + (error is OperationCanceledException ? "Verification timed out; archival remains pending." : error.Message);
            writer.SetLifecycle(project, true, true, diagnostic[..Math.Min(diagnostic.Length, 800)]);
        }
    }
    private static long? Next(bool more, long? next, long? previous)
    {
        if (!more) return null;
        if (next is null || next <= 0 || previous is not null && next >= previous) throw new RelayException("archive_unverified", "Incomplete lifecycle verification; no state was changed.");
        return next;
    }
}
