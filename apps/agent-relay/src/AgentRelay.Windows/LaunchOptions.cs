namespace AgentRelay.Windows;

internal sealed record LaunchOptions(string Mode, string DataDirectory, string? Message, Guid? Id, bool Show, int? LifetimeMilliseconds,
    string? Profile, string? Kind, string? Command, Guid? ConnectionId = null, string? InputFile = null, string? WindowTitle = null)
{
    public static LaunchOptions Parse(string[] arguments)
    {
        var mode = arguments.Length > 0 && !arguments[0].StartsWith('-') ? arguments[0] : "tray";
        if (mode is not ("tray" or "send" or "read" or "mcp" or "prepare" or "submit" or "result" or "history" or "inbox" or "profiles" or "start-profile" or "admin" or "initialize")) throw new ArgumentException("Unsupported application mode.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool show = false;
        for (var index = mode == "tray" && (arguments.Length == 0 || arguments[0].StartsWith('-')) ? 0 : 1; index < arguments.Length; index++)
        {
            var key = arguments[index];
            if (key == "--show") { show = true; continue; }
            if (key is not ("--data-dir" or "--message" or "--id" or "--lifetime-ms" or "--profile" or "--kind" or "--command" or "--connection-id" or "--input" or "--window-title") || index + 1 >= arguments.Length ||
                !values.TryAdd(key, arguments[++index])) throw new ArgumentException("Unknown, repeated or incomplete command option.");
        }
        var data = values.GetValueOrDefault("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AgentRelay");
        Guid? id = values.TryGetValue("--id", out var identifier) ? Guid.Parse(identifier) : null;
        int? lifetime = values.TryGetValue("--lifetime-ms", out var milliseconds) ? int.Parse(milliseconds) : null;
        if (lifetime is < 500 or > 300_000) throw new ArgumentException("Test lifetime must be 500–300000 milliseconds.");
        if (mode == "send" && !values.ContainsKey("--message")) throw new ArgumentException("Send requires --message.");
        if (mode == "read" && id is null) throw new ArgumentException("Read requires --id.");
        return new(mode, Path.GetFullPath(data), values.GetValueOrDefault("--message"), id, show, lifetime,
            values.GetValueOrDefault("--profile"), values.GetValueOrDefault("--kind"), values.GetValueOrDefault("--command"), values.TryGetValue("--connection-id", out var connection) ? Guid.Parse(connection) : null, values.GetValueOrDefault("--input"), values.GetValueOrDefault("--window-title"));
    }
}
