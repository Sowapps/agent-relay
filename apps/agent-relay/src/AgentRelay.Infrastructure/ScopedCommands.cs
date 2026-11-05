using System.Security.Cryptography;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record CommandDispatch(OperationScope Scope, WslProfile Profile, string Workspace, CommandRule Rule, string Nonce);

// Owns direct command preparation, dispatch context and matching receipt recovery.
public sealed class ScopedCommands(string data, SettingsStore technical, ProjectSettingsStore projects, WslBridge bridge,
    SqliteDocuments<CommandDispatch> dispatches)
{
    /// <seealso cref="RecoverAsync"/>
    public async Task<WorkResult> ExecuteAsync(CallerIdentity caller, WorkRequest request, CancellationToken token)
    {
        var scope = request.Scope!;
        var configuration = projects.Load();
        var policy = new ProjectPolicy(projects);
        var member = policy.Demand(caller, scope.ProjectId, RelayCapabilities.RunCommand);
        if (!(member.CommandIds ?? []).Contains(request.CommandId)) throw new RelayException("command_denied", "The command grant was revoked.");
        var workspace = policy.Workspace(caller, scope.ProjectId, scope.WorkspaceId!.Value, false, scope.RecipientId);
        var endpoint = configuration.Endpoints.Single(item => item.Id == scope.RecipientId);
        var binding = technical.Load().Profiles.SingleOrDefault(item => item.Id == endpoint.BindingId && item.Client == "command")
            ?? throw new RelayException("adapter_unavailable", "A configured direct-command adapter is required.");
        var command = binding.Commands.SingleOrDefault(item => item.Id == request.CommandId) ?? throw new RelayException("command_denied", "Command is not configured.");
        var key = new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("command-runtime:" + endpoint.Id + ":" + scope.ProjectId + ":" + workspace.Id + ":" + scope.SenderId)).AsSpan(0, 16));
        var definition = new SessionDefinition(key, endpoint.Name, scope, endpoint, workspace, false);
        var prepared = await WslProvisioner.PrepareSessionAsync(data, key, binding, token, definition);
        var profile = binding with { Root = prepared.Root, Workspace = workspace.Location };
        var dispatch = new CommandDispatch(scope, profile, workspace.Location, command, Guid.NewGuid().ToString("D"));
        using var lease = workspace.ConcurrentWrites ? null : WorkspaceLease.Acquire(WorkspaceLease.Native(profile, workspace.Location));
        await dispatches.SaveAsync(request.Id, dispatch, token);
        var result = await bridge.CallAsync(profile, "command", new { request.Id, request.CommandId, nonce = dispatch.Nonce, command, Workspace = workspace.Location }, token);
        return CommandResult(request, dispatch, result);
    }
    private static WorkResult CommandResult(WorkRequest request, CommandDispatch dispatch, System.Text.Json.JsonElement result)
    {
        if (result.GetProperty("requestId").GetGuid() != request.Id || result.GetProperty("nonce").GetString() != dispatch.Nonce ||
            result.GetProperty("workspace").GetString() != dispatch.Workspace || result.GetProperty("user").GetString() != dispatch.Profile.User ||
            result.GetProperty("commandId").GetString() != request.CommandId || result.GetProperty("program").GetString() != dispatch.Rule.Program ||
            !result.GetProperty("arguments").EnumerateArray().Select(item => item.GetString()).SequenceEqual(dispatch.Rule.Arguments))
            throw new InvalidDataException("Command identity or effective execution context was not confirmed.");
        return new("endpoint-command", result.GetProperty("stdout").GetString()!, result.GetProperty("stderr").GetString()!, result.GetProperty("exitCode").GetInt32(),
            new Dictionary<string, string> { ["workspace"] = dispatch.Workspace, ["user"] = dispatch.Profile.User, ["program"] = dispatch.Rule.Program, ["arguments"] = RelayJson.Serialize(dispatch.Rule.Arguments) });
    }
    /// <summary>Read the retained receipt for the exact prepared dispatch; never replay a command.</summary>
    /// <seealso cref="ExecuteAsync"/>
    public async Task<WorkResult?> RecoverAsync(WorkRequest request, CancellationToken token)
    {
        var dispatch = await dispatches.FindAsync(request.Id, token);
        if (dispatch is null || dispatch.Scope != request.Scope) return null;
        var response = await bridge.CallAsync(dispatch.Profile, "recover", new { request.Id, nonce = dispatch.Nonce }, token);
        return response.TryGetProperty("receipt", out var retained) && retained.ValueKind == System.Text.Json.JsonValueKind.Object
            ? CommandResult(request, dispatch, retained) : null;
    }
}
