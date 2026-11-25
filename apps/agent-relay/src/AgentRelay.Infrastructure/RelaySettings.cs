using System.Text.RegularExpressions;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record CommandRule(string Id, string Name, string Program, string[] Arguments);
public sealed record WslProfile(string Id, string Name, string Distribution, string User, string Root, string Client, CommandRule[] Commands,
    bool AllowLocalWork = false, string? Workspace = null, string? NativeExecutable = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    internal McpLaunch? ParticipantMcp { get; init; }
}
public sealed record TransferLimits(long MaximumBytes = 256L * 1024 * 1024, int WaitSeconds = 180);
public sealed record WindowsCommandProfile(string Id, CommandRule[] Commands);
public sealed record RelaySettings(int Version, WslProfile[] Profiles, int MaximumConcurrency = 4, TransferLimits? Transfers = null,
    WindowsCommandProfile[]? WindowsCommands = null);

public sealed class SettingsStore(string dataDirectory)
{
    private readonly string path = Path.Combine(dataDirectory, "relay-config.json");
    public RelaySettings Load() => File.Exists(path) ? RelayJson.Deserialize<RelaySettings>(File.ReadAllText(path)) : new(1, []);

    public void Save(RelaySettings settings)
    {
        Validate(settings);
        Directory.CreateDirectory(dataDirectory);
        var temporary = path + ".pending-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, RelayJson.Serialize(settings));
        File.Move(temporary, path, true);
    }

    public static void Validate(RelaySettings settings)
    {
        if (settings.Version != 1 || settings.MaximumConcurrency is < 1 or > 64 || settings.Profiles is null || settings.Profiles.Length > 20 ||
            (settings.Transfers is { } limits && (limits.MaximumBytes is < 1 or > 4L * 1024 * 1024 * 1024 || limits.WaitSeconds is < 30 or > 3600)) ||
            settings.Profiles.Select(profile => profile.Id).Distinct().Count() != settings.Profiles.Length ||
            settings.Profiles.Select(profile => (profile.Distribution, profile.User, profile.Root, profile.Client)).Distinct().Count() != settings.Profiles.Length)
            throw new RelayException("invalid_configuration", "Expected unique profiles and configuration version 1.");
        foreach (var profile in settings.Profiles)
        {
            if (!Regex.IsMatch(profile.Id, "^[a-z][a-z0-9-]{0,63}$") || string.IsNullOrWhiteSpace(profile.Name) ||
                !Regex.IsMatch(profile.Distribution, "^[A-Za-z0-9_.-]+$") || !Regex.IsMatch(profile.User, "^[a-z_][a-z0-9_-]*$") ||
                !RuntimePaths.IsNativeRoot(profile.Root) || profile.Client is not ("command" or "codex" or "claude") ||
                profile.Commands is null || (profile.Client != "command" && profile.Commands.Length != 0) ||
                (profile.NativeExecutable is { } executable && (!executable.StartsWith('/') || executable.Length > 500 || executable.Any(char.IsControl))))
                throw new RelayException("invalid_configuration", "Profiles require a dedicated host runtime root under /srv/agent-relay/var/apps/agent-relay (or an explicit legacy root), Linux user, distribution and supported client.");
            ValidateCommands(profile.Commands, program => program.StartsWith('/'));
        }
        var windows = settings.WindowsCommands ?? [];
        if (windows.Length > 20 || windows.Any(profile => profile is null || !Regex.IsMatch(profile.Id ?? "", "^[a-z][a-z0-9-]{0,63}$")) ||
            windows.DistinctBy(profile => profile.Id).Count() != windows.Length)
            throw new RelayException("invalid_configuration", "Windows command profiles require unique identifiers.");
        foreach (var profile in windows) ValidateCommands(profile.Commands,
            program => ScopedCommands.IsLocalWindowsPath(program) && program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
    }

    private static void ValidateCommands(CommandRule[]? commands, Func<string, bool> validProgram)
    {
        if (commands is not { Length: <= 100 } || commands.Any(rule => rule is null || string.IsNullOrWhiteSpace(rule.Id) ||
            rule.Id.Length > 100 || string.IsNullOrWhiteSpace(rule.Program) || rule.Program.Length > 500 ||
            rule.Program.Any(char.IsControl) || !validProgram(rule.Program) || rule.Arguments is not { Length: <= 100 } ||
            rule.Arguments.Any(argument => argument is null || argument.Length > 1000 || argument.Contains('\0'))) ||
            commands.DistinctBy(rule => rule.Id).Count() != commands.Length)
            throw new RelayException("invalid_configuration", "Commands require unique IDs, an absolute executable and a bounded argument array.");
    }
}
