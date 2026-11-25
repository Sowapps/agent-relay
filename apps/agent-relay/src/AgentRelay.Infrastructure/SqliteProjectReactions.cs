using System.Security.Cryptography;
using System.Text;
using AgentRelay.Core;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Infrastructure;

internal sealed class SqliteProjectReactions(RelayDatabase database) : IProjectReactionStore
{
    public async Task<ProjectReactions> ReadAsync(Guid project, CancellationToken token)
    {
        using var connection = database.Open();
        using var lookup = Command(connection, null, "SELECT payload FROM reaction_settings WHERE project_id=$project",
            ("$project", project.ToString("D")));
        return await lookup.ExecuteScalarAsync(token) is string json ? RelayJson.Deserialize<ProjectReactions>(json) : new(0, []);
    }

    public async Task<ProjectReactions> SaveAsync(Guid project, ProjectReactions replacement, CancellationToken token)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var id = project.ToString("D");
        using var lookup = Command(connection, transaction, "SELECT payload FROM reaction_settings WHERE project_id=$project", ("$project", id));
        var existing = await lookup.ExecuteScalarAsync(token) is string json ? RelayJson.Deserialize<ProjectReactions>(json) : new(0, []);
        if (existing.Revision != replacement.Revision)
            throw new RelayException("state_conflict", "Reaction settings changed. Read the current revision and try again.", false);
        // Retain already-received work under the old settings before switching to a new event boundary.
        await ScheduleAsync(connection, transaction, project, null, int.MaxValue, token);
        var stored = replacement with { Revision = existing.Revision + 1 };
        using var save = Command(connection, transaction, """
            INSERT INTO reaction_settings(project_id,payload,after_event)
            VALUES($project,$payload,(SELECT COALESCE(MAX(rowid),0) FROM work_events))
            ON CONFLICT(project_id) DO UPDATE SET payload=excluded.payload,after_event=excluded.after_event
            """, ("$project", id), ("$payload", RelayJson.Serialize(stored)));
        await save.ExecuteNonQueryAsync(token);
        transaction.Commit();
        return stored;
    }

    public async Task<IReadOnlyList<ReactionRun>> ScheduleAsync(Guid? project, Guid? source, CancellationToken token)
    {
        using var connection = database.Open();
        if (project is null)
        {
            using var enabled = Command(connection, null, """
                SELECT 1 FROM reaction_settings, json_each(json_extract(payload,'$.rules'))
                WHERE json_extract(value,'$.enabled')=1 LIMIT 1
                """);
            if (await enabled.ExecuteScalarAsync(token) is null) return [];
        }
        using var transaction = connection.BeginTransaction();
        await ScheduleAsync(connection, transaction, project, source, 16, token);
        var results = new List<ReactionRun>();
        if (project is { } id && source is { } request)
        {
            using var read = Command(connection, transaction, "SELECT payload FROM reaction_runs WHERE project_id=$project AND source_id=$source ORDER BY rule_id",
                ("$project", id.ToString("D")), ("$source", request.ToString("D")));
            using var reader = await read.ExecuteReaderAsync(token);
            while (await reader.ReadAsync(token)) results.Add(RelayJson.Deserialize<ReactionRun>(reader.GetString(0)));
        }
        transaction.Commit();
        return results;
    }

    private static async Task ScheduleAsync(SqliteConnection connection, SqliteTransaction transaction, Guid? project, Guid? source,
        int limit, CancellationToken token)
    {
        var configurations = new List<(Guid Id, ProjectReactions Settings, long After)>();
        using (var read = Command(connection, transaction, "SELECT project_id,payload,after_event FROM reaction_settings WHERE $project IS NULL OR project_id=$project",
                   ("$project", project?.ToString("D") ?? (object)DBNull.Value)))
        using (var reader = await read.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
                configurations.Add((Guid.Parse(reader.GetString(0)), RelayJson.Deserialize<ProjectReactions>(reader.GetString(1)), reader.GetInt64(2)));

        foreach (var config in configurations)
        foreach (var rule in config.Settings.Rules.Where(item => item.Enabled))
        {
            var candidates = new List<(Guid Source, Guid Session, DateTimeOffset Occurred)>();
            var bound = SqliteWorkStore.BoundReplyCondition("w.id", "json_extract(e.payload,'$.sessionId')", "$project",
                "json_extract(w.payload,'$.request.scope.recipientId')", "json_extract(w.payload,'$.request.scope.senderId')");
            using (var search = Command(connection, transaction, $"""
                SELECT json_extract(e.payload,'$.requestId'),json_extract(e.payload,'$.sessionId'),json_extract(e.payload,'$.occurredAt')
                FROM work_events e JOIN work w ON w.id=json_extract(e.payload,'$.requestId')
                WHERE json_extract(e.payload,'$.state')='completed' AND json_extract(e.payload,'$.projectId')=$project AND e.rowid>$after
                  AND json_extract(w.payload,'$.state')='completed' AND json_extract(w.payload,'$.request.kind')='message'
                  AND json_extract(w.payload,'$.request.sessionId') IS NOT NULL AND json_extract(w.payload,'$.result') IS NOT NULL
                  AND EXISTS(SELECT 1 FROM inbox i WHERE {bound})
                  AND ($source IS NULL OR w.id=$source) AND ($session IS NULL OR json_extract(e.payload,'$.sessionId')=$session)
                  AND NOT EXISTS(SELECT 1 FROM reaction_runs r WHERE r.project_id=$project AND r.rule_id=$rule AND r.source_id=w.id)
                ORDER BY e.rowid LIMIT $limit
                """, ("$project", config.Id.ToString("D")), ("$after", config.After),
                ("$source", source?.ToString("D") ?? (object)DBNull.Value), ("$session", rule.SessionId?.ToString("D") ?? (object)DBNull.Value),
                ("$rule", rule.Id.ToString("D")), ("$limit", limit)))
            using (var reader = await search.ExecuteReaderAsync(token))
                while (await reader.ReadAsync(token))
                    candidates.Add((Guid.Parse(reader.GetString(0)), Guid.Parse(reader.GetString(1)), DateTimeOffset.Parse(reader.GetString(2))));

            foreach (var candidate in candidates)
            {
                var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"project-reaction:{config.Id:D}:{rule.Id:D}:{candidate.Source:D}")).AsSpan(0, 16));
                var run = new ReactionRun(id, config.Id, candidate.Source, candidate.Session, rule, candidate.Occurred);
                using var insert = Command(connection, transaction, """
                    INSERT OR IGNORE INTO reaction_runs(id,project_id,source_id,rule_id,payload) VALUES($id,$project,$source,$rule,$payload)
                    """, ("$id", id.ToString("D")), ("$project", config.Id.ToString("D")), ("$source", candidate.Source.ToString("D")),
                    ("$rule", rule.Id.ToString("D")), ("$payload", RelayJson.Serialize(run)));
                await insert.ExecuteNonQueryAsync(token);
            }
        }
    }

    public async Task<IReadOnlyList<ReactionRun>> PendingAsync(CancellationToken token)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        var rows = new List<ReactionRun>();
        using (var read = Command(connection, transaction, """
            SELECT payload FROM reaction_runs WHERE json_extract(payload,'$.state') IN ('waiting_for_release','ready')
            ORDER BY last_checked,rowid LIMIT 16
            """))
        using (var reader = await read.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token)) rows.Add(RelayJson.Deserialize<ReactionRun>(reader.GetString(0)));
        foreach (var row in rows)
        {
            using var touch = Command(connection, transaction, "UPDATE reaction_runs SET last_checked=$now WHERE id=$id",
                ("$now", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()), ("$id", row.Id.ToString("D")));
            await touch.ExecuteNonQueryAsync(token);
        }
        transaction.Commit();
        return rows;
    }

    public async Task<bool> TransitionAsync(ReactionRun previous, ReactionRun next, CancellationToken token)
    {
        using var connection = database.Open();
        using var update = Command(connection, null, "UPDATE reaction_runs SET payload=$next WHERE id=$id AND payload=$previous",
            ("$next", RelayJson.Serialize(next)), ("$id", previous.Id.ToString("D")), ("$previous", RelayJson.Serialize(previous)));
        return await update.ExecuteNonQueryAsync(token) == 1;
    }

    private static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
