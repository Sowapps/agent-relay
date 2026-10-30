using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class SqliteWorkStore(RelayDatabase database) : IWorkStore
{
    public async Task ProgressAsync(Guid id, WorkProgress progress, CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE work SET payload=json_set(payload,'$.progress',json($progress),'$.updatedAt',$at) WHERE id=$id AND json_extract(payload,'$.state') IN ('pending','running')";
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

    public async Task SaveAsync(WorkRecord record, CancellationToken token)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var lookup = connection.CreateCommand(); lookup.Transaction = transaction;
        lookup.CommandText = "SELECT payload FROM work WHERE id=$id"; lookup.Parameters.AddWithValue("$id", record.Request.Id.ToString("D"));
        var existing = await lookup.ExecuteScalarAsync(token) is string previous ? RelayJson.Deserialize<WorkRecord>(previous) : null;
        record = record with { CancellationRequestedAt = record.CancellationRequestedAt ?? existing?.CancellationRequestedAt,
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
    }

    public async Task RequestCancellationAsync(Guid id, CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE work SET payload=json_set(payload,'$.cancellationRequestedAt',$at) WHERE id=$id AND json_extract(payload,'$.state') IN ('pending','running') AND json_extract(payload,'$.cancellationRequestedAt') IS NULL";
        command.Parameters.AddWithValue("$id", id.ToString("D")); command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); await command.ExecuteNonQueryAsync(token);
    }

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
                command.CommandText = "SELECT payload FROM work WHERE json_extract(payload,'$.state') IN ('running','pending') ORDER BY rowid LIMIT 128";
                using var reader = await command.ExecuteReaderAsync(token);
                while (await reader.ReadAsync(token)) unfinished.Add(RelayJson.Deserialize<WorkRecord>(reader.GetString(0)));
            }
            if (unfinished.Count == 0) return;
            foreach (var record in unfinished) await SaveAsync(record with { State = "unknown",
                Error = "The host stopped before completion was confirmed. No automatic replay.", ErrorCode = "host_stopped", UpdatedAt = DateTimeOffset.UtcNow }, token);
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
