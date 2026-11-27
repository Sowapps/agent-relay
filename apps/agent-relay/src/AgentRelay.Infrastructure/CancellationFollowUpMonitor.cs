using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

internal sealed class CancellationFollowUpMonitor(SqliteWorkStore store, RelayApplication app, Action<Exception>? reportError)
{
    private long cursor;

    public async Task SweepAsync(CancellationToken token)
    {
        var page = await store.PendingCancellationFollowUpsAsync(cursor, token);
        cursor = page.Count == 0 ? 0 : page[^1].Cursor;
        await Parallel.ForEachAsync(page, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = token },
            async (entry, stopping) =>
            {
                try { await DeliverAsync(entry.Record, stopping); }
                catch (Exception error) when (!stopping.IsCancellationRequested) { reportError?.Invoke(error); }
            });
    }

    private async Task DeliverAsync(WorkRecord retained, CancellationToken token)
    {
        var followUp = retained.FollowUp!;
        var source = retained.Request;
        if (source.Scope is not { } scope || source.SessionId is not { } session) return;
        Task Record(string state, string? diagnostic = null) =>
            store.RecordFollowUpStateAsync(source.Id, followUp.RequestId, state, diagnostic, token);

        if (await store.FindAsync(followUp.RequestId, token) is { } existing)
        {
            var sent = existing.Request;
            if (sent.SingleDispatch && sent.PreviousRequestId == source.Id && sent.SessionId == session &&
                sent.Scope?.ProjectId == scope.ProjectId && sent.Scope.SenderId == followUp.SenderId && sent.Text == followUp.Text)
                await Record("submitted");
            else await Record("failed", "id_conflict");
            return;
        }

        var caller = new CallerIdentity(followUp.SenderId, [scope.ProjectId]);
        try
        {
            var current = await app.ResultAsync(caller, source.Id, token);
            if (current.Administration?.Disposition is "disabled" or "deleted")
            {
                await Record("not_sent", "request_disabled");
                return;
            }
            var confirmed = current.CancellationConfirmedAt is not null ||
                current is { ApprovalResumeVerifiedAt: not null, State: not "completed" } ||
                current.ErrorCode is WorkExecution.RetryCancelled or "cancelled_before_dispatch";
            if (!confirmed)
            {
                if (current.State == "completed" && !WorkExecution.ApprovalStopPending(current))
                    await Record("not_sent", "operation_already_finished");
                return;
            }
            if (await app.RequestActivityAsync(caller, current, token) is not { State: "waiting_for_input" }) return;
            await app.MessageAsync(caller, session, followUp.RequestId, followUp.Text, source.TaskId ?? source.Id, token, source.Id, singleDispatch: true);
            await Record("submitted");
        }
        catch (RelayException error) when (error.Code is "busy" or "session_awaiting_approval" or "session_not_ready" or "unavailable")
        {
            // A fresh observation can recover these conditions without sending a second attempt.
        }
        catch (RelayException error)
        {
            await Record("failed", error.Code);
        }
    }
}
