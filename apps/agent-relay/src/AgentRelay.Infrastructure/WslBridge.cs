using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class WslBridge
{
    public async Task<JsonElement> CallAsync(WslProfile profile, string action, object payload, CancellationToken token, TimeSpan? timeout = null)
    {
        var result = await LocalProcess.RunAsync("wsl.exe", ["-d", profile.Distribution, "-u", profile.User, "--exec", "/usr/bin/python3",
            profile.Root + "/install/relay_bridge.py", action, profile.Root, profile.Client], RelayJson.Serialize(payload),
            timeout ?? TimeSpan.FromSeconds(action == "message" ? 135 : action == "start" ? 60 : action.StartsWith("transfer_", StringComparison.Ordinal) ? 180 : 20), token);
        var text = result.Output;
        if (result.ExitCode != 0)
            {
                try { using var parsed = JsonDocument.Parse(text); if (parsed.RootElement.TryGetProperty("error", out var failure)) text = failure.GetString() ?? text; }
                catch (JsonException) { /* Preserve unstructured native diagnostics. */ }
                var code = text.Contains("approval_required:", StringComparison.Ordinal) ? "approval_required" : text.Contains("destination_exists:", StringComparison.Ordinal) ? "destination_exists" :
                    text.Contains("credential_reference_unavailable:", StringComparison.Ordinal) ? "credential_reference_unavailable" : text.Contains("authentication_required:", StringComparison.Ordinal) ? "agent_authentication_required" : text.Contains("unavailable:", StringComparison.Ordinal) ? "unavailable" :
                    text.Contains("command_denied:", StringComparison.Ordinal) ? "command_denied" : "wsl_error";
                throw new RelayException(code, string.IsNullOrWhiteSpace(text) ? result.Error[..Math.Min(2000, result.Error.Length)] : text[..Math.Min(4000, text.Length)]);
            }
            return RelayJson.Deserialize<JsonElement>(text);
    }
}

public sealed class WslWorkExecutor(SettingsStore settings, WslBridge bridge, SqliteWorkStore store,
    Func<WorkRequest, CancellationToken, Task<WslProfile>>? resolveProfile = null) : IWorkExecutor
{
    public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token)
    {
        var profile = resolveProfile is not null ? await resolveProfile(request, token) : settings.Load().Profiles.SingleOrDefault(item => item.Id == request.ProfileId)
            ?? throw new RelayException("unavailable", "The configured profile is missing.");
        if (request.Kind == "command")
        {
            var command = profile.Commands.SingleOrDefault(item => item.Id == request.CommandId)
                ?? throw new RelayException("command_denied", "The command is not configured.");
            var nonce = Guid.NewGuid().ToString("D");
            var result = await bridge.CallAsync(profile, "command", new { request.Id, request.CommandId, nonce, command }, token);
            if (result.GetProperty("requestId").GetGuid() != request.Id || result.GetProperty("nonce").GetString() != nonce ||
                result.GetProperty("user").GetString() != profile.User || result.GetProperty("workspace").GetString() != profile.Root + "/command/workspace")
                throw new InvalidDataException("WSL execution identity was not confirmed.");
            return CommandResult(result, command.Program, command.Arguments);
        }
        var response = await bridge.CallAsync(profile, "message", new { request.Id, request.Text, profile.AllowLocalWork }, token);
        await ImportMessagesAsync(profile, response, token, request.SessionId);
        var reply = response.GetProperty("reply");
        if (reply.GetProperty("inReplyTo").GetGuid() != request.Id || reply.GetProperty("sender").GetString() != profile.Client + "-wsl")
            throw new InvalidDataException("Agent reply correlation/origin was not confirmed.");
        return new(profile.Client + "-wsl", reply.GetProperty("text").GetString()!);
    }

    public async Task ImportMessagesAsync(WslProfile profile, JsonElement response, CancellationToken token, Guid? sessionId = null)
    {
        foreach (var item in response.GetProperty("messages").EnumerateArray())
        {
            var message = RelayJson.Deserialize<InboxMessage>(item.GetRawText()) with { ProfileId = profile.Id, SessionId = sessionId };
            if (message.Sender != profile.Client + "-wsl" || message.Recipient != "windows-pilot") throw new InvalidDataException("Unconfigured inbox origin.");
            await store.AddInboxAsync(message, token);
            if (message.InReplyTo is { } id && await store.FindAsync(id, token) is { State: "unknown", Request.Kind: "message" } pending && pending.Request.ProfileId == profile.Id && pending.Request.SessionId == sessionId)
                await store.SaveAsync(pending with { State = "completed", Result = new(message.Sender, message.Text), Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow }, token);
        }
    }

    private static WorkResult CommandResult(JsonElement result, string program, string[] arguments) =>
        new("wsl-command", result.GetProperty("stdout").GetString()!, result.GetProperty("stderr").GetString()!, result.GetProperty("exitCode").GetInt32(),
            new Dictionary<string, string> { ["program"] = program, ["arguments"] = RelayJson.Serialize(arguments),
                ["workspace"] = result.GetProperty("workspace").GetString()!, ["linuxUser"] = result.GetProperty("user").GetString()!,
                ["nonce"] = result.GetProperty("nonce").GetString()! });

    public async Task RecoverAsync(WslProfile profile, CancellationToken token, Guid? sessionId = null)
    {
        // Agent inbox import already reconciles every correlated message by id, beyond display pages.
        if (profile.Client != "command") return;
        foreach (var pending in (await store.UncertainCommandsAsync(token)).Where(item => item.Request.ProfileId == profile.Id && item.Request.SessionId == sessionId))
        {
            var nonce = Guid.NewGuid().ToString("D");
            var response = await bridge.CallAsync(profile, "recover", new { pending.Request.Id, nonce }, token);
            if (profile.Client != "command") { await ImportMessagesAsync(profile, response, token, sessionId); continue; }
            var receipt = response.GetProperty("receipt");
            if (receipt.ValueKind == JsonValueKind.Null) continue;
            if (response.GetProperty("recoveryNonce").GetString() != nonce || receipt.GetProperty("requestId").GetGuid() != pending.Request.Id ||
                receipt.GetProperty("workspace").GetString() != profile.Root + "/command/workspace" || receipt.GetProperty("user").GetString() != profile.User)
                throw new InvalidDataException("Recovered command identity does not match its configured workspace/user.");
            var result = CommandResult(receipt, receipt.TryGetProperty("program", out var program) ? program.GetString()! : "previous configured command",
                receipt.TryGetProperty("arguments", out var arguments) ? arguments.Deserialize<string[]>()! : []);
            await store.SaveAsync(pending with { State = result.ExitCode == 0 ? "completed" : "failed", Result = result, Error = null,
                UpdatedAt = DateTimeOffset.UtcNow }, token);
        }
    }
}
