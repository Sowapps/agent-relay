namespace AgentRelay.Windows;

internal sealed record LaunchOptions(string Mode, string DataDirectory, string? Message, Guid? Id, bool Show, int? LifetimeMilliseconds,
    string? Profile, string? Kind, string? Command, Guid? ConnectionId = null, string? InputFile = null, string? WindowTitle = null,
    bool OnDemand = false, string? PreviousDataDirectory = null, bool Apply = false, bool AllowUninitialized = false)
{
    public static LaunchOptions Parse(string[] arguments)
    {
        var mode = arguments.Length > 0 && !arguments[0].StartsWith('-') ? arguments[0] : "tray";
        if (mode is "wsl-maintenance" or "installation-update" or "installation-resume") return Maintenance(arguments);
        if (mode is not ("tray" or "startup" or "startup-enable" or "startup-disable" or "startup-status" or "startup-transfer" or "startup-entry" or "send" or "read" or "mcp" or "mcp-worker" or "mcp-stable-worker" or "mcp-contract" or "mcp-operations" or "portable-layout" or "prepare" or "submit" or "result" or "history" or "inbox" or "profiles" or "start-profile" or "admin" or "initialize" or "deployment-quiesce" or "maintenance-idle")) throw new ArgumentException("Unsupported application mode.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool show = false, onDemand = false, apply = false, allowUninitialized = false;
        for (var index = mode == "tray" && (arguments.Length == 0 || arguments[0].StartsWith('-')) ? 0 : 1; index < arguments.Length; index++)
        {
            var key = arguments[index];
            if (key == "--show") { show = true; continue; }
            if (key == "--on-demand" && mode == "tray" && !onDemand) { onDemand = true; continue; }
            if (key == "--apply" && mode is ("startup-transfer" or "startup-entry") && !apply) { apply = true; continue; }
            if (key == "--allow-uninitialized" && mode == "maintenance-idle" && !allowUninitialized) { allowUninitialized = true; continue; }
            var known = key is "--data-dir" or "--message" or "--id" or "--lifetime-ms" or "--profile" or "--kind" or "--command" or "--connection-id" or "--input" or "--window-title"
                || key == "--previous-data-dir" && mode == "startup-transfer";
            if (!known || index + 1 >= arguments.Length ||
                !values.TryAdd(key, arguments[++index])) throw new ArgumentException("Unknown, repeated or incomplete command option.");
        }
        var data = values.GetValueOrDefault("--data-dir") ?? (mode == "startup"
            ? Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!
            : AgentRelay.Infrastructure.RuntimePaths.DefaultDataDirectory());
        Guid? id = values.TryGetValue("--id", out var identifier) ? Guid.Parse(identifier) : null;
        if (mode is not ("mcp" or "mcp-worker" or "mcp-stable-worker" or "mcp-contract" or "mcp-operations" or "portable-layout")) AgentRelay.Infrastructure.RuntimePaths.DemandActive(data);
        int? lifetime = values.TryGetValue("--lifetime-ms", out var milliseconds) ? int.Parse(milliseconds) : null;
        if (lifetime is < 500 or > 300_000) throw new ArgumentException("Test lifetime must be 500–300000 milliseconds.");
        if (mode == "send" && !values.ContainsKey("--message")) throw new ArgumentException("Send requires --message.");
        if (mode == "read" && id is null) throw new ArgumentException("Read requires --id.");
        var previous = values.GetValueOrDefault("--previous-data-dir");
        if (mode == "startup-transfer" && (previous is null || !values.ContainsKey("--data-dir")))
            throw new ArgumentException("Startup transfer requires both explicit data directories.");
        if (mode == "startup-entry" && (!values.ContainsKey("--data-dir") || !Path.IsPathFullyQualified(data)))
            throw new ArgumentException("Startup entry requires an explicit absolute data directory.");
        if (mode == "maintenance-idle" && (!values.ContainsKey("--data-dir") || !Path.IsPathFullyQualified(data)))
            throw new ArgumentException("Maintenance requires an explicit absolute data directory.");
        return new(mode, Path.GetFullPath(data), values.GetValueOrDefault("--message"), id, show, lifetime,
            values.GetValueOrDefault("--profile"), values.GetValueOrDefault("--kind"), values.GetValueOrDefault("--command"), values.TryGetValue("--connection-id", out var connection) ? Guid.Parse(connection) : null, values.GetValueOrDefault("--input"), values.GetValueOrDefault("--window-title"), onDemand, previous, apply, allowUninitialized);
    }

    private static LaunchOptions Maintenance(string[] arguments)
    {
        if (arguments.Length != 3 || arguments[1] != "--data-dir" || !Path.IsPathFullyQualified(arguments[2]))
            throw new ArgumentException("Maintenance requires one explicit absolute data directory.");
        AgentRelay.Infrastructure.RuntimePaths.DemandActive(arguments[2]);
        return new(arguments[0], Path.GetFullPath(arguments[2]), null, null, false, null, null, null, null);
    }
}
