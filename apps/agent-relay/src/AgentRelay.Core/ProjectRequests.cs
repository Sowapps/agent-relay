namespace AgentRelay.Core;

public sealed partial class RelayApplication
{
    private void DemandRequestRead(CallerIdentity caller, Guid project)
    {
        if (!policy.CanManageRequests(caller, project)) policy.Demand(caller, project, RelayCapabilities.ReadHistory);
    }

    public async Task<WorkRecord> ChangeRequestAsync(CallerIdentity caller, Guid id, RequestChange change, CancellationToken token)
    {
        var record = await work.ReadAsync(id, token);
        var scope = record.Request.Scope ?? throw new RelayException("legacy_unassigned", "The request has no retained project.", false);
        using var snapshot = policy.Pin();
        policy.DemandRequestManagement(caller, scope.ProjectId);
        if (change.ExpectedRevision < 0 || change.ExpectedRevision == long.MaxValue ||
            change.Action is not ("edit" or "disable" or "delete" or "restore") ||
            change.Label?.Length > 200 || change.Note?.Length > 2000 || change.Status?.Length > 100)
            throw new RelayException("invalid_request", "Expected a current revision, a supported action and bounded text.", false);
        // Administrative recovery must remain available independently of native approval and worker readiness.
        return await work.ChangeRequestAsync(id, caller.EndpointId, change, token);
    }

    public async Task<WorkRecord> RescheduleRequestAsync(CallerIdentity caller, Guid originalId, Guid newId,
        Guid? sessionId, string? text, CancellationToken token)
    {
        if (originalId == newId || newId == Guid.Empty)
            throw new RelayException("invalid_request", "Rescheduling requires a distinct request ID.", false);
        var original = (await work.ReadAsync(originalId, token)).Request;
        var scope = original.Scope ?? throw new RelayException("legacy_unassigned", "The request has no retained project.", false);
        using var snapshot = policy.Pin();
        policy.DemandRequestManagement(caller, scope.ProjectId);
        var chosenSession = sessionId ?? original.SessionId;
        if (chosenSession is { } id && (await sessions.GetAsync(id, token)).Scope.ProjectId != scope.ProjectId)
            throw new RelayException("capability_denied", "The replacement session belongs to another project.", false);
        var task = original.TaskId ?? original.Id;
        // The caller is the new sender. Normal execution checks still apply to each chosen destination.
        return original.Kind switch
        {
            "message" when chosenSession is { } target => await MessageAsync(caller, target, newId, text ?? original.Text, task, token, originalId),
            "command" when sessionId is null && text is null && scope.WorkspaceId is { } workspace && original.CommandId is { } command =>
                await CommandAsync(caller, scope.ProjectId, scope.RecipientId, workspace, command, newId, token, originalId, task),
            "transfer" when text is null && chosenSession is { } target && original.Artifact is { } transfer =>
                await TransferAsync(caller, target, transfer, newId, token, originalId, task),
            _ => throw new RelayException("invalid_request", "Provide the retained request's supported reschedule parameters.", false)
        };
    }
}
