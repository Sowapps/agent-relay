using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public static class WslProvisioner
{
    public static async Task<WslProfile> PrepareSessionAsync(string dataDirectory, Guid sessionId, WslProfile template, CancellationToken token, SessionDefinition? definition = null)
    {
        var output = Path.Combine(dataDirectory, "prepared", "session-" + sessionId.ToString("N"));
        var arguments = new List<string> { "-B", Path.Combine(AppContext.BaseDirectory, "tools", "provision-wsl.py"),
            "--output", output, "--distribution", template.Distribution, "--user", template.User, "--session-id", sessionId.ToString("D") };
        if (definition is not null)
        {
            arguments.AddRange(["--workspace", definition.Workspace.Location, "--client", template.Client,
                "--binding", RelayJson.Serialize(new { definition.Scope.ProjectId, definition.Scope.SenderId, definition.Scope.RecipientId, SessionId = definition.Id })]);
        }
        if (template.NativeExecutable is not null) arguments.AddRange(["--executable", template.NativeExecutable]);
        var result = await LocalProcess.RunAsync("python.exe", arguments, null, TimeSpan.FromSeconds(80), token);
        if (result.ExitCode != 0) throw new RelayException("session_setup_failed", "Dedicated session preparation failed: " + result.Error);
        return new SettingsStore(output).Load().Profiles.Single(profile => profile.Client == template.Client) with
        { Id = template.Id, Name = template.Name, AllowLocalWork = template.AllowLocalWork };
    }

    public static async Task<RelaySettings> PrepareAsync(string dataDirectory, string distribution, string user, CancellationToken token)
    {
        var output = Path.Combine(dataDirectory, "prepared", Guid.NewGuid().ToString("N"));
        var script = Path.Combine(AppContext.BaseDirectory, "tools", "provision-wsl.py");
        var result = await LocalProcess.RunAsync("python.exe", ["-B", script, "--output", output, "--distribution", distribution, "--user", user],
            null, TimeSpan.FromSeconds(40), token);
        if (result.ExitCode != 0) throw new IOException("WSL preparation failed: " + result.Error);
        var settings = new SettingsStore(output).Load();
        SettingsStore.Validate(settings);
        var suffix = Path.GetFileName(output)[..8];
        return settings with { Profiles = settings.Profiles.Select(profile => profile with { Id = profile.Id + "-" + suffix }).ToArray() };
    }
}
