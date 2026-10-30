using System.Collections.Concurrent;
using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record AgentStatus(string ProfileId, string Client, string State, string Message, string Distribution,
    string User, string Workspace, bool ModelPromptSent = false, string? Details = null, string? NativeConversationId = null);
public sealed record ProfileContext(string Id, string Name, string Client, string Distribution, string User,
    string Workspace, bool AllowLocalWork, IReadOnlyList<CommandRule> Commands);

public sealed class AgentControl(SettingsStore settings, WslBridge bridge)
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> startup = new();

    public static ProfileContext Context(WslProfile profile) => new(profile.Id, profile.Name, profile.Client,
        profile.Distribution, profile.User, profile.Workspace ?? profile.Root + "/" + profile.Client + "/workspace", profile.AllowLocalWork, profile.Commands);

    private WslProfile Profile(string id)
    {
        var profile = settings.Load().Profiles.SingleOrDefault(item => item.Id == id)
            ?? throw new RelayException("not_found", "The configured profile does not exist.");
        if (profile.Client == "command") throw new RelayException("capability_denied", "This profile executes direct commands; it is not an agent session.");
        return profile;
    }

    public async Task<AgentStatus> StatusAsync(string id, CancellationToken token)
    {
        var profile = Profile(id);
        return await StatusAsync(profile, token);
    }

    public async Task<AgentStatus> StatusAsync(WslProfile profile, CancellationToken token)
    {
        try { return FromNative(profile, await bridge.CallAsync(profile, "status", new { }, token)); }
        catch (Exception error) when (error is not OperationCanceledException)
        { return Result(profile, "unavailable", "The WSL agent status could not be confirmed.", error.Message); }
    }

    public async Task<AgentStatus> StartAsync(string id, CancellationToken token)
    {
        var profile = Profile(id);
        return await StartAsync(profile, token);
    }

    public async Task<AgentStatus> StartAsync(WslProfile profile, CancellationToken token, bool resume = false)
    {
        var gate = startup.GetOrAdd(profile.Root + "/" + profile.Client, _ => new(1, 1));
        if (!await gate.WaitAsync(0, token)) return Result(profile, "starting", "This agent already has a startup request in progress.");
        try
        {
            try { await bridge.CallAsync(profile, "start", new { profile.AllowLocalWork, Resume = resume }, token); }
            catch (TimeoutException)
            {
                var observed = await StatusAsync(profile, token);
                return observed.State == "not_started" ? observed with { State = "unknown", Message = "Startup timed out; do not assume it was cancelled. Check status before retrying." } : observed;
            }
            return await StatusAsync(profile, token);
        }
        finally { gate.Release(); }
    }

    private static AgentStatus FromNative(WslProfile profile, JsonElement native)
    {
        var available = native.TryGetProperty("available", out var a) && a.GetBoolean();
        var initialized = native.TryGetProperty("mcpInitialized", out var m) && m.GetBoolean();
        var pane = native.TryGetProperty("pane", out var p) ? p.GetString() ?? "" : "";
        var disconnected = native.TryGetProperty("authenticated", out var auth) && auth.ValueKind == JsonValueKind.False;
        var authUnknown = native.TryGetProperty("authenticationChecked", out var checkedAuth) && checkedAuth.ValueKind == JsonValueKind.True && auth.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
        var approval = native.TryGetProperty("awaitingApproval", out var required) && required.ValueKind == JsonValueKind.True;
        var attention = new[] { "Do you trust", "Yes, I trust", "Sign in", "Log in", "Select the login", "Login expired", "Please run /login", "Not logged in", "Update now (runs", "Press enter to continue" }.Any(text => pane.Contains(text, StringComparison.OrdinalIgnoreCase));
        var result = !available ? Result(profile, "not_started", "The dedicated agent session is not running.") :
            disconnected ? Result(profile, "attention_required", "The native account is not connected. Reauthenticate that client before sending a model request.") :
            authUnknown ? Result(profile, "unknown", "Authentication could not be confirmed in this isolated relay environment; this does not prove the normal account login expired.") :
            approval ? Result(profile, "awaiting_approval", "The native task requires human approval; the relay never approves it automatically.") :
            attention ? Result(profile, "attention_required", "The native agent requires attention in its dedicated terminal.", pane) :
            !initialized ? Result(profile, "starting", "The agent session is running; its relay MCP is still initializing.") :
            Result(profile, "ready", "The native session and relay MCP are ready.");
        return result with { NativeConversationId = native.TryGetProperty("nativeConversationId", out var conversation) && conversation.ValueKind == JsonValueKind.String ? conversation.GetString() : null };
    }

    private static AgentStatus Result(WslProfile profile, string state, string message, string? details = null) =>
        new(profile.Id, profile.Client, state, message, profile.Distribution, profile.User, Context(profile).Workspace, Details: details);
}
