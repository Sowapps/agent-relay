using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class SqliteWorkStore(RelayDatabase database) : IWorkStore
{
    private const string ActiveState = "json_extract(payload,'$.state') IN ('pending','running')";
    internal const string DeferredState = "json_extract(payload,'$.state')='retry_wait'";
    internal const string ReplyObservationState = "(json_extract(payload,'$.state') IN ('awaiting_reply','unknown','awaiting_approval','approval_expired') AND json_extract(payload,'$.replyDispatch') IS NOT NULL)";
    internal const string RetryDue = "julianday(json_extract(payload,'$.retry.dueAt'))";
    private const string ResumeUnverified = "json_extract(payload,'$.approvalResumeVerifiedAt') IS NULL";
    internal const string PendingApprovalStop = "json_extract(payload,'$.approvalStopRequestedAt') IS NOT NULL AND json_extract(payload,'$.cancellationConfirmedAt') IS NULL AND " + ResumeUnverified;
    internal const string RetainedApproval = "((" + ResumeUnverified + ") AND (" +
        "json_extract(payload,'$.state')='awaiting_approval' OR (" +
        "json_extract(payload,'$.state')='approval_expired' AND json_extract(payload,'$.cancellationConfirmedAt') IS NULL) OR (" +
        "json_extract(payload,'$.state')='unknown' AND json_extract(payload,'$.sessionClosedAt') IS NULL AND " +
        "json_extract(payload,'$.cancellationConfirmedAt') IS NULL AND " +
        "(json_extract(payload,'$.terminalEscapeSentAt') IS NOT NULL OR json_extract(payload,'$.terminalRefusalSentAt') IS NOT NULL)))) OR (" +
        PendingApprovalStop + ")";
    internal const string RetainedApprovalsQuery =
        "SELECT payload FROM work WHERE json_extract(payload,'$.request.sessionId')=$id AND (" + RetainedApproval + ") LIMIT 2";
    private const string WaitingSince = "coalesce(json_extract(payload,'$.approvalWaitingSince'),json_extract(payload,'$.updatedAt'))";
    private const string LegacyApprovalStop = "json_extract(payload,'$.cancellationConfirmedAt') IS NULL AND id IN " +
        "(SELECT json_extract(payload,'$.retiredForCancellationId') FROM sessions WHERE json_extract(payload,'$.retiredForCancellationId') IS NOT NULL)";

    /// <summary>Check durable work without opening a host or recovering pending operations.</summary>
    /// <seealso cref="RecoverAsync"/>
    public static Task DemandIdleAsync(string dataDirectory, bool allowUninitialized, CancellationToken token) =>
        DemandMaintenanceIdleAsync(dataDirectory, allowUninitialized, false, token);

    internal static Task DemandUpdateIdleAsync(string dataDirectory, CancellationToken token, DateTimeOffset? historyBefore = null,
        Func<WorkRecord, bool>? historicalApproval = null, bool preserveLiveSessions = false) =>
        DemandMaintenanceIdleAsync(dataDirectory, false, true, token, historyBefore, historicalApproval, preserveLiveSessions);

    private static async Task DemandMaintenanceIdleAsync(string dataDirectory, bool allowUninitialized, bool includeUncertain, CancellationToken token,
        DateTimeOffset? historyBefore = null, Func<WorkRecord, bool>? historicalApproval = null, bool preserveLiveSessions = false)
    {
        var path = RelayDatabase.DatabasePath(dataDirectory);
        RuntimePaths.DemandPrivatePath(path);
        FileAttributes attributes;
        try { attributes = File.GetAttributes(path); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            if (allowUninitialized) return;
            throw new RelayException("storage_unavailable", "The relay database is missing. Verify the selected data directory before maintenance.", false);
        }
        if ((attributes & FileAttributes.Directory) != 0)
            throw new RelayException("storage_unavailable", "The selected relay database path is not a file.", false);
        using var connection = new RelayDatabase(dataDirectory, readOnly: true).Open();
        using var command = connection.CreateCommand();
        var uncertain = includeUncertain ? " OR (json_extract(payload,'$.state')='unknown' AND json_extract(payload,'$.cancellationConfirmedAt') IS NULL AND ($historyBefore IS NULL OR " +
            "julianday(json_extract(payload,'$.createdAt')) IS NULL OR julianday(json_extract(payload,'$.updatedAt')) IS NULL OR " +
            "julianday(json_extract(payload,'$.createdAt')) >= julianday($historyBefore) OR julianday(json_extract(payload,'$.updatedAt')) >= julianday($historyBefore)))" : "";
        // The host has drained its execution leases. A parked native approval is session state,
        // and an online update leaves its terminal and durable request alone.
        var approval = preserveLiveSessions ? "0" : $"({RetainedApproval}) OR ({LegacyApprovalStop})";
        // An approval needs the participant to resolve it. Report it before other blockers so that prepublication admission can reopen.
        command.CommandText = $"SELECT id,json_extract(payload,'$.state'),({approval}) AS approval,payload FROM work WHERE ({ActiveState} OR ({approval}){uncertain}) ORDER BY approval DESC";
        if (includeUncertain) command.Parameters.AddWithValue("$historyBefore", historyBefore is { } before ? before.ToString("O") : DBNull.Value);
        using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.GetBoolean(2) && historicalApproval is not null && historyBefore is { } started &&
                RelayJson.Deserialize<WorkRecord>(reader.GetString(3)) is { } retained &&
                (retained.State is "awaiting_approval" or "approval_expired" || WorkExecution.ApprovalStopPending(retained)) &&
                ApprovalExpiry.StartedAt(retained) is var waited && retained.CreatedAt != default &&
                waited != default && retained.CreatedAt <= waited && waited < started && historicalApproval(retained)) continue;
            if (reader.GetBoolean(2))
                throw new RelayException("update_approval_pending", "Operation " + reader.GetString(0) +
                    " may still continue. Resolve its approval or verify its cancellation before resuming maintenance.", false);
            throw new RelayException(includeUncertain && reader.GetString(1) == "unknown" ? "update_uncertain_work" : "busy",
                includeUncertain ? "Active or uncertain durable work blocks update: " + reader.GetString(0) : "Active durable work exists; finish it before maintenance.", false);
        }
    }

    public async Task ProgressAsync(Guid id, WorkProgress progress, CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE work SET payload=json_set(payload,'$.progress',json($progress),'$.updatedAt',$at) WHERE id=$id AND {ActiveState}";
        command.Parameters.AddWithValue("$id", id.ToString("D")); command.Parameters.AddWithValue("$progress", RelayJson.Serialize(progress));
        command.Parameters.AddWithValue("$at", progress.UpdatedAt.ToString("O")); await command.ExecuteNonQueryAsync(token);
    }
    public async Task<WorkRecord?> FindAsync(Guid id, CancellationToken token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM work WHERE id=$id";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync(token) is string json ? RelayJson.Deserialize<WorkRecord>(json) : null;
    }

    public Task SaveAsync(WorkRecord record, CancellationToken token) =>
        WriteAsync(record.Request.Id, existing => existing switch
        {
            { State: "completed", Result: not null } => null,
            { ErrorCode: WorkExecution.RetryCancelled } => null,
            null => record,
            _ => record with { ApprovalStopRequestedAt = existing.ApprovalStopRequestedAt, NativeAttempts = existing.NativeAttempts,
                ReplyDispatch = existing.ReplyDispatch }
        }, token);

    private sealed record Transition(WorkRecord Record, DateTimeOffset? JournalExpiry = null, bool ClearExpiry = false,
        string? Diagnostic = null, string? Event = null, DateTimeOffset? EventAt = null);

    private Task<bool> WriteAsync(Guid id, Func<WorkRecord?, WorkRecord?> change, CancellationToken token) =>
        WriteTransitionAsync(id, existing => change(existing) is { } record ? new(record) : null, token);

    private async Task<bool> WriteTransitionAsync(Guid id, Func<WorkRecord?, Transition?> change, CancellationToken token)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
        lookup.CommandText = "SELECT payload FROM work WHERE id=$id"; lookup.Parameters.AddWithValue("$id", id.ToString("D"));
        var existing = await lookup.ExecuteScalarAsync(token) is string previous ? RelayJson.Deserialize<WorkRecord>(previous) : null;
        var transition = change(existing);
        if (transition is null) return false;
        var record = transition.Record;
        record = record with { CancellationRequestedAt = record.CancellationRequestedAt ?? existing?.CancellationRequestedAt,
            CancellationConfirmedAt = record.CancellationConfirmedAt ?? existing?.CancellationConfirmedAt,
            SessionClosedAt = record.SessionClosedAt ?? existing?.SessionClosedAt,
            TerminalRefusalSentAt = existing?.TerminalRefusalSentAt ?? record.TerminalRefusalSentAt,
            TerminalEscapeSentAt = existing?.TerminalEscapeSentAt ?? record.TerminalEscapeSentAt,
            ApprovalResumeVerifiedAt = existing?.ApprovalResumeVerifiedAt ?? record.ApprovalResumeVerifiedAt,
            NativeTurnEnded = record.NativeTurnEnded || existing?.NativeTurnEnded == true,
            ApprovalWaitingSince = existing?.ApprovalWaitingSince ?? record.ApprovalWaitingSince,
            ApprovalExpiredAt = transition.ClearExpiry ? null : existing?.ApprovalExpiredAt ?? record.ApprovalExpiredAt,
            Progress = record.Progress ?? existing?.Progress };
        if (record.State == "completed" && record.Progress is { } progress) record = record with { Progress = progress with { Phase = "completed", Bytes = progress.TotalBytes } };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO work VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        command.Parameters.AddWithValue("$id", record.Request.Id.ToString("D"));
        command.Parameters.AddWithValue("$payload", RelayJson.Serialize(record));
        await command.ExecuteNonQueryAsync(token);
        if (transition.JournalExpiry is { } expired)
            await AppendEventAsync("approval_expired", expired, "approval_expired");
        if (existing?.State != record.State)
            await AppendEventAsync(record.State, record.State == "approval_expired" ? record.ApprovalExpiredAt ?? record.UpdatedAt : record.UpdatedAt,
                transition.Diagnostic ?? record.ErrorCode);
        if (transition.Event is { } eventName)
            await AppendEventAsync(eventName, transition.EventAt ?? record.UpdatedAt, transition.Diagnostic);
        transaction.Commit();
        return true;

        async Task AppendEventAsync(string state, DateTimeOffset at, string? diagnostic)
        {
            var scope = record.Request.Scope;
            var item = new WorkEvent(Guid.NewGuid(), record.Request.Id, scope?.ProjectId, record.Request.TaskId, record.Request.AttemptId,
                record.Request.SessionId, scope?.RecipientId, state, at, diagnostic);
            using var append = connection.CreateCommand(); append.Transaction = transaction;
            append.CommandText = "INSERT INTO work_events(id,payload) VALUES($id,$payload)"; append.Parameters.AddWithValue("$id", item.Id.ToString("D")); append.Parameters.AddWithValue("$payload", RelayJson.Serialize(item));
            await append.ExecuteNonQueryAsync(token);
        }
    }

    public Task RequestCancellationAsync(Guid id, CancellationToken token) =>
        WriteAsync(id, record =>
        {
            if (record is not { CancellationRequestedAt: null }) return null;
            var now = DateTimeOffset.UtcNow;
            // BeginNativeAttemptAsync uses the same transaction boundary. A claim alone never needs a terminal signal.
            if (record.State == "retry_wait" || record is { State: "running", Retry: not null, NativeAttempts: [.., { FinishedAt: not null }] })
                return record with { State = "failed", Retry = null, CancellationRequestedAt = now, UpdatedAt = now,
                    ErrorCode = WorkExecution.RetryCancelled, Error = "The scheduled retry was cancelled before dispatch." };
            return (record.State is "pending" or "running" or "awaiting_approval" or "awaiting_reply" ||
                record is { State: "unknown", ReplyDispatch: not null })
                ? record with { CancellationRequestedAt = now } : null;
        }, token);

    public async Task<bool> SessionHasDeferredWorkAsync(Guid id, CancellationToken token, bool includeReplies = false)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        var pending = includeReplies ? $"({DeferredState} OR {ReplyObservationState})" : DeferredState;
        command.CommandText = $"SELECT 1 FROM work WHERE {pending} AND json_extract(payload,'$.request.sessionId')=$id LIMIT 1";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync(token) is not null;
    }

    public async Task<IReadOnlyList<WorkRecord>> DueRetriesAsync(DateTimeOffset now, CancellationToken token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM work WHERE {DeferredState} AND {RetryDue}<=julianday($now) ORDER BY {RetryDue},rowid LIMIT 16";
        command.Parameters.AddWithValue("$now", now.ToString("O"));
        using var reader = await command.ExecuteReaderAsync(token);
        var results = new List<WorkRecord>();
        while (await reader.ReadAsync(token)) results.Add(RelayJson.Deserialize<WorkRecord>(reader.GetString(0)));
        return results;
    }

    public Task<bool> ClaimRetryAsync(Guid id, DateTimeOffset now, CancellationToken token) =>
        WriteAsync(id, record => record is { State: "retry_wait", Retry: { } retry, CancellationRequestedAt: null } && retry.DueAt <= now
            ? record with { State = "running", Error = null, ErrorCode = null, UpdatedAt = now } : null, token);

    public async Task<IReadOnlyList<WorkRecord>> RetainedApprovalsAsync(Guid sessionId, CancellationToken token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = RetainedApprovalsQuery;
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        using var reader = await command.ExecuteReaderAsync(token);
        var records = new List<WorkRecord>();
        while (await reader.ReadAsync(token)) records.Add(RelayJson.Deserialize<WorkRecord>(reader.GetString(0)));
        return records;
    }

    internal async Task ExpireDueApprovalsAsync(DateTimeOffset now, CancellationToken token)
    {
        var cutoff = now - ApprovalExpiry.WaitingLimit;
        var candidates = new List<Guid>();
        using (var connection = database.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT id FROM work WHERE json_extract(payload,'$.state')='awaiting_approval' " +
                $"AND julianday(json_extract(payload,'$.createdAt')) <= julianday({WaitingSince}) " +
                $"AND julianday({WaitingSince}) <= julianday($cutoff) ORDER BY julianday({WaitingSince}) LIMIT 64";
            command.Parameters.AddWithValue("$cutoff", cutoff.ToString("O"));
            using var reader = await command.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) candidates.Add(Guid.Parse(reader.GetString(0)));
        }
        foreach (var id in candidates)
            await WriteAsync(id, record =>
            {
                if (record is not { State: "awaiting_approval" }) return null;
                var since = ApprovalExpiry.StartedAt(record);
                if (since == default || since < record.CreatedAt || since > cutoff) return null;
                return record with
                {
                    State = "approval_expired", ErrorCode = "approval_expired",
                    Error = "The approval deadline passed. Prior effects may remain. Inspect or explicitly close the session.",
                    ApprovalWaitingSince = since, ApprovalExpiredAt = since + ApprovalExpiry.WaitingLimit, UpdatedAt = now
                };
            }, token);
    }

    public Task<bool> BeginApprovalStopAsync(Guid id, CancellationToken token) => WriteAsync(id, record =>
    {
        if (record is null || record is not { State: "awaiting_approval", CancellationRequestedAt: not null } &&
            record is not { State: "approval_expired", CancellationConfirmedAt: null, ApprovalExpiredAt: not null } &&
            !(record is { State: "unknown", SessionClosedAt: null } && WorkExecution.NeedsApprovalObservation(record)) &&
            !WorkExecution.ApprovalStopPending(record)) return null;
        return record with { ApprovalStopRequestedAt = record.ApprovalStopRequestedAt ?? DateTimeOffset.UtcNow };
    }, token);

    public Task ClearApprovalStopAsync(Guid id, CancellationToken token) => WriteAsync(id, record =>
        record is not null && WorkExecution.ApprovalStopPending(record) ? record with { ApprovalStopRequestedAt = null } : null, token);

    public Task RecordApprovalInterruptionAsync(Guid id, CancellationToken token) => WriteAsync(id, record =>
    {
        if (record is not { State: "awaiting_approval" } || WorkExecution.ApprovalStopPending(record)) return null;
        var now = DateTimeOffset.UtcNow;
        return record with
        {
            State = "unknown",
            ErrorCode = "terminal_interruption_unconfirmed",
            Error = "An interruption or refusal was sent to the terminal. Inspect the request before reusing the session.",
            CancellationRequestedAt = record.CancellationRequestedAt ?? now,
            UpdatedAt = now
        };
    }, token);

    public Task RecordApprovalSignalAsync(Guid id, string action, CancellationToken token) =>
        WriteTransitionAsync(id, record =>
        {
            if (record is null) return null;
            var now = DateTimeOffset.UtcNow;
            return action switch
            {
                "refuse" when record.TerminalRefusalSentAt is null => new(record with { TerminalRefusalSentAt = now },
                    Event: "terminal_refusal_sent", EventAt: now),
                "escape" when record.TerminalEscapeSentAt is null => new(record with { TerminalEscapeSentAt = now },
                    Event: "terminal_escape_sent", EventAt: now),
                _ => null
            };
        }, token);

    internal Task<bool> BeginNativeAttemptAsync(Guid id, int number, CancellationToken token, NativeReplyDispatch? dispatch = null) =>
        WriteTransitionAsync(id, record =>
        {
            if (record is not { State: "running", CancellationRequestedAt: null } ||
                number != (record.NativeAttempts?.Length ?? 0) + 1) return null;
            var now = DateTimeOffset.UtcNow;
            return new(record with { NativeAttempts = [.. record.NativeAttempts ?? [], new(number, now)], ReplyDispatch = dispatch },
                Event: "native_attempt_started", EventAt: now);
        }, token);

    internal async Task<bool> FinishNativeAttemptAsync(Guid id, int number, bool? dispatched, string? errorCode,
        NativeRetry? retry, CancellationToken token, RelayException? observation = null)
    {
        var parked = false;
        await WriteTransitionAsync(id, record =>
        {
            if (record?.NativeAttempts is not { Length: > 0 } attempts || attempts[^1].Number != number ||
                attempts[^1].FinishedAt is not null) return null;
            var now = DateTimeOffset.UtcNow;
            if (observation is not null && !IsAwaitingReply(record)) return null;
            parked = retry is not null && record.CancellationRequestedAt is null &&
                (observation is not null || record.State == "running");
            var state = parked ? "retry_wait" : observation?.Code switch
            {
                null => record.State,
                "native_reply_missing" or "session_closed_before_reply" when record.State == "approval_expired" => "approval_expired",
                "session_closed_before_reply" or "native_reply_missing" => "failed",
                _ => observation.EffectsPossible == false ? "failed" : "unknown"
            };
            var completed = attempts[^1] with { FinishedAt = now, Dispatched = dispatched, ErrorCode = errorCode, RetryAt = parked ? retry!.DueAt : null };
            return new(record with { NativeAttempts = [.. attempts[..^1], completed], Retry = parked ? retry : null, ReplyDispatch = null,
                    State = state, ErrorCode = state == "approval_expired" ? record.ErrorCode : parked || observation is not null ? errorCode : record.ErrorCode,
                    Error = state == "approval_expired" ? record.Error : observation?.Message ?? record.Error,
                    ApprovalWaitingSince = state == "awaiting_approval" ? now : record.ApprovalWaitingSince,
                    NativeTurnEnded = observation?.NativeTurnEnded == true || record.NativeTurnEnded, UpdatedAt = now },
                Diagnostic: errorCode, Event: parked ? "native_retry_scheduled" : "native_attempt_finished", EventAt: now);
        }, token);
        return parked;
    }

    internal Task<bool> MarkAwaitingReplyAsync(Guid id, CancellationToken token) =>
        WriteAsync(id, record => record is { State: "running", ReplyDispatch: not null }
            ? record with { State = "awaiting_reply", Retry = null, UpdatedAt = DateTimeOffset.UtcNow } : null, token);

    internal static bool IsAwaitingReply(WorkRecord record) =>
        record.ReplyDispatch is not null && record.State is "awaiting_reply" or "unknown" or "awaiting_approval" or "approval_expired";

    internal Task RecordReplyApprovalAsync(Guid id, CancellationToken token) =>
        WriteAsync(id, record => record is { State: "awaiting_reply" or "unknown", ReplyDispatch: not null }
            ? record with { State = "awaiting_approval", ApprovalWaitingSince = DateTimeOffset.UtcNow,
                ErrorCode = "approval_required", Error = "Native task needs human approval.", UpdatedAt = DateTimeOffset.UtcNow }
            : null, token);

    internal async Task<IReadOnlyList<(long Cursor, WorkRecord Record)>> AwaitingRepliesAsync(long after, CancellationToken token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT rowid,payload FROM work WHERE rowid>$after AND {ReplyObservationState} ORDER BY rowid LIMIT 16";
        command.Parameters.AddWithValue("$after", after);
        using var reader = await command.ExecuteReaderAsync(token);
        var results = new List<(long, WorkRecord)>();
        while (await reader.ReadAsync(token)) results.Add((reader.GetInt64(0), RelayJson.Deserialize<WorkRecord>(reader.GetString(1))));
        return results;
    }

    internal Task RecordApprovalTurnObservationAsync(Guid id, bool ready, CancellationToken token) =>
        WriteTransitionAsync(id, record =>
        {
            if (record is null || !WorkExecution.NeedsApprovalObservation(record)) return null;
            var canResume = ready && record.SessionClosedAt is null;
            if (!canResume && record.NativeTurnEnded) return null;
            var now = DateTimeOffset.UtcNow;
            return new(record with { NativeTurnEnded = true, ApprovalResumeVerifiedAt = canResume ? now : null },
                Event: canResume ? "approval_resume_verified" : "native_turn_ended", EventAt: now);
        }, token);

    public Task RecordApprovalSessionClosureAsync(Guid id, bool stopped, CancellationToken token) => WriteAsync(id, record =>
    {
        if (record is null || !WorkExecution.ApprovalStopPending(record) || !stopped && record.SessionClosedAt is not null) return null;
        var now = DateTimeOffset.UtcNow;
        record = record with { SessionClosedAt = record.SessionClosedAt ?? now, CancellationConfirmedAt = stopped ? now : null,
            ReplyDispatch = stopped ? null : record.ReplyDispatch };
        if (record.State is "completed" or "failed")
            return record;
        if (record.State == "approval_expired")
            return record with { UpdatedAt = now };
        return record with
        {
            State = "unknown",
            ErrorCode = stopped ? "cancelled_effects_unconfirmed" : "session_closed_stop_unconfirmed",
            Error = stopped ? "Native continuation was stopped. Earlier effects were not confirmed." :
                "The session is closed. Process termination and earlier effects remain unconfirmed.",
            UpdatedAt = now
        };
    }, token);

    public Task AttachNativeReplyAsync(Guid id, OperationScope scope, Guid sessionId, WorkResult result, DateTimeOffset receivedAt, CancellationToken token) =>
        WriteTransitionAsync(id, record =>
        {
            if (record is null || record.Request.Scope != scope || record.Request.SessionId != sessionId)
                throw new InvalidDataException("Native reply does not belong to the selected task/project.");
            if (record.State is not ("unknown" or "awaiting_approval" or "approval_expired" or "retry_wait" or "awaiting_reply") &&
                record.ErrorCode is not ("session_closed_before_reply" or "native_reply_missing")) return null;
            var observedAt = DateTimeOffset.UtcNow;
            var attempts = record.NativeAttempts;
            if (record.ReplyDispatch is not null && attempts is [.., { FinishedAt: null } sent])
                attempts = [.. attempts[..^1], sent with { FinishedAt = observedAt, Dispatched = true }];
            var completed = record with { State = "completed", Result = result, Error = null, ErrorCode = null, Retry = null,
                ReplyDispatch = null, NativeAttempts = attempts, UpdatedAt = observedAt };
            if (record.State is not ("awaiting_approval" or "approval_expired")) return new(completed);
            var started = ApprovalExpiry.StartedAt(record);
            if (record.CreatedAt == default || started < record.CreatedAt || started > observedAt ||
                started > DateTimeOffset.MaxValue - ApprovalExpiry.WaitingLimit ||
                receivedAt < record.CreatedAt || receivedAt > observedAt)
                return new(completed, Diagnostic: "approval_receipt_time_unverified");
            completed = completed with { ApprovalWaitingSince = started };
            var deadline = record.ApprovalExpiredAt ?? started + ApprovalExpiry.WaitingLimit;
            if (receivedAt < deadline)
                return record.ApprovalExpiredAt is null ? new(completed) :
                    new(completed, ClearExpiry: true, Diagnostic: "approval_receipt_before_deadline");
            return new(completed with { ApprovalExpiredAt = deadline },
                JournalExpiry: record.ApprovalExpiredAt is null ? deadline : null);
        }, token);

    public async Task<IReadOnlyList<WorkRecord>> ListAsync(CancellationToken token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM work ORDER BY rowid DESC LIMIT 200";
        using var reader = await command.ExecuteReaderAsync(token);
        var records = new List<WorkRecord>();
        while (await reader.ReadAsync(token)) records.Add(RelayJson.Deserialize<WorkRecord>(reader.GetString(0)));
        return records;
    }

    public async Task RecoverAsync(CancellationToken token)
    {
        while (true)
        {
            var unfinished = new List<WorkRecord>();
            using (var connection = database.Open())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = $"SELECT payload FROM work WHERE {ActiveState} ORDER BY rowid LIMIT 128";
                using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) unfinished.Add(RelayJson.Deserialize<WorkRecord>(reader.GetString(0)));
            }
            if (unfinished.Count == 0) return;
            foreach (var record in unfinished)
            {
                var beforeDispatch = record.State == "pending" && record.DispatchBoundaryTracked;
                await SaveAsync(record with
                {
                    State = beforeDispatch ? "failed" : "unknown",
                    Error = beforeDispatch ? "The host stopped before dispatch began." : "The host stopped before completion was confirmed. No automatic replay.",
                    ErrorCode = beforeDispatch ? "host_stopped_before_dispatch" : "host_stopped",
                    Retry = null,
                    UpdatedAt = DateTimeOffset.UtcNow
                }, token);
            }
        }
    }

    public async Task<IReadOnlyList<WorkRecord>> UncertainCommandsAsync(CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM work WHERE json_extract(payload,'$.state')='unknown' AND json_extract(payload,'$.request.kind')='command'";
        using var reader = await command.ExecuteReaderAsync(token); var records = new List<WorkRecord>();
        while (await reader.ReadAsync(token)) records.Add(RelayJson.Deserialize<WorkRecord>(reader.GetString(0)));
        return records;
    }

    public async Task AddInboxAsync(InboxMessage message, CancellationToken token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT OR IGNORE INTO inbox VALUES($id,$payload)";
        command.Parameters.AddWithValue("$id", message.Id.ToString("D"));
        command.Parameters.AddWithValue("$payload", RelayJson.Serialize(message));
        await command.ExecuteNonQueryAsync(token);
    }

    internal async Task<InboxMessage?> FindBoundReplyAsync(WorkRequest request, CancellationToken token)
    {
        if (request.Scope is not { } scope || request.SessionId is not { } sessionId) return null;
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT payload FROM inbox WHERE json_extract(payload,'$.inReplyTo')=$request
            AND json_extract(payload,'$.sessionId')=$session AND json_extract(payload,'$.projectId')=$project
            AND json_extract(payload,'$.senderEndpointId')=$sender AND json_extract(payload,'$.recipientEndpointId')=$recipient
            ORDER BY rowid LIMIT 1
            """;
        command.Parameters.AddWithValue("$request", request.Id.ToString("D"));
        command.Parameters.AddWithValue("$session", sessionId.ToString("D"));
        command.Parameters.AddWithValue("$project", scope.ProjectId.ToString("D"));
        command.Parameters.AddWithValue("$sender", scope.RecipientId.ToString("D"));
        command.Parameters.AddWithValue("$recipient", scope.SenderId.ToString("D"));
        return await command.ExecuteScalarAsync(token) is string payload ? RelayJson.Deserialize<InboxMessage>(payload) : null;
    }

    public async Task<IReadOnlyList<InboxMessage>> InboxAsync(CancellationToken token)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM inbox ORDER BY rowid DESC LIMIT 200";
        using var reader = await command.ExecuteReaderAsync(token);
        var messages = new List<InboxMessage>();
        while (await reader.ReadAsync(token)) messages.Add(RelayJson.Deserialize<InboxMessage>(reader.GetString(0)));
        return messages;
    }
}
