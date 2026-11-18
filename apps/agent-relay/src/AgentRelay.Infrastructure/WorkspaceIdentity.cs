using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

/// <summary>Verifies that an assigned WSL path still refers to the expected directory.</summary>
internal static class WorkspaceIdentity
{
    public static async Task<string> VerifyNativeAsync(RelayDatabase database, WslProfile profile, RelayWorkspace workspace, CancellationToken token)
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
        var identity = scope + "directory:" + stamp;
        using var connection = database.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.Parameters.AddWithValue("$id", workspace.Id.ToString("D"));
        command.Parameters.AddWithValue("$resource", identity);
        command.CommandText = "INSERT OR IGNORE INTO workspace_identities(workspace_id,resource) VALUES($id,$resource)";
        command.ExecuteNonQuery();
        command.CommandText = "SELECT resource FROM workspace_identities WHERE workspace_id=$id";
        if (command.ExecuteScalar() as string != identity)
            throw new RelayException("workspace_identity_changed", "The assigned path points to a different directory. Verify the workspace in project settings before issuing work.", false);
        transaction.Commit();
        return stamp;
    }
}
