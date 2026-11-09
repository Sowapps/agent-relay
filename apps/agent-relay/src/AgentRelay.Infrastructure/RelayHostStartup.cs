using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AgentRelay.Core;
using Microsoft.Data.Sqlite;

namespace AgentRelay.Infrastructure;

/// <summary>Cold-starts the installed tray host before a scoped MCP operation can be admitted.</summary>
/// <seealso cref="RelayRuntime"/>
public sealed class RelayHostStartup(string data, Func<ProcessStartInfo> resolveHost)
{
    private const int ProbeMilliseconds = 250;
    private const int StartupMilliseconds = 8000;

    internal async Task<JsonElement> HandshakeAsync(RelayPipeClient client, Guid connection, string secret, CancellationToken token)
    {
        var request = new PipeRequest("mcp_handshake", AccessToken: secret);
        try { return await client.RequestRawAsync(request, token, ProbeMilliseconds); }
        catch (RelayHostNotListeningException) { }

        try
        {
            await ConnectionStore.DemandStartupAsync(data, connection, secret, token);
            PromotionAdmission.DemandStartupAllowed(data);
            using var admission = TryOpenStartup();
            using var started = admission is null ? null : StartIfStopped();
            // This second handshake is read-only. The original project operation has not been sent.
            return await client.RequestRawAsync(request, token, StartupMilliseconds);
        }
        catch (RelayHostNotListeningException)
        {
            PromotionAdmission.DemandStartupAllowed(data);
            throw Failure("The relay did not become ready before the startup deadline.");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or Win32Exception or JsonException or SqliteException)
        {
            McpDiagnostics.Record(data, error);
            throw Failure("The installed relay could not be started. Check its installation and data access.");
        }
    }

    private FileStream? TryOpenStartup()
    {
        RuntimePaths.DemandPrivatePath(Path.Combine(data, "startup.lock"));
        try { return RelayLocation.OpenExclusive(data, "startup.lock"); }
        catch (IOException error) when (RelayLocation.IsSharingConflict(error)) { return null; }
    }

    private Process? StartIfStopped()
    {
        // A manual or sign-in launch may already be acquiring the same tray ownership.
        RuntimePaths.DemandPrivatePath(Path.Combine(data, "host.lock"));
        try { using var ownership = RelayLocation.OpenHostOwnership(data); }
        catch (IOException error) when (RelayLocation.IsSharingConflict(error)) { return null; }

        PromotionAdmission.DemandStartupAllowed(data);
        var start = resolveHost();
        if (RuntimePaths.InstallationRoot() is null ||
            !Path.GetFileName(start.FileName).Equals("AgentRelay.Windows.exe", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(start.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw Failure("This MCP connection cannot launch its selected installation. Start the reviewed relay manually.");
        RuntimePaths.DemandPrivatePath(start.FileName);
        start.ArgumentList.Add("--on-demand");
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        var process = Process.Start(start) ?? throw Failure("The relay process could not be started.");
        // Never let a tray host keep the worker's MCP stdin/stdout handles open.
        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.ErrorDataReceived += (_, line) => { if (!string.IsNullOrEmpty(line.Data)) McpDiagnostics.RecordText(data, line.Data); };
        process.BeginErrorReadLine();
        return process;
    }

    private static RelayException Failure(string message) => new("relay_start_failed",
        message + " Use the documented local recovery command if authorized. Otherwise ask your Agent Relay maintainer or the user to start the stable installation.", false);
}
