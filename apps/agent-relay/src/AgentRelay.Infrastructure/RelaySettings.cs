using System.Text.RegularExpressions;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record CommandRule(string Id, string Name, string Program, string[] Arguments);
public sealed record WslProfile(string Id, string Name, string Distribution, string User, string Root, string Client, CommandRule[] Commands,
    bool AllowLocalWork = false, string? Workspace = null, string? NativeExecutable = null);
public sealed record TransferLimits(long MaximumBytes = 256L * 1024 * 1024, int WaitSeconds = 180);
public sealed record RelaySettings(int Version, WslProfile[] Profiles, int MaximumConcurrency = 4, TransferLimits? Transfers = null);

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
                !Regex.IsMatch(profile.Root, "^/srv/agent-relay-[a-zA-Z0-9-]+$") || profile.Client is not ("command" or "codex" or "claude") ||
                profile.Commands is null || (profile.Client != "command" && profile.Commands.Length != 0) ||
                (profile.NativeExecutable is { } executable && (!executable.StartsWith('/') || executable.Length > 500 || executable.Any(char.IsControl))))
                throw new RelayException("invalid_configuration", "Profiles require a dedicated /srv/agent-relay-* root, Linux user, distribution and supported client.");
            if (profile.Commands.Length > 100 || profile.Commands.Select(rule => rule.Id).Distinct().Count() != profile.Commands.Length)
                throw new RelayException("invalid_configuration", "Commands require unique identifiers and a bounded count.");
            foreach (var rule in profile.Commands)
                if (string.IsNullOrWhiteSpace(rule.Id) || rule.Id.Length > 100 || !rule.Program.StartsWith('/') || rule.Program.Length > 500 || rule.Arguments is null || rule.Arguments.Length > 100 || rule.Arguments.Any(argument => argument is null || argument.Length > 1000))
                    throw new RelayException("invalid_configuration", "Commands require an absolute program and bounded argument array.");
        }
    }
}
