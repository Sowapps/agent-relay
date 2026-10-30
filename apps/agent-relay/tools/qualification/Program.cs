using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

// Dependency gate only; this fixture is not the application's transport or store.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
var data = Path.GetFullPath(args.Single());
Directory.CreateDirectory(data);
var database = new SqliteConnectionStringBuilder { DataSource = Path.Combine(data, "gate.sqlite"), Pooling = false }.ToString();
using (var connection = new SqliteConnection(database))
{
    connection.Open();
    using var command = connection.CreateCommand();
    command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; CREATE TABLE IF NOT EXISTS probe (id TEXT PRIMARY KEY, body TEXT NOT NULL); INSERT OR REPLACE INTO probe VALUES ('gate', 'persisted');";
    command.ExecuteNonQuery();
}
string sqliteVersion;
using (var reopened = new SqliteConnection(database))
{
    reopened.Open();
    sqliteVersion = reopened.ServerVersion;
    using var command = reopened.CreateCommand();
    command.CommandText = "SELECT body FROM probe WHERE id='gate';";
    if ((string?)command.ExecuteScalar() != "persisted") throw new InvalidOperationException("SQLite reopen failed.");
}
var name = "agent-relay-gate-" + Guid.NewGuid().ToString("N");
await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance);
await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
await Task.WhenAll(server.WaitForConnectionAsync(timeout.Token), client.ConnectAsync(timeout.Token));
var payload = Encoding.UTF8.GetBytes("correlated-local-message");
var received = new byte[payload.Length];
await Task.WhenAll(client.WriteAsync(payload, timeout.Token).AsTask(), server.ReadExactlyAsync(received, timeout.Token).AsTask());
if (!received.SequenceEqual(payload)) throw new InvalidOperationException("Restricted named-pipe transfer failed.");
Console.WriteLine(JsonSerializer.Serialize(new { SqliteReopenPassed = true, CurrentUserPipePassed = true, SqliteVersion = sqliteVersion }));
