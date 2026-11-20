namespace AgentRelay.Core;

public sealed record TerminalApproval(string Revision, string Text);
public sealed record SessionTerminal(string Client, string Text, TerminalApproval? Approval,
    DateTimeOffset? ObservedAt = null, bool? CanDecideApproval = null, string? MissingCapability = null);
public sealed record TerminalSignalReceipt(string Action, bool KeySent);
public sealed record ApprovalProgress(DateTimeOffset? ExpiredAt, DateTimeOffset? RefusedAt, bool TurnEnded,
    DateTimeOffset? SessionClosedAt, DateTimeOffset? StopConfirmedAt)
{
    public string? NextAction => StopConfirmedAt is not null || SessionClosedAt is not null || TurnEnded ? null :
        RefusedAt is not null ? "get_result" : "read_session_terminal";
    public static ApprovalProgress? From(WorkRecord work) => work is
        { State: "awaiting_approval" or "approval_expired" } or { ApprovalWaitingSince: not null } or { TerminalRefusalSentAt: not null }
        ? new(work.ApprovalExpiredAt, work.TerminalRefusalSentAt, work.NativeTurnEnded, work.SessionClosedAt, work.CancellationConfirmedAt)
        : null;
}

public sealed partial class RelayApplication
{
    /// <summary>Read the current viewport of the owned session for review or targeted terminal input.</summary>
    /// <seealso cref="SignalSessionTerminalAsync"/>
    public async Task<SessionTerminal> ReadSessionTerminalAsync(CallerIdentity caller, Guid id, CancellationToken token)
    {
        using var admission = Admission.Enter((await sessions.GetAsync(id, token)).Scope.ProjectId);
        using var snapshot = policy.Pin();
        using var sessionLease = await Gate(id, token);
        var session = await OwnedAsync(caller, id, RelayCapabilities.ManageSession, token);
        var terminal = await sessions.ReadTerminalAsync(id, token);
        var observedAt = DateTimeOffset.UtcNow;
        if (terminal.Approval is null) return terminal with { ObservedAt = observedAt };
        try
        {
            policy.Demand(caller, session.Scope.ProjectId, RelayCapabilities.DecideApproval);
            return terminal with { ObservedAt = observedAt, CanDecideApproval = true };
        }
        catch (RelayException error) when (error.Code is "capability_denied" or "project_draining" or "project_archived")
        {
            return terminal with { ObservedAt = observedAt, CanDecideApproval = false,
                MissingCapability = error.Code == "capability_denied" ? RelayCapabilities.DecideApproval : null };
        }
    }

    /// <summary>Send a fixed key to the owned session. Approval requires the exact reviewed viewport.</summary>
    /// <seealso cref="ReadSessionTerminalAsync"/>
    public async Task<TerminalSignalReceipt> SignalSessionTerminalAsync(CallerIdentity caller, Guid id,
        string action, string? approvalRevision, CancellationToken token)
    {
        if (action is not ("escape" or "approve" or "refuse") || (action == "escape" ? approvalRevision is not null :
            approvalRevision is not { Length: 64 } || !approvalRevision.All(Uri.IsHexDigit)))
            throw new RelayException("invalid_request", "Choose Escape, or approve/refuse using the reviewed approval revision.", false);
        using var admission = Admission.Enter((await sessions.GetAsync(id, token)).Scope.ProjectId);
        using var snapshot = policy.Pin();
        using var sessionLease = await Gate(id, token);
        var session = await OwnedAsync(caller, id, RelayCapabilities.ManageSession, token);
        if (action != "escape")
            policy.Demand(caller, session.Scope.ProjectId, RelayCapabilities.DecideApproval);
        var pending = action == "refuse" ? await work.RetainedApprovalsAsync(id, token) : [];
        var receipt = await sessions.SignalTerminalAsync(id, action, approvalRevision, token);
        if (receipt.KeySent && pending.Count == 1)
            await work.RecordApprovalRefusalAsync(pending[0].Request.Id, CancellationToken.None);
        if (receipt.KeySent && action is ("escape" or "refuse"))
            await work.RecordTerminalInterruptionAsync(id, cancelActive: action == "escape", CancellationToken.None);
        return receipt;
    }
}
