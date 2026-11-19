namespace AgentRelay.Core;

public sealed record TerminalApproval(string Revision, string Text);
public sealed record SessionTerminal(string Client, string Text, TerminalApproval? Approval);
public sealed record TerminalSignalReceipt(string Action, bool KeySent);

public sealed partial class RelayApplication
{
    /// <summary>Read the current viewport of the owned session for review or targeted terminal input.</summary>
    /// <seealso cref="SignalSessionTerminalAsync"/>
    public async Task<SessionTerminal> ReadSessionTerminalAsync(CallerIdentity caller, Guid id, CancellationToken token)
    {
        using var admission = Admission.Enter((await sessions.GetAsync(id, token)).Scope.ProjectId);
        using var snapshot = policy.Pin();
        using var sessionLease = await Gate(id, token);
        await OwnedAsync(caller, id, RelayCapabilities.ManageSession, token);
        return await sessions.ReadTerminalAsync(id, token);
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
        var receipt = await sessions.SignalTerminalAsync(id, action, approvalRevision, token);
        if (receipt.KeySent && action is ("escape" or "refuse"))
            await work.RecordTerminalInterruptionAsync(id, cancelActive: action == "escape", CancellationToken.None);
        return receipt;
    }
}
