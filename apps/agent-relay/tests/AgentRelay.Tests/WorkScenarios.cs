using System.Diagnostics;
using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class WorkScenarios
{
    internal static async Task Run(string data)
    {
        var temporary = NativeFailures.Read("""
            {"error":"typed","code":"native_provider_rate_limited","effectsPossible":false,"nativeTurnEnded":true,"retrySafe":true,"retryAfterSeconds":20}
            """, "");
        Assert(ScopedWorkExecutor.RetryDelay(1, temporary) == TimeSpan.FromSeconds(20), "provider delay overrides first local delay");
        Assert(ScopedWorkExecutor.RetryDelay(2, temporary) == TimeSpan.FromSeconds(30) &&
            ScopedWorkExecutor.RetryDelay(3, temporary) is null, "second delay and exhaustion are bounded");
        var uncertain = new RelayException("server_overloaded", "native action was observed", true, true, TimeSpan.Zero);
        Assert(ScopedWorkExecutor.RetryDelay(1, uncertain) is null, "uncertain effects cannot authorize another attempt");
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
        await DurableRetryAsync(store, capability);
        Console.WriteLine("MVP_WORK_STATES=PASS");
    }

    private static async Task DurableRetryAsync(SqliteWorkStore store, WorkCapability capability)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var due = DateTimeOffset.UtcNow.AddHours(1);
        var parked = new WorkRequest(Guid.NewGuid(), "fixture", "message", "park", SessionId: Guid.NewGuid());
        var cancelled = parked with { Id = Guid.NewGuid(), SessionId = Guid.NewGuid() };
        var ordinary = parked with { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Text = "ordinary" };
        var executor = new DeferredExecutor(store, due);
        await using (var service = new WorkService(store, executor, () => [capability], maximumConcurrency: 1))
        {
            await service.SubmitAsync(parked, token);
            await Idle(service);
            var waiting = await service.ReadAsync(parked.Id, token);
            Assert(waiting is { State: "retry_wait", NativeAttempts.Length: 1 } && waiting.Retry?.DueAt == due,
                "parked attempt releases the only worker and retains its due time");
            await Refuses("busy", () => service.SubmitAsync(parked with { Id = Guid.NewGuid() }, token));
            await service.SubmitAsync(ordinary, token);
            await Idle(service);
            Assert((await service.ReadAsync(ordinary.Id, token)).State == "completed", "an independent session runs while a retry waits");
            await service.SubmitAsync(cancelled, token);
            await Idle(service);
            Assert((await service.RequestCancellationAsync(cancelled.Id, token)).ErrorCode == "native_retry_cancelled",
                "a parked attempt can be cancelled before dispatch");
        }
        await store.RecoverAsync(token);
        await using var restarted = new WorkService(store, executor, () => [capability], maximumConcurrency: 1);
        await restarted.ResumeDueRetriesAsync(due.AddSeconds(-1), token);
        Assert(!restarted.IsActive && executor.Resumed == 0, "restart preserves the provider deadline");
        await restarted.ResumeDueRetriesAsync(due, token);
        await Idle(restarted);
        await restarted.ResumeDueRetriesAsync(due.AddSeconds(1), token);
        var result = await restarted.ReadAsync(parked.Id, token);
        Assert(result is { State: "completed", Retry: null, NativeAttempts.Length: 2 } &&
            result.Request.Id == parked.Id && executor.Resumed == 1 && !restarted.IsActive,
            "the due retry resumes once with its original request ID and ignores the cancelled retry");

        var beforeParking = parked with { Id = Guid.NewGuid() };
        var retry = new NativeRetry(due, "process", "distribution", "workspace");
        await store.SaveAsync(new(beforeParking, "running", due, due), token);
        Assert(await store.BeginNativeAttemptAsync(beforeParking.Id, 1, token), "first dispatch intent retained");
        await store.RequestCancellationAsync(beforeParking.Id, token);
        Assert(!await store.FinishNativeAttemptAsync(beforeParking.Id, 1, true, "server_overloaded", retry, token),
            "cancellation committed before parking prevents the next attempt");

        var claimed = parked with { Id = Guid.NewGuid() };
        await store.SaveAsync(new(claimed, "running", due, due), token);
        Assert(await store.BeginNativeAttemptAsync(claimed.Id, 1, token) &&
            await store.FinishNativeAttemptAsync(claimed.Id, 1, true, "server_overloaded", retry, token) &&
            await store.ClaimRetryAsync(claimed.Id, due, token), "due retry is claimed before cancellation");
        var receipt = await restarted.RequestCancellationAsync(claimed.Id, token);
        Assert(receipt.ErrorCode == WorkExecution.RetryCancelled &&
            !await store.BeginNativeAttemptAsync(claimed.Id, 2, token), "cancelling a claim before dispatch prevents terminal input");
        await store.SaveAsync(receipt with { ErrorCode = "stale_worker_failure" }, token);
        Assert((await restarted.ReadAsync(claimed.Id, token)).ErrorCode == WorkExecution.RetryCancelled, "stale worker cannot replace the cancellation receipt");

        async Task Idle(WorkService service)
        {
            while (service.IsActive) await Task.Delay(10, token);
        }
    }

    private sealed class DeferredExecutor(SqliteWorkStore store, DateTimeOffset due, ControlledExecutor? occupying = null) : IWorkExecutor, IRetryPreparation
    {
        public int Resumed { get; private set; }
        public TaskCompletionSource ResumedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        {
            if (request.Text == "occupy" && occupying is not null) return await occupying.ExecuteAsync(request, token);
            if (request.Text != "park") return new("fixture", "ordinary");
            Assert(await store.BeginNativeAttemptAsync(request.Id, 1, token), "initial native attempt admitted");
            Assert(await store.FinishNativeAttemptAsync(request.Id, 1, true, "server_overloaded",
                new(due, "process", "distribution", "workspace"), token), "native attempt parked");
            throw new WorkDeferredException();
        }

        public Task<IPreparedWork> PrepareRetryAsync(WorkRecord record, CancellationToken token) =>
            Task.FromResult<IPreparedWork>(new ScopedPreparedWork(async dispatchToken =>
            {
                Assert(await store.BeginNativeAttemptAsync(record.Request.Id, 2, dispatchToken), "resumed native attempt admitted");
                Resumed++;
                ResumedSignal.TrySetResult();
                return new("fixture", "retried");
            }));
    }

    internal static async Task MeasureRetryAsync(string directory)
    {
        if (Directory.Exists(directory)) throw new IOException("Use a fresh local measurement directory.");
        Directory.CreateDirectory(directory);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        using var scheduler = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        using var process = Process.GetCurrentProcess();
        var token = deadline.Token;
        var interval = TimeSpan.FromSeconds(2);
        var database = new SqliteWorkStore(new RelayDatabase(directory));
        var store = new MeasuredRetryStore(database);
        var due = DateTimeOffset.UtcNow.AddSeconds(21);
        var occupied = new ControlledExecutor();
        var executor = new DeferredExecutor(database, due, occupied);
        await using var service = new WorkService(store, executor, () => [new("fixture", true, true)], maximumConcurrency: 1);
        var idle = await SampleAsync(async () =>
        {
            using var warmup = new PeriodicTimer(interval);
            for (var sample = 0; sample < 6; sample++)
            {
                await warmup.WaitForNextTickAsync(token);
                await service.ResumeDueRetriesAsync(DateTimeOffset.UtcNow, token);
            }
        });
        var work = new WorkRequest(Guid.NewGuid(), "fixture", "message", "park", SessionId: Guid.NewGuid());
        await service.SubmitAsync(work, token);
        while (service.IsActive) await Task.Delay(20, token);
        await service.SubmitAsync(work with { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Text = "occupy" }, token);
        await occupied.Entered.Task.WaitAsync(token);
        var loop = PollAsync();
        var capacityReleasedAt = due;
        object contention;
        try
        {
            contention = await SampleAsync(async () =>
            {
                await Task.Delay(due.AddSeconds(4) - DateTimeOffset.UtcNow, token);
                capacityReleasedAt = DateTimeOffset.UtcNow;
                occupied.Release.TrySetResult();
                await executor.ResumedSignal.Task.WaitAsync(token);
                while (service.IsActive) await Task.Delay(20, token);
            });
        }
        finally
        {
            scheduler.Cancel();
            await loop;
        }
        var result = await service.ReadAsync(work.Id, token);
        Assert(result is { State: "completed", NativeAttempts.Length: 2 }, "measured operation completed with its retry");
        var dispatch = result.NativeAttempts![1].StartedAt;
        var report = new { intervalMs = interval.TotalMilliseconds, maximumConcurrency = 1, idle, contention,
            retryLatenessMs = (dispatch - due).TotalMilliseconds, dispatchAfterCapacityReleasedMs = (dispatch - capacityReleasedAt).TotalMilliseconds, dueAt = due,
            scope = "Harness .NET process with production SqliteWorkStore and WorkService, fake executor; reads count only the due-retry SELECT" };
        await File.WriteAllTextAsync(Path.Combine(directory, "retry-metrics.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }), token);
        Console.WriteLine(JsonSerializer.Serialize(report));

        async Task PollAsync()
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(scheduler.Token))
                    await service.ResumeDueRetriesAsync(DateTimeOffset.UtcNow, scheduler.Token);
            }
            catch (OperationCanceledException) when (scheduler.IsCancellationRequested) { }
        }

        async Task<object> SampleAsync(Func<Task> action)
        {
            var reads = store.Reads;
            process.Refresh();
            var cpu = process.TotalProcessorTime;
            var start = Stopwatch.StartNew();
            await action();
            process.Refresh();
            return new { elapsedMs = start.Elapsed.TotalMilliseconds, reads = store.Reads - reads,
                cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds, peakWorkingSetBytes = process.PeakWorkingSet64 };
        }
    }

    private sealed class MeasuredRetryStore(SqliteWorkStore database) : IWorkStore
    {
        private int reads;
        public int Reads => Volatile.Read(ref reads);
        public Task<WorkRecord?> FindAsync(Guid id, CancellationToken token) => database.FindAsync(id, token);
        public Task SaveAsync(WorkRecord work, CancellationToken token) => database.SaveAsync(work, token);
        public Task RecoverAsync(CancellationToken token) => database.RecoverAsync(token);
        public Task<IReadOnlyList<WorkRecord>> ListAsync(CancellationToken token) => database.ListAsync(token);
        public Task<bool> ClaimRetryAsync(Guid id, DateTimeOffset now, CancellationToken token) => database.ClaimRetryAsync(id, now, token);
        public Task<IReadOnlyList<WorkRecord>> DueRetriesAsync(DateTimeOffset now, CancellationToken token)
        {
            Interlocked.Increment(ref reads);
            return database.DueRetriesAsync(now, token);
        }
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
    internal static async Task RetainNativeFailureAsync(SqliteWorkStore store, WorkRequest request, string nativeEnvelope)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var executor = new FailingExecutor(NativeFailures.Read(nativeEnvelope, ""));
        await using var work = new WorkService(store, executor, () => [new(request.ProfileId, true, true)]);
        await work.SubmitAsync(request, deadline.Token);
        while ((await work.ReadAsync(request.Id, deadline.Token)).State is "pending" or "running")
            await Task.Delay(10, deadline.Token);
    }

    private sealed class FailingExecutor(Exception error) : IWorkExecutor
    { public Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token) => Task.FromException<WorkResult>(error); }
}
