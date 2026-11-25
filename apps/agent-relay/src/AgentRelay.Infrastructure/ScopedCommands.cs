using System.Collections.Concurrent;
using System.Security.Cryptography;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record CommandDispatch(OperationScope Scope, WslProfile Profile, string Workspace, CommandRule Rule, string Nonce);

// Owns direct command preparation, dispatch context and matching receipt recovery.
public sealed class ScopedCommands(string data, SettingsStore technical, ProjectSettingsStore projects, WslBridge bridge,
    SqliteDocuments<CommandDispatch> dispatches)
{
    private readonly RelayDatabase database = new(data);
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> provisioning = new();

    internal static bool IsLocalWindowsPath(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) &&
        path[1] == ':' && path[2] is '\\' or '/';

    internal static string? Destination(RelayEndpoint endpoint) => endpoint.AdapterId switch
    {
        "windows-command" => "windows",
        "wsl-command" => "wsl",
        _ => null
    };

    internal static CommandRule[] Declared(RelaySettings settings, RelayEndpoint endpoint) => Destination(endpoint) switch
    {
        "windows" => (settings.WindowsCommands ?? []).SingleOrDefault(item => item.Id == endpoint.BindingId)?.Commands ?? [],
        "wsl" => settings.Profiles.SingleOrDefault(item => item.Id == endpoint.BindingId && item.Client == "command")?.Commands ?? [],
        _ => []
    };

    internal static void ValidateTarget(RelaySettings settings, RelayEndpoint endpoint, ProjectReaction rule)
    {
        if (Destination(endpoint) != rule.Destination || endpoint.BindingId != rule.Environment ||
            !Declared(settings, endpoint).Any(command => command.Id == rule.CommandId))
            throw new RelayException("reaction_target_invalid", "Select a declared command in the explicit destination and environment.", false);
    }
    /// <seealso cref="RecoverAsync"/>
    internal async Task<IPreparedWork> PrepareAsync(CallerIdentity caller, WorkRequest request, CancellationToken token)
    {
        var scope = request.Scope!;
        var policy = new ProjectPolicy(projects);
        RelayWorkspace workspace;
        RelayEndpoint endpoint;
        using (policy.Pin())
        {
            var member = policy.Demand(caller, scope.ProjectId, RelayCapabilities.RunCommand);
            if (!(member.CommandIds ?? []).Contains(request.CommandId))
                throw new RelayException("command_denied", "The command grant was revoked.");
            workspace = policy.Workspace(caller, scope.ProjectId, scope.WorkspaceId!.Value, false, scope.RecipientId);
            endpoint = policy.Configuration().Endpoints.Single(item => item.Id == scope.RecipientId);
        }
        var settings = technical.Load();
        var destination = Destination(endpoint);
        if (request.ExecutionBinding is { } expected && expected != destination + ":" + endpoint.BindingId)
            throw new RelayException("reaction_target_invalid", "The retained command environment changed.", false);
        var command = Declared(settings, endpoint).SingleOrDefault(item => item.Id == request.CommandId)
            ?? throw new RelayException("command_denied", "Command is not declared for this endpoint.");
        if (destination == "windows")
            return new ScopedPreparedWork(dispatchToken => ExecuteWindowsAsync(workspace.Location, command, dispatchToken));
        var binding = settings.Profiles.SingleOrDefault(item => item.Id == endpoint.BindingId && item.Client == "command")
            ?? throw new RelayException("adapter_unavailable", "A configured direct-command adapter is required.");
        var key = new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("command-runtime:" + endpoint.Id + ":" + scope.ProjectId + ":" + workspace.Id + ":" + scope.SenderId)).AsSpan(0, 16));
        var definition = new SessionDefinition(key, endpoint.Name, scope, endpoint, workspace, false);
        await WorkspaceIdentity.VerifyNativeAsync(database, binding, workspace, token);
        // Only bootstrap of the shared private command runtime is serialized. Commands run independently.
        var gate = provisioning.GetOrAdd(key, _ => new(1, 1));
        WslProfile prepared;
        await gate.WaitAsync(token);
        try { prepared = await WslProvisioner.PrepareSessionAsync(data, key, binding, token, definition); }
        finally { gate.Release(); }
        var profile = binding with { Root = prepared.Root, Workspace = workspace.Location };
        var dispatch = new CommandDispatch(scope, profile, workspace.Location, command, Guid.NewGuid().ToString("D"));
        return new ScopedPreparedWork(async dispatchToken =>
        {
            await WorkspaceIdentity.VerifyNativeAsync(database, profile, workspace, dispatchToken);
            await dispatches.SaveAsync(request.Id, dispatch, dispatchToken);
            var result = await bridge.CallAsync(profile, "command", new { request.Id, request.CommandId, nonce = dispatch.Nonce, command, Workspace = workspace.Location }, dispatchToken);
            return CommandResult(request, dispatch, result);
        });
    }
    private static async Task<WorkResult> ExecuteWindowsAsync(string location, CommandRule command, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows() || !IsLocalWindowsPath(location))
            throw new RelayException("invalid_workspace", "Windows commands require a local drive workspace.", false);
        var directory = Path.GetFullPath(location);
        RuntimePaths.DemandPrivatePath(directory);
        if (!Directory.Exists(directory))
            throw new RelayException("invalid_workspace", "The declared Windows workspace is unavailable.", false);
        var result = await LocalProcess.RunAsync(command.Program, command.Arguments, null, TimeSpan.FromSeconds(10), token, directory);
        return new("endpoint-command", result.Output, result.Error, result.ExitCode, new Dictionary<string, string>
        { ["workspace"] = directory, ["user"] = Environment.UserName, ["program"] = command.Program });
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
