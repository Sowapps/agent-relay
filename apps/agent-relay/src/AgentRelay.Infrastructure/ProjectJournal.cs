using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class ProjectJournal(RelayDatabase database) : IRelayJournal
{
    public Task<ReadPage<WorkRecord>> OperationsAsync(ReadQuery query, CancellationToken token) =>
        ReadAsync<WorkRecord>("work", query, null, token);
    public Task<ReadPage<InboxMessage>> MessagesAsync(ReadQuery query, Guid recipientId, CancellationToken token) =>
        ReadAsync<InboxMessage>("inbox", query, recipientId, token);
    public Task<ReadPage<RelaySession>> SessionsAsync(ReadQuery query, Guid ownerId, CancellationToken token, SessionMandate[]? mandates = null) =>
        ReadAsync<RelaySession>("sessions", query, ownerId, token, mandates: mandates);
    public Task<ReadPage<WorkEvent>> EventsAsync(ReadQuery query, CancellationToken token) => ReadAsync<WorkEvent>("work_events", query, null, token);

    // Local administrator observation only: does not poll an agent or advance its inbox cursor.
    internal Task<ReadPage<InboxMessage>> ObserveMessagesAsync(ReadQuery query, CancellationToken token) => ReadAsync<InboxMessage>("inbox", query, null, token, incrementalInbox: false);
    internal Task<ReadPage<RelaySession>> ObserveSessionsAsync(ReadQuery query, CancellationToken token) => ReadAsync<RelaySession>("sessions", query, null, token);

    private async Task<ReadPage<T>> ReadAsync<T>(string table, ReadQuery query, Guid? recipient, CancellationToken token,
        bool incrementalInbox = true, SessionMandate[]? mandates = null)
    {
        if (query.ProjectId == Guid.Empty || query.Limit is < 1 or > 100 || query.Cursor < 0 || query.Search?.Length > 200 || query.Since > query.Until)
            throw new RelayException("invalid_query", "Expected a project, limit1..100, nonnegative cursor and valid date/search bounds.");
        using var connection = database.Open(); using var command = connection.CreateCommand();
        var fields = table == "work" ? "request." : "";
        var scope = table == "work" ? "request.scope." : table == "sessions" ? "definition.scope." : "";
        var clauses = new List<string>();
        void Equal(string parameter, string field, object? value)
        {
            if (value is null) return;
            clauses.Add("json_extract(payload,'$." + field + "')=$" + parameter); command.Parameters.AddWithValue("$" + parameter, value);
        }
        Equal("project", scope + "projectId", query.ProjectId.ToString("D"));
        if (table == "sessions" && recipient is not null && mandates is { Length: > 0 })
        {
            clauses.Add("""
                (json_extract(payload,'$.definition.scope.senderId')=$recipient OR EXISTS (
                    SELECT 1 FROM json_each($mandates)
                    WHERE json_extract(value,'$.sessionId')=json_extract(payload,'$.id')
                    AND json_extract(value,'$.ownerId')=json_extract(payload,'$.definition.scope.senderId')))
                """);
            command.Parameters.AddWithValue("$recipient", recipient.Value.ToString("D"));
            command.Parameters.AddWithValue("$mandates", RelayJson.Serialize(mandates));
        }
        else Equal("recipient", table == "sessions" ? "definition.scope.senderId" : "recipientEndpointId", recipient?.ToString("D"));
        Equal("endpoint", table == "work" ? "request.scope.recipientId" : table == "sessions" ? "definition.scope.recipientId" : table == "work_events" ? "endpointId" : "senderEndpointId", query.EndpointId?.ToString("D"));
        Equal("session", table == "sessions" ? "id" : fields + "sessionId", query.SessionId?.ToString("D"));
        if (table == "work")
        {
            if (!query.IncludeDeleted) clauses.Add("coalesce(json_extract(payload,'$.administration.disposition'),'active') <> 'deleted'");
            Equal("task", "request.taskId", query.TaskId?.ToString("D")); Equal("kind", "request.kind", query.Kind); Equal("state", "state", query.State);
        }
        if (table == "sessions") Equal("state", "state", query.State);
        if (table == "work_events") { Equal("task", "taskId", query.TaskId?.ToString("D")); Equal("state", "state", query.State); }
        var date = table is "work" or "sessions" ? "createdAt" : table == "work_events" ? "occurredAt" : "receivedAt";
        if (query.Since is { } since) { clauses.Add("julianday(json_extract(payload,'$." + date + "'))>=julianday($since)"); command.Parameters.AddWithValue("$since", since.ToUniversalTime().ToString("O")); }
        if (query.Until is { } until) { clauses.Add("julianday(json_extract(payload,'$." + date + "'))<=julianday($until)"); command.Parameters.AddWithValue("$until", until.ToUniversalTime().ToString("O")); }
        if (query.Search is { Length: > 0 } search)
        {
            clauses.Add(table == "work" ? "instr(lower(coalesce(json_extract(payload,'$.metadata.label'),'')||char(10)||coalesce(json_extract(payload,'$.request.text'),'')||char(10)||coalesce(json_extract(payload,'$.result.output'),'')),lower($search))>0" : table == "sessions" ? "instr(lower(json_extract(payload,'$.name')),lower($search))>0" : "instr(lower(json_extract(payload,'$.text')),lower($search))>0");
            command.Parameters.AddWithValue("$search", search);
        }
        var incremental = table == "inbox" && incrementalInbox;
        if (query.Cursor is { } cursor) { clauses.Add("rowid" + (incremental ? ">" : "<") + "$cursor"); command.Parameters.AddWithValue("$cursor", cursor); }
        command.CommandText = "SELECT rowid,payload FROM " + table + " WHERE " + string.Join(" AND ", clauses) + " ORDER BY rowid " + (incremental ? "ASC" : "DESC") + " LIMIT $limit";
        command.Parameters.AddWithValue("$limit", query.Limit + 1);
        using var reader = await command.ExecuteReaderAsync(token); var items = new List<T>(); long? last = null; var more = false; var payloadBytes = 0;
        while (await reader.ReadAsync(token))
        {
            if (items.Count == query.Limit) { more = true; break; }
            var item = RelayJson.Deserialize<T>(reader.GetString(1));
            if (item is WorkRecord record)
            {
                var preview = record with { Request = record.Request with { Text = record.Request.Text[..Math.Min(140, record.Request.Text.Length)] },
                    Result = record.Result is null ? null : record.Result with { Output = record.Result.Output[..Math.Min(1000, record.Result.Output.Length)], ErrorOutput = "", Details = new Dictionary<string, string> { ["previewOnly"] = "true" } },
                    Error = record.Error?[..Math.Min(300, record.Error.Length)] };
                item = (T)(object)preview;
            }
            var size = System.Text.Encoding.UTF8.GetByteCount(RelayJson.Serialize(item));
            if (items.Count > 0 && payloadBytes + size > 400 * 1024) { more = true; break; }
            if (size > 400 * 1024) throw new RelayException("result_too_large", "A retained record exceeds the bounded page budget; use its exact operation/artifact reference.");
            payloadBytes += size; items.Add(item); last = reader.GetInt64(0);
        }
        return new(items, incremental ? last ?? query.Cursor : more ? last : null, more);
    }
}
