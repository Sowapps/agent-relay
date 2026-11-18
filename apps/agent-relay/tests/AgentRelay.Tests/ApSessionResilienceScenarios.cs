using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class ApSessionResilienceScenarios
{
    public static async Task RunAsync(string data)
    {
        var database = new RelayDatabase(data);
        var store = new SqliteWorkStore(database);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var token = deadline.Token;
        var executor = new FixtureExecutor();
        var scope = new OperationScope(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        WorkRequest Request(string surface) => new(Guid.NewGuid(), "fixture", "message", surface, SessionId: Guid.NewGuid(), Scope: scope);
        var refused = Request("offline");
        var first = Request("frontend");
        var second = Request("backend");

        await using (var service = new WorkService(store, executor, () => [new("fixture", false, true)]))
        {
            await RefusesAsync("session_not_ready", () => service.SubmitAsync(refused, token));
            Check(await store.FindAsync(refused.Id, token) is null, "Offline request entered the journal before readiness.");

            var start = service.SubmitAsync(first, token);
            await executor.FrontendPreparing.Task.WaitAsync(token);
            var same = service.SubmitAsync(first, token);
            await RefusesAsync("id_conflict", () => service.SubmitAsync(first with { Text = "different" }, token));
            await RefusesAsync("busy", () => service.SubmitAsync(first with { Id = Guid.NewGuid() }, token));
            await service.SubmitAsync(second, token).WaitAsync(token);
            Check(await store.FindAsync(first.Id, token) is null, "Preparation wrote pending before readiness.");
            executor.FrontendReady.TrySetResult();
            await Task.WhenAll(start, same).WaitAsync(token);
            await executor.BothDispatched.Task.WaitAsync(token);
            Check(executor.Dispatches == 2, "Duplicate request was dispatched.");
            executor.Finish.TrySetResult();
            while ((await service.ReadAsync(second.Id, token)).State != "completed" || (await service.ReadAsync(first.Id, token)).State != "unknown")
                await Task.Delay(10, token);
        }

        var retained = (await store.FindAsync(first.Id, token))!;
        await store.SaveAsync(retained with { State = "completed", Result = new("fixture", "correlated receipt") }, token);
        Check(executor.Dispatches == 2, "Reconciliation retried uncertain work.");
        var beforeDispatch = Request("integration");
        await store.SaveAsync(new(beforeDispatch, "pending", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DispatchBoundaryTracked: true), token);
        await store.RecoverAsync(token);
        Check((await store.FindAsync(beforeDispatch.Id, token))?.ErrorCode == "host_stopped_before_dispatch", "Proven pre-dispatch interruption was treated as uncertain.");
        await NextNativeObservation(data, first.SessionId!.Value, token);
        await RestoreBoundSessionAsync(data, token);
        var project = new ProjectAdmission();
        using var firstAdmission = project.EnterWork(scope.ProjectId);
        using var secondAdmission = project.EnterWork(scope.ProjectId);
        await RefusesAsync("busy", () => Task.FromResult(project.Enter(scope.ProjectId)));
        Console.WriteLine("AP_SESSION_RESILIENCE=PASS (distinct sessions share a workspace; same-session admission; durable results; no replay)");
    }

    private static async Task RestoreBoundSessionAsync(string data, CancellationToken token)
    {
        var runtime = new FixtureRuntime();
        var service = new SessionService(data, null!, runtime, null!);
        var profile = new WslProfile("fixture", "Fixture", "fixture", "fixture", "/fixture", "codex", []);
        var session = new RelaySession(Guid.NewGuid(), "Fixture", "fixture", profile, profile.Root, "created", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await RefusesAsync("session_not_ready", () => service.EnsureReadyAsync(session, profile, mayRestore: false, token));
        Check(runtime.Starts == 0, "A caller without restore permission started the session.");
        Check((await service.EnsureReadyAsync(session, profile, mayRestore: true, token)).State == "ready", "Bound session did not restore.");
        Check((await service.EnsureReadyAsync(session, profile, mayRestore: true, token)).State == "ready" && runtime.Starts == 1,
            "A ready session was restarted.");
    }

    private static async Task NextNativeObservation(string data, Guid session, CancellationToken token)
    {
        var service = new SessionService(data, null!, null!, null!);
        var started = DateTimeOffset.UtcNow;
        var observation = new AgentStatus("fixture", "codex", "ready", "", "fixture", "fixture", "fixture",
            DistributionRuntime: new("distribution-1", started), NativeRuntime: new("native-1", started), NativePresent: true);
        Check((await service.ObserveAsync(session, observation, token)).Cause == "initial_observation", "Initial observation was misclassified.");
        Check((await service.ObserveAsync(session, observation with { DistributionRuntime = null }, token)).Cause == "unknown", "Unavailable telemetry was guessed.");
        Check((await service.ObserveAsync(session, observation with { NativeRuntime = null, NativePresent = false }, token)).Cause == "native_exit", "Native exit not detected.");
        Check((await service.ObserveAsync(session, observation with { DistributionRuntime = new("distribution-2", started) }, token)).Cause == "wsl_restart", "WSL restart not detected.");
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static async Task RefusesAsync<T>(string code, Func<Task<T>> call)
    {
        try { await call(); }
        catch (RelayException error) when (error.Code == code && error.EffectsPossible == false) { return; }
        throw new InvalidOperationException("Expected refusal before execution: " + code);
    }

    private sealed class FixtureExecutor : IWorkExecutor, IWorkPreparation
    {
        public TaskCompletionSource FrontendPreparing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FrontendReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource BothDispatched { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Dispatches;

        public async Task<IPreparedWork> PrepareAsync(WorkRequest request, CancellationToken token)
        {
            if (request.Text == "offline") throw new RelayException("session_not_ready", "Fixture offline", false);
            if (request.Text == "frontend")
            {
                FrontendPreparing.TrySetResult();
                await FrontendReady.Task.WaitAsync(token);
            }
            return new ScopedPreparedWork(dispatchToken => ExecuteAsync(request, dispatchToken));
        }

        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        {
            if (Interlocked.Increment(ref Dispatches) == 2) BothDispatched.TrySetResult();
            await Finish.Task.WaitAsync(token);
            if (request.Text == "frontend") throw new RelayException("reply_unconfirmed", "Fixture interrupted after dispatch", true);
            return new("fixture", "confirmed");
        }
    }

    private sealed class FixtureRuntime : ISessionRuntime
    {
        public int Starts;
        public Task<AgentStatus> StatusAsync(WslProfile profile, CancellationToken token) => Task.FromResult(new AgentStatus(
            profile.Id, profile.Client, Starts == 0 ? "not_started" : "ready", "Fixture", profile.Distribution, profile.User, profile.Root,
            NativeConversationId: "original-conversation", DistributionRuntime: new("fixture-boot", DateTimeOffset.UnixEpoch),
            NativeRuntime: Starts == 0 ? null : new("fixture-process", DateTimeOffset.UnixEpoch), NativePresent: Starts > 0));

        public Task<AgentStatus> StartAsync(WslProfile profile, CancellationToken token, bool resume = false, bool requirePinnedConversation = false)
        {
            Check(resume && requirePinnedConversation, "On-demand start could substitute the bound conversation.");
            Starts++;
            return StatusAsync(profile, token);
        }
    }
}
