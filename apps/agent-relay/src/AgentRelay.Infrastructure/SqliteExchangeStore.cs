using AgentRelay.Core;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Infrastructure;

public sealed class SqliteExchangeStore : IExchangeStore
{
    private readonly RelayDatabase database;

    public SqliteExchangeStore(string dataDirectory)
    {
        database = new RelayDatabase(dataDirectory);
    }

    public async Task<RelayExchange?> FindAsync(Guid requestId, CancellationToken cancellationToken)
    {
        using var connection = database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT payload FROM exchanges WHERE request_id=$id;";
        command.Parameters.AddWithValue("$id", requestId.ToString("D"));
        var payload = await command.ExecuteScalarAsync(cancellationToken);
        return payload is string json ? RelayJson.Deserialize<RelayExchange>(json) : null;
    }

    public async Task SaveAsync(RelayExchange exchange, CancellationToken cancellationToken)
    {
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO exchanges(request_id,payload) VALUES($id,$payload);";
        command.Parameters.AddWithValue("$id", exchange.Request.Id.ToString("D"));
        command.Parameters.AddWithValue("$payload", RelayJson.Serialize(exchange));
        await command.ExecuteNonQueryAsync(cancellationToken);
        transaction.Commit();
    }

}
