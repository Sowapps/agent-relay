using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

/// <summary>Reserves all workspace resources together. Unconfirmed work retains the affected resources across host restart.</summary>
internal sealed class WorkspaceLease : IDisposable
{
    internal sealed record NativeIdentity(string Path, string Physical, string DeviceAndInode)
    {
        public string[] Resources => [Path, Physical];
    }
    private static readonly object Gate = new();
    private static readonly HashSet<(string Data, Guid Id)> Live = [];
    private readonly RelayDatabase database;
    private readonly Guid id;
    private bool disposed;

    private WorkspaceLease(RelayDatabase database, Guid id) => (this.database, this.id) = (database, id);

    public static WorkspaceLease Acquire(RelayDatabase database, Guid id, OperationScope scope, Guid? session, params string[] resources)
    {
        lock (Gate)
        {
            using var connection = database.Open();
            using var transaction = connection.BeginTransaction();
            foreach (var resource in resources.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                using var lookup = connection.CreateCommand();
                lookup.Transaction = transaction;
                lookup.CommandText = """
                    SELECT r.request_id, r.project_id, r.sender_id, r.session_id, json_extract(w.payload,'$.state'), json_extract(w.payload,'$.cancellationConfirmedAt')
                    FROM workspace_reservations r LEFT JOIN work w ON w.id=r.request_id WHERE r.resource=$resource
                    """;
                lookup.Parameters.AddWithValue("$resource", resource);
                using var owner = lookup.ExecuteReader();
                if (owner.Read())
                {
                    var requestId = Guid.Parse(owner.GetString(0));
                    var sameCaller = owner.GetString(1) == scope.ProjectId.ToString("D") && owner.GetString(2) == scope.SenderId.ToString("D");
                    var sessionId = owner.IsDBNull(3) ? null : owner.GetString(3);
                    var state = owner.IsDBNull(4) ? null : owner.GetString(4);
                    var confirmedStop = state == "unknown" && !owner.IsDBNull(5);
                    if (Live.Contains((database.DataDirectory, requestId)) || !confirmedStop && state is not (null or "completed" or "failed"))
                    {
                        var detail = sameCaller ? $" Holder request: {requestId:D}." + (sessionId is null ? "" : $" Session: {sessionId}.") : "";
                        var surface = scope.WorkspaceId is { } workspace ? $"Workspace {workspace:D}" : "The requested workspace surface";
                        throw new RelayException("workspace_busy", surface + " overlaps a reserved surface. Inspect the active or unconfirmed work before retrying." + detail, false);
                    }
                }
                owner.Close();
                using var put = connection.CreateCommand();
                put.Transaction = transaction;
                put.CommandText = "INSERT OR REPLACE INTO workspace_reservations(resource,request_id,project_id,sender_id,session_id) VALUES($resource,$request,$project,$sender,$session)";
                put.Parameters.AddWithValue("$resource", resource);
                put.Parameters.AddWithValue("$request", id.ToString("D"));
                put.Parameters.AddWithValue("$project", scope.ProjectId.ToString("D"));
                put.Parameters.AddWithValue("$sender", scope.SenderId.ToString("D"));
                put.Parameters.AddWithValue("$session", (object?)session?.ToString("D") ?? DBNull.Value);
                put.ExecuteNonQuery();
            }
            transaction.Commit();
            Live.Add((database.DataDirectory, id));
            return new(database, id);
        }
    }

    public static async Task<NativeIdentity> VerifyNativeAsync(RelayDatabase database, WslProfile profile, RelayWorkspace workspace, CancellationToken token)
    {
        var location = workspace.Location;
        var result = await LocalProcess.RunAsync("wsl.exe", ["-d", profile.Distribution, "-u", profile.User, "--exec",
            "/usr/bin/stat", "--dereference", "--format=%d:%i:%f", "--", location], "", TimeSpan.FromSeconds(10), token);
        var parts = result.Output.Trim().Split(':', 3);
        if (result.ExitCode != 0 || parts.Length != 3 || !ulong.TryParse(parts[0], out _) || !ulong.TryParse(parts[1], out _) ||
            !uint.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var mode) || (mode & 0xf000) != 0x4000)
            throw new RelayException("workspace_unavailable", "The assigned WSL workspace directory could not be verified.", false);
        var scope = "wsl:" + profile.Distribution.ToUpperInvariant() + ":";
        var stamp = parts[0] + ":" + parts[1];
        var identity = new NativeIdentity(scope + "path:" + location.TrimEnd('/'), scope + "directory:" + stamp, stamp);
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", workspace.Id.ToString("D"));
        command.Parameters.AddWithValue("$resource", identity.Physical);
        command.CommandText = "INSERT OR IGNORE INTO workspace_identities(workspace_id,resource) VALUES($id,$resource)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT resource FROM workspace_identities WHERE workspace_id=$id";
        if (command.ExecuteScalar() as string != identity.Physical)
            throw new RelayException("workspace_identity_changed", "The assigned path points to a different directory. Verify the workspace in project settings before issuing work.", false);
        transaction.Commit();
        return identity;
    }

    public static string[] Local(string location)
    {
        var path = Path.GetFullPath(location);
        if (!Directory.Exists(path)) throw new RelayException("workspace_unavailable", "The assigned local workspace directory is unavailable.", false);
        var root = Path.GetPathRoot(path)!;
        var directory = new DirectoryInfo(root);
        foreach (var segment in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            directory = new DirectoryInfo(Path.Combine(directory.FullName, segment));
            if (directory.ResolveLinkTarget(returnFinalTarget: true) is DirectoryInfo resolved) directory = resolved;
        }
        return ["local:" + Path.TrimEndingDirectorySeparator(path).ToUpperInvariant(),
            "local:" + Path.TrimEndingDirectorySeparator(directory.FullName).ToUpperInvariant()];
    }

    public void Dispose()
    {
        lock (Gate)
        {
            if (disposed) return;
            disposed = true;
            // Rows are reclaimed on next acquisition according to the retained work outcome, never by timeout.
            Live.Remove((database.DataDirectory, id));
        }
    }
}
