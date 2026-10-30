using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class WorkScenarios
{
    internal static async Task Run(string data)
    {
        var store = new SqliteWorkStore(new RelayDatabase(Path.Combine(data, "work")));
        var executor = new ControlledExecutor();
        var capability = new WorkCapability("fixture", true, true);
        var request = new WorkRequest(Guid.NewGuid(), "fixture", "message", "real store, deterministic executor");
        await using (var service = new WorkService(store, executor, () => [capability]))
        {
            await service.SubmitAsync(request, CancellationToken.None);
            await executor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert(executor.Count == 1, "intent dispatched once");
            await service.SubmitAsync(request, CancellationToken.None);
            Assert(executor.Count == 1, "pending duplicate not dispatched");
            await Refuses("id_conflict", () => service.SubmitAsync(request with { Text = "different" }, CancellationToken.None));
            await Refuses("busy", () => service.SubmitAsync(request with { Id = Guid.NewGuid() }, CancellationToken.None));
            await Refuses("capability_denied", () => service.SubmitAsync(request with { Id = Guid.NewGuid(), ProfileId = "missing" }, CancellationToken.None));
            executor.Release.TrySetResult();
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while ((await service.ReadAsync(request.Id, CancellationToken.None)).State != "completed")
            { if (DateTime.UtcNow > deadline) throw new TimeoutException("Work completion"); await Task.Delay(20); }
        }
        var interrupted = request with { Id = Guid.NewGuid() };
        await store.SaveAsync(new(interrupted, "running", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
        await store.RecoverAsync(CancellationToken.None);
        await using (var restarted = new WorkService(store, executor, () => [capability]))
        {
            var unknown = await restarted.SubmitAsync(interrupted, CancellationToken.None);
            Assert(unknown.State == "unknown" && executor.Count == 1, "interrupted intent is unknown and never replayed");
            Assert((await restarted.ReadAsync(request.Id, CancellationToken.None)).State == "completed", "completed work survives restart");
        }
        foreach (var (error, expected) in new (Exception, string)[] { (new TimeoutException("fixture deadline"), "unknown"),
                     (new RelayException("unavailable", "fixture unavailable"), "failed"),
                     (new RelayException("approval_required", "Native approval needed"), "awaiting_approval"),
                     (new RelayException("agent_authentication_required", "Authentication failed after dispatch", effectsPossible: true), "unknown") })
        {
            await using var service = new WorkService(store, new FailingExecutor(error), () => [capability]);
            var failedRequest = request with { Id = Guid.NewGuid() };
            await service.SubmitAsync(failedRequest, CancellationToken.None);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            WorkRecord failed;
            do { failed = await service.ReadAsync(failedRequest.Id, CancellationToken.None); if (DateTime.UtcNow > deadline) throw new TimeoutException("Failure state"); await Task.Delay(10); }
            while (failed.State is "pending" or "running");
            Assert(failed.State == expected, error.GetType().Name + " maps to " + expected);
        }
        Console.WriteLine("MVP_WORK_STATES=PASS");
    }

    private static void Assert(bool value, string name) { if (!value) throw new InvalidOperationException(name); Console.WriteLine("PASS: " + name); }
    private static async Task Refuses(string code, Func<Task<WorkRecord>> action)
    { try { await action(); throw new InvalidOperationException("Expected " + code); } catch (RelayException error) when (error.Code == code) { Console.WriteLine("PASS: refused " + code); } }

    private sealed class ControlledExecutor : IWorkExecutor
    {
        internal int Count;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        { Interlocked.Increment(ref Count); Entered.TrySetResult(); await Release.Task.WaitAsync(token); return new("fixture", "confirmed"); }
    }
    private sealed class FailingExecutor(Exception error) : IWorkExecutor
    { public Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token) => Task.FromException<WorkResult>(error); }
}
