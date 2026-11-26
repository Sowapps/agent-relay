using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class ProjectRequestRecoveryScenarios
{
    internal static async Task RunAsync(string data)
    {
        var project = Guid.NewGuid();
        var foreign = Guid.NewGuid();
        var manager = Guid.NewGuid();
        var worker = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var workspace = Guid.NewGuid();
        var settings = new ProjectSettingsStore(data);
        settings.Save(new(1, [new(project, "Project"), new(foreign, "Foreign")],
            [new(manager, "Principal", "fixture", "test"), new(worker, "Worker", "fixture", "test"), new(endpoint, "Peer", "fixture", "test")],
            [new(workspace, endpoint, "Fixture", data)],
            [new(project, manager, "main", [RelayCapabilities.ManageProjectRequests, RelayCapabilities.RunCommand, RelayCapabilities.ReadFile],
                [new(workspace, true)], ["list"]),
             new(project, worker, "secondary", [RelayCapabilities.ReadHistory], []), new(project, endpoint, "secondary", [], []),
             new(foreign, worker, "main", [RelayCapabilities.ManageProjectRequests], [])]));
        var database = new RelayDatabase(data);
        var store = new SqliteWorkStore(database);
        var native = new Fixture();
        await using var work = new WorkService(store, native, () => [new(endpoint.ToString("D"), true, false)]);
        var app = new RelayApplication(new(settings), work, native, new ProjectJournal(database), recovery: native);
        var caller = new CallerIdentity(manager, [project]);
        var follower = new CallerIdentity(worker, [project, foreign]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var token = deadline.Token;
        var now = DateTimeOffset.UtcNow;
        var original = new WorkRequest(Guid.NewGuid(), endpoint.ToString("D"), "command", CommandId: "list",
            Scope: new(project, worker, endpoint, workspace), TaskId: Guid.NewGuid());
        await store.SaveAsync(new(original, "unknown", now, now, ErrorCode: "execution_unknown"), token);
        Check(await app.ResultAsync(caller, original.Id, token) is { State: "unknown", ReadRecoveryError: "adapter_unavailable" },
            "A failed native recovery hid the retained result.");
        await Refuse("capability_denied", () => app.ChangeRequestAsync(follower, original.Id, new(0, "delete"), token));

        var edit = await app.ChangeRequestAsync(caller, original.Id, new(0, "edit", "Renamed", "Escalated", "Needs review"), token);
        Check(edit is { State: "unknown", Administration: { Revision: 1, Label: "Renamed", Status: "Needs review" } },
            "Administrative edits rewrote or lost execution evidence.");
        await Refuse("state_conflict", () => app.ChangeRequestAsync(caller, original.Id, new(0, "disable"), token));

        var retryId = Guid.NewGuid();
        var retry = new NativeRetry(now.AddMinutes(1), "native", "distribution", "workspace");
        await store.SaveAsync(new(original with { Id = retryId }, "retry_wait", now, now, Retry: retry), token);
        Check(await app.ChangeRequestAsync(caller, retryId, new(0, "disable"), token) is
            { Administration.Disposition: "disabled", Retry: null, CancellationRequestedAt: not null },
            "Disabling a request left an automatic retry scheduled.");
        Check((await store.DueRetriesAsync(now.AddHours(1), token)).Count == 0, "The disabled request can still retry.");
        Check(await app.ChangeRequestAsync(caller, original.Id, new(1, "delete"), token) is
            { Administration.Disposition: "deleted", ErrorCode: "execution_unknown" }, "Deletion erased technical evidence.");
        Check(!(await app.ResultsAsync(caller, new(project), token)).Items.Any(item => item.Request.Id == original.Id) &&
            (await app.ResultsAsync(caller, new(project, IncludeDeleted: true), token)).Items.Any(item => item.Request.Id == original.Id),
            "Ordinary history and explicitly requested deleted history disagree.");
        Check(await app.ChangeRequestAsync(caller, original.Id, new(2, "restore"), token) is { Administration.Disposition: "active" } &&
            (await app.ResultsAsync(caller, new(project), token)).Items.Any(item => item.Request.Id == original.Id),
            "Restoring a deleted request did not make its retained record visible.");

        var newId = Guid.NewGuid();
        var rescheduled = await app.RescheduleRequestAsync(caller, original.Id, newId, null, null, token);
        Check(rescheduled.Request.PreviousRequestId == original.Id && rescheduled.Request.Scope?.SenderId == manager &&
            rescheduled.Request.TaskId == original.TaskId, "The new attempt lost its identity, author or relationship.");
        await native.Running.Task.WaitAsync(token);
        Check((await app.RescheduleRequestAsync(caller, original.Id, newId, null, null, token)).Request == rescheduled.Request,
            "Rescheduling with the same new ID submitted another attempt.");
        await app.CancelAsync(caller, newId, token);
        await native.Interrupted.Task.WaitAsync(token);
        Check(native.Attempts == 1, "Rescheduling or cancellation produced more than one dispatch.");
        var followerCommand = Guid.NewGuid();
        await work.SubmitAsync(original with { Id = followerCommand }, token);
        await native.FollowerRunning.Task.WaitAsync(token);
        Check((await app.CancelAsync(caller, followerCommand, token)).CancellationRequestedAt is not null,
            "The principal could not cancel another participant's command.");
        await native.FollowerInterrupted.Task.WaitAsync(token);

        var foreignId = Guid.NewGuid();
        await store.SaveAsync(new(original with { Id = foreignId, Scope = original.Scope! with { ProjectId = foreign } }, "failed", now, now), token);
        await Refuse("capability_denied", () => app.ResultAsync(caller, foreignId, token));
        await Refuse("capability_denied", () => app.ChangeRequestAsync(caller, foreignId, new(0, "delete"), token));
        await Refuse("capability_denied", () => app.RescheduleRequestAsync(caller, foreignId, Guid.NewGuid(), null, null, token));
        Console.WriteLine("PROJECT_REQUEST_RECOVERY=PASS (cross-owner edit, delete/restore visibility, linked attempt, cancellation, project isolation)");
    }

    private static async Task Refuse(string code, Func<Task> operation)
    {
        try { await operation(); }
        catch (RelayException error) when (error.Code == code) { return; }
        throw new InvalidOperationException("Expected " + code);
    }

    private static void Check(bool condition, string invariant)
    {
        if (!condition) throw new InvalidOperationException(invariant);
    }

    private sealed class Fixture : IWorkExecutor, IRelaySessions, IOperationRecovery
    {
        public int Attempts { get; private set; }
        public TaskCompletionSource Running { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Interrupted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FollowerRunning { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource FollowerInterrupted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
        {
            Attempts++;
            var follower = request.PreviousRequestId is null;
            (follower ? FollowerRunning : Running).TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { (follower ? FollowerInterrupted : Interrupted).TrySetResult(); }
            throw new InvalidOperationException("Expected cancellation.");
        }
        public Task<WorkRecord> ReconcileAsync(WorkRecord record, CancellationToken token) =>
            throw new RelayException("adapter_unavailable", "Fixture native endpoint unavailable.", false);
        public Task<SessionSnapshot> OpenAsync(SessionDefinition definition, CancellationToken token) => throw new NotSupportedException();
        public Task<SessionSnapshot> GetAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<SessionSnapshot> InspectAsync(Guid id, CancellationToken token) => throw new NotSupportedException();
        public Task<SessionSnapshot> ControlAsync(Guid id, string action, CancellationToken token) => throw new NotSupportedException();
        public Task<ReadPage<SessionSnapshot>> ListAsync(ReadQuery query, Guid ownerId, CancellationToken token) => throw new NotSupportedException();
        public Task<IReadOnlyList<string>> RefreshMessagesAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token) => throw new NotSupportedException();
    }
}
