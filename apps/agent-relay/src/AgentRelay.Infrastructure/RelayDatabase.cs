using Microsoft.Data.Sqlite;

namespace AgentRelay.Infrastructure;

public sealed class RelayDatabase
{
    public string DataDirectory { get; }
    private readonly string connectionString;
    private readonly bool readOnly;
    public RelayDatabase(string dataDirectory) : this(dataDirectory, readOnly: false) { }

    internal RelayDatabase(string dataDirectory, bool readOnly)
    {
        DataDirectory = Path.GetFullPath(dataDirectory);
        this.readOnly = readOnly;
        connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath(DataDirectory),
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 5 }.ToString();
        if (readOnly) return;
        Directory.CreateDirectory(dataDirectory);
        using var connection = Open();
        using var version = connection.CreateCommand();
        version.CommandText = "PRAGMA user_version;";
        if (Convert.ToInt32(version.ExecuteScalar()) is < 0 or > 4) throw new InvalidOperationException("Unsupported database schema.");
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS exchanges(request_id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS work(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS inbox(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS sessions(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS transfers(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS connections(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS cursors(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS work_events(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS dispatches(id TEXT PRIMARY KEY,payload TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS event_project_task ON work_events(json_extract(payload,'$.projectId'),json_extract(payload,'$.taskId'));
            CREATE INDEX IF NOT EXISTS work_project ON work(json_extract(payload,'$.request.scope.projectId'));
            CREATE INDEX IF NOT EXISTS inbox_project_recipient ON inbox(json_extract(payload,'$.projectId'),json_extract(payload,'$.recipientEndpointId'));
            PRAGMA user_version=4;
            """;
        command.ExecuteNonQuery();
        transaction.Commit();
    }

    internal static string DatabasePath(string dataDirectory)
    {
        var path = Path.GetFullPath(Path.Combine(dataDirectory, "relay.sqlite"));
        // Native SQLite also opens journal/WAL siblings. Explicit Windows paths avoid MAX_PATH at that boundary.
        if (!OperatingSystem.IsWindows() || path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        try
        {
            connection.Open();
            if (readOnly) return connection;
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
            command.ExecuteNonQuery();
            return connection;
        }
        catch { connection.Dispose(); throw; }
    }
}
