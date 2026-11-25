using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

/// <summary>Visit durable replies independently of WorkService admission. The cursor rotates through all retained requests.</summary>
internal sealed class ReplyMonitor(SqliteWorkStore store, IReplyObservation observer, Action<Exception>? reportError)
{
    private long cursor;

    public async Task SweepAsync(CancellationToken token)
    {
        var page = await store.AwaitingRepliesAsync(cursor, token);
        cursor = page.Count == 0 ? 0 : page[^1].Cursor;
        await Parallel.ForEachAsync(page, new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token },
            async (entry, stopping) =>
            {
                try { await observer.ObserveReplyAsync(entry.Record, stopping); }
                catch (Exception error) when (!stopping.IsCancellationRequested) { reportError?.Invoke(error); }
            });
    }
}
