namespace AgentRelay.Infrastructure;

/// <summary>Shared projection of a portable runtime into its retained native launchers.</summary>
/// <seealso cref="McpInstallation"/>
/// <seealso cref="InstallationUpdate"/>
public static class RetainedLauncherPayload
{
    public static bool Includes(string relative, bool documentation)
    {
        relative = relative.Replace('\\', '/');
        var first = relative.Split('/')[0];
        if (first is "presentation" or "wsl" or "tools") return false;
        if (first == "documentation") return documentation;
        return Path.GetExtension(relative).ToLowerInvariant() is ".dll" or ".exe" or ".json";
    }
}
