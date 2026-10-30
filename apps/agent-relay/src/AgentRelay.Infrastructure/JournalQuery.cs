using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record JournalQuery(string? ProfileId = null, Guid? SessionId = null, string? Kind = null,
    string? State = null, string? Search = null, long? Cursor = null, int Limit = 50);
public sealed record JournalPage<T>(IReadOnlyList<T> Items, long? NextCursor, bool HasMore);

public static class JournalReader
{
    public static async Task<JournalPage<T>> ReadAsync<T>(RelayDatabase database, string table, JournalQuery query,
        CancellationToken token, bool incremental = false)
    {
        if (table is not ("work" or "inbox" or "sessions")) throw new ArgumentException("Unsupported journal.");
        if (query.Limit is < 1 or > 100 || query.Cursor < 0 || query.Search?.Length > 200)
            throw new RelayException("invalid_query", "Limit must be1..100; cursor nonnegative; search at most200 characters.");
        using var connection = database.Open(); using var command = connection.CreateCommand();
        var clauses = new List<string>();
        void Filter(string name, string field, object? value)
        {
            if (value is null) return;
            clauses.Add("json_extract(payload,'$." + field + "')=$" + name);
            command.Parameters.AddWithValue("$" + name, value);
        }
        var prefix = table == "work" ? "request." : "";
        Filter("profile", prefix + "profileId", query.ProfileId);
        Filter("session", table == "sessions" ? "id" : prefix + "sessionId", query.SessionId?.ToString("D"));
        Filter("kind", prefix + "kind", query.Kind); Filter("state", "state", query.State);
        if (query.Cursor is { } cursor)
        { clauses.Add("rowid" + (incremental ? ">" : "<") + "$cursor"); command.Parameters.AddWithValue("$cursor", cursor); }
        if (!string.IsNullOrEmpty(query.Search))
        { clauses.Add("instr(lower(payload),lower($search))>0"); command.Parameters.AddWithValue("$search", query.Search); }
        command.Parameters.AddWithValue("$limit", query.Limit + 1);
        command.CommandText = "SELECT rowid,payload FROM " + table + (clauses.Count == 0 ? "" : " WHERE " + string.Join(" AND ", clauses)) +
            " ORDER BY rowid " + (incremental ? "ASC" : "DESC") + " LIMIT $limit";
        using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<T>(); long? last = null; var more = false;
        while (await reader.ReadAsync(token))
        {
            if (rows.Count == query.Limit) { more = true; break; }
            last = reader.GetInt64(0); rows.Add(RelayJson.Deserialize<T>(reader.GetString(1)));
        }
        // Inbox cursors are caller-owned checkpoints, also returned when caught up.
        return new(rows, incremental ? last ?? query.Cursor : more ? last : null, more);
    }
}
