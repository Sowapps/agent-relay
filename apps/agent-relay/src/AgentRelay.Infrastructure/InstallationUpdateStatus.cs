using System.Text.Json;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed record InstallationUpdateView(string? Id, string? Version, string Phase, bool Pending, string? Code,
    string? Message, DateTimeOffset? StartedAt, DateTimeOffset? UpdatedAt, double? ElapsedSeconds,
    InstallationRecoveryPresence? Continuation = null, JsonElement? LastCommand = null, string? DiagnosticId = null, string? TechnicalError = null,
    bool AdmissionSuspended = false);

/// <summary>Passive common projection for CLI/MCP/UI; no update, process launch or diagnostic mutation.</summary>
public static class InstallationUpdateStatus
{
    public static InstallationUpdateView? Read(string data)
    {
        string journal;
        try { journal = RuntimePaths.UpdateJournal(data); }
        catch (IOException) { return null; } // Nonportable development scopes have no portable update transaction.
        if (!File.Exists(journal)) return null;
        try
        {
            var state = RelayJson.Deserialize<InstallationUpdateState>(McpPrivateFiles.ReadText(journal));
            InstallationUpdate.ValidateHeader(state, RuntimePaths.PortableInstallationForData(data), Path.GetFullPath(data));
            var last = Path.Combine(RuntimePaths.UpdateDirectory(state.Installation), "last-command.json");
            JsonElement? command = File.Exists(last) ? JsonSerializer.Deserialize<JsonElement>(McpPrivateFiles.ReadText(last, 16384)) : null;
            var pending = state.Phase is not ("complete" or "rolled_back") && (state.Activation?.ResumeAuthorized == true || File.Exists(Path.Combine(data, "promotion.lock")));
            var continuation = InstallationUpdateRecovery.Presence(data);
            if (continuation?.TransactionId != state.Id) continuation = null;
            return new(state.Id, state.Version, state.Phase, pending, state.ErrorCode, state.Error, state.StartedAt, state.UpdatedAt,
                (DateTimeOffset.UtcNow - state.StartedAt).TotalSeconds, continuation, command,
                AdmissionSuspended: File.Exists(Path.Combine(data, "promotion.lock")));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException or AgentRelay.Core.RelayException)
        {
            var id = "update-status-" + McpStableProtocol.Hash(error.GetType().Name + error.Message)[..12];
            return new(null, null, "unverified", true, "update_status_unavailable", "Update state cannot be verified. Ask the relay maintainer to inspect the retained transaction.",
                null, null, null, DiagnosticId: id, TechnicalError: error.ToString()[..Math.Min(error.ToString().Length, 8000)]);
        }
    }
}
