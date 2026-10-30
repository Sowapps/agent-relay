namespace AgentRelay.Infrastructure;

public sealed class SqliteDocuments<T>
{
    private readonly RelayDatabase database;
    private readonly string table;
    public SqliteDocuments(RelayDatabase database, string table)
    {
        if (table is not ("sessions" or "transfers" or "connections" or "cursors" or "dispatches")) throw new ArgumentException("Unsupported document collection.");
        this.database = database; this.table = table;
    }

    public async Task<T?> FindAsync(Guid id, CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {table} WHERE id=$id"; command.Parameters.AddWithValue("$id", id.ToString("D"));
        return await command.ExecuteScalarAsync(token) is string json ? RelayJson.Deserialize<T>(json) : default;
    }

    public async Task SaveAsync(Guid id, T value, CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {table}(id,payload) VALUES($id,$payload) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload";
        command.Parameters.AddWithValue("$id", id.ToString("D")); command.Parameters.AddWithValue("$payload", RelayJson.Serialize(value));
        await command.ExecuteNonQueryAsync(token);
    }

    public async Task<IReadOnlyList<T>> ListAsync(CancellationToken token)
    {
        using var connection = database.Open(); using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {table} ORDER BY rowid DESC";
        using var reader = await command.ExecuteReaderAsync(token); var values = new List<T>();
        while (await reader.ReadAsync(token)) values.Add(RelayJson.Deserialize<T>(reader.GetString(0)));
        return values;
    }
}
