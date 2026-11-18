using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class SqliteWorkStore(RelayDatabase database) : IWorkStore
{
    private const string ActiveState = "json_extract(payload,'$.state') IN ('pending','running')";
    internal const string PendingApprovalStop = "json_extract(payload,'$.approvalStopRequestedAt') IS NOT NULL AND json_extract(payload,'$.cancellationConfirmedAt') IS NULL";
    internal const string RetainedApproval = "json_extract(payload,'$.state')='awaiting_approval' OR (" +
        "json_extract(payload,'$.state')='approval_expired' AND json_extract(payload,'$.cancellationConfirmedAt') IS NULL) OR (" + PendingApprovalStop + ")";
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
        Func<WorkRecord, bool>? historicalApproval = null) =>
        DemandMaintenanceIdleAsync(dataDirectory, false, true, token, historyBefore, historicalApproval);

    private static async Task DemandMaintenanceIdleAsync(string dataDirectory, bool allowUninitialized, bool includeUncertain, CancellationToken token,
        DateTimeOffset? historyBefore = null, Func<WorkRecord, bool>? historicalApproval = null)
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
        var approval = $"({RetainedApproval}) OR ({LegacyApprovalStop})";
        // An approval needs the participant to resolve it. Report it before other blockers so that prepublication admission can reopen.
        command.CommandText = $"SELECT id,json_extract(payload,'$.state'),({approval}) AS approval,payload FROM work WHERE ({ActiveState} OR ({approval}){uncertain}) ORDER BY approval DESC";
        if (includeUncertain) command.Parameters.AddWithValue("$historyBefore", historyBefore is { } before ? before.ToString("O") : DBNull.Value);
        using var reader = await command.ExecuteReaderAsync(token);
        while (await reader.ReadAsync(token))
        {
            if (reader.GetString(1) is "awaiting_approval" or "approval_expired" && historicalApproval is not null && historyBefore is { } started &&
                RelayJson.Deserialize<WorkRecord>(reader.GetString(3)) is { ApprovalStopRequestedAt: null } retained &&
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
        WriteAsync(record.Request.Id, existing => existing is null ? record :
            record with { ApprovalStopRequestedAt = existing.ApprovalStopRequestedAt }, token);

    private sealed record Transition(WorkRecord Record, DateTimeOffset? JournalExpiry = null, bool ClearExpiry = false, string? Diagnostic = null);

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

    public async Task RequestCancellationAsync(Guid id, CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE work SET payload=json_set(payload,'$.cancellationRequestedAt',$at) WHERE id=$id AND ({ActiveState} OR json_extract(payload,'$.state')='awaiting_approval') AND json_extract(payload,'$.cancellationRequestedAt') IS NULL";
        command.Parameters.AddWithValue("$id", id.ToString("D")); command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); await command.ExecuteNonQueryAsync(token);
    }

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
            !WorkExecution.ApprovalStopPending(record)) return null;
        return record with { ApprovalStopRequestedAt = record.ApprovalStopRequestedAt ?? DateTimeOffset.UtcNow };
    }, token);

    public Task ClearApprovalStopAsync(Guid id, CancellationToken token) => WriteAsync(id, record =>
        record is not null && WorkExecution.ApprovalStopPending(record) ? record with { ApprovalStopRequestedAt = null } : null, token);

    public Task ConfirmApprovalCancellationAsync(Guid id, CancellationToken token) => WriteAsync(id, record =>
    {
        if (record is null || !WorkExecution.ApprovalStopPending(record)) return null;
        var now = DateTimeOffset.UtcNow;
        if (record.State is "completed" or "failed")
            return record with { CancellationConfirmedAt = now };
        if (record.State == "approval_expired")
            return record with { CancellationConfirmedAt = now, UpdatedAt = now };
        return record with
        {
            State = "unknown",
            ErrorCode = "cancelled_effects_unconfirmed",
            Error = "Native continuation was stopped. Earlier effects were not confirmed.",
            UpdatedAt = now, CancellationConfirmedAt = now
        };
    }, token);

    public Task AttachNativeReplyAsync(Guid id, OperationScope scope, Guid sessionId, WorkResult result, DateTimeOffset receivedAt, CancellationToken token) =>
        WriteTransitionAsync(id, record =>
        {
            if (record is null || record.Request.Scope != scope || record.Request.SessionId != sessionId)
                throw new InvalidDataException("Native reply does not belong to the selected task/project.");
            if (record.State is not ("unknown" or "awaiting_approval" or "approval_expired")) return null;
            var observedAt = DateTimeOffset.UtcNow;
            var completed = record with { State = "completed", Result = result, Error = null, ErrorCode = null, UpdatedAt = observedAt };
            if (record.State == "unknown") return new(completed);
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
