using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class SqliteWorkStore(RelayDatabase database) : IWorkStore
{
    private const string ActiveState = "json_extract(payload,'$.state') IN ('pending','running')";
    internal const string PendingApprovalStop = "json_extract(payload,'$.approvalStopRequestedAt') IS NOT NULL AND json_extract(payload,'$.cancellationConfirmedAt') IS NULL";
    internal const string RetainedApproval = "json_extract(payload,'$.state')='awaiting_approval' OR (" + PendingApprovalStop + ")";
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
            if (reader.GetString(1) == "awaiting_approval" && historicalApproval is not null && historyBefore is { } started &&
                RelayJson.Deserialize<WorkRecord>(reader.GetString(3)) is { ApprovalStopRequestedAt: null } retained &&
                retained.CreatedAt != default && retained.UpdatedAt != default && retained.CreatedAt <= retained.UpdatedAt &&
                retained.UpdatedAt < started && historicalApproval(retained)) continue;
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

    private async Task<bool> WriteAsync(Guid id, Func<WorkRecord?, WorkRecord?> change, CancellationToken token)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
        lookup.CommandText = "SELECT payload FROM work WHERE id=$id"; lookup.Parameters.AddWithValue("$id", id.ToString("D"));
        var existing = await lookup.ExecuteScalarAsync(token) is string previous ? RelayJson.Deserialize<WorkRecord>(previous) : null;
        var record = change(existing);
        if (record is null) return false;
        record = record with { CancellationRequestedAt = record.CancellationRequestedAt ?? existing?.CancellationRequestedAt,
            CancellationConfirmedAt = record.CancellationConfirmedAt ?? existing?.CancellationConfirmedAt,
            Progress = record.Progress ?? existing?.Progress };
        if (record.State == "completed" && record.Progress is { } progress) record = record with { Progress = progress with { Phase = "completed", Bytes = progress.TotalBytes } };
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO work VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        command.Parameters.AddWithValue("$id", record.Request.Id.ToString("D"));
        command.Parameters.AddWithValue("$payload", RelayJson.Serialize(record));
        await command.ExecuteNonQueryAsync(token);
        if (existing?.State != record.State)
        {
            var scope = record.Request.Scope;
            var item = new WorkEvent(Guid.NewGuid(), record.Request.Id, scope?.ProjectId, record.Request.TaskId, record.Request.AttemptId,
                record.Request.SessionId, scope?.RecipientId, record.State, record.UpdatedAt, record.ErrorCode);
            using var append = connection.CreateCommand(); append.Transaction = transaction;
            append.CommandText = "INSERT INTO work_events(id,payload) VALUES($id,$payload)"; append.Parameters.AddWithValue("$id", item.Id.ToString("D")); append.Parameters.AddWithValue("$payload", RelayJson.Serialize(item));
            await append.ExecuteNonQueryAsync(token);
        }
        transaction.Commit();
        return true;
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
        command.CommandText = $"SELECT payload FROM work WHERE json_extract(payload,'$.request.sessionId')=$id AND ({RetainedApproval}) LIMIT 2";
        command.Parameters.AddWithValue("$id", sessionId.ToString("D"));
        using var reader = await command.ExecuteReaderAsync(token);
        var records = new List<WorkRecord>();
        while (await reader.ReadAsync(token)) records.Add(RelayJson.Deserialize<WorkRecord>(reader.GetString(0)));
        return records;
    }

    public Task<bool> BeginApprovalStopAsync(Guid id, CancellationToken token) => WriteAsync(id, record =>
    {
        if (record is null || record is not { State: "awaiting_approval", CancellationRequestedAt: not null } &&
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
        return record with
        {
            State = "unknown",
            ErrorCode = "cancelled_effects_unconfirmed",
            Error = "Native continuation was stopped. Earlier effects were not confirmed.",
            UpdatedAt = now, CancellationConfirmedAt = now
        };
    }, token);

    public Task AttachNativeReplyAsync(Guid id, OperationScope scope, Guid sessionId, WorkResult result, CancellationToken token) =>
        WriteAsync(id, record =>
        {
            if (record is null || record.Request.Scope != scope || record.Request.SessionId != sessionId)
                throw new InvalidDataException("Native reply does not belong to the selected task/project.");
            return record.State is "unknown" or "awaiting_approval"
                ? record with { State = "completed", Result = result, Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow }
                : null;
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
