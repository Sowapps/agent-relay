using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

internal static class PortableConfiguration
{
    public static async Task InitializeAsync(string data, Guid? requestedConnection)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The portable executable is unavailable.");
        if (!Path.GetFileName(executable).Equals("AgentRelay.Windows.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Use the portable AgentRelay.Windows.exe for initialization.");
        var pointerPath = Path.Combine(data, "mcp-runtime.json");
        var previous = File.Exists(pointerPath) ? JsonNode.Parse(await File.ReadAllTextAsync(pointerPath)) : null;
        if (previous is not null && previous["distribution"]?.GetValue<string>() != "portable-win-x64")
            throw new ArgumentException("This data directory uses another deployment format; use a distinct portable scope or deliberate migration.");
        var root = RuntimePaths.InstallationRoot() ?? throw new IOException("Portable installation ownership is missing.");
        if (!string.Equals(Path.GetFullPath(data), RuntimePaths.PortableDataDirectory(root), StringComparison.OrdinalIgnoreCase) ||
            previous?["executable"] is { } priorExecutable && !string.Equals(priorExecutable.GetValue<string>(), executable, StringComparison.OrdinalIgnoreCase))
            throw new RelayException("update_scope_invalid", "Initialization never rebinds a retained installation to a different executable or data scope. Use its reviewed update/migration procedure.", false);
        var connection = requestedConnection ?? (Guid.TryParse(previous?["connectionId"]?.GetValue<string>(), out var id) ? id : (Guid?)null);
        if (connection is { } scoped)
        {
            var store = new ConnectionStore(data);
            var authenticated = await store.AuthenticateAsync(store.Secret(scoped), CancellationToken.None);
            if (authenticated.Administrator) throw new RelayException("capability_denied", "Export requires a participant-scoped connection, never an administrator credential.");
        }
        Directory.CreateDirectory(data);
        if (connection is null)
        {
            await AtomicWrite(pointerPath, JsonSerializer.Serialize(new { executable, distribution = "portable-win-x64", connectionId = (string?)null }));
            Console.WriteLine("Local portable data initialized. Create a project and scoped connection, then initialize again. No client settings or permissions changed.");
            return;
        }
        var transportDirectory = File.Exists(RuntimePaths.TransportSelection(data))
            ? Path.GetDirectoryName(AgentRelay.Protocol.McpProcessSelections.Read(RuntimePaths.TransportSelection(data)).Gateway.Executable)!
            : Path.Combine(root, "transport");
        McpTransportInstallation.Prepare(data, AppContext.BaseDirectory, transportDirectory);
        var launch = McpTransportInstallation.Launch(data, connection);
        await AtomicWrite(pointerPath, JsonSerializer.Serialize(new { executable, distribution = "portable-win-x64", connectionId = connection.Value.ToString("D"),
            mcpExecutable = RuntimePaths.TransportBootstrapExecutable(data), transportSelection = RuntimePaths.TransportSelection(data), mcpLaunch = launch }, RelayJson.Options));
        await AtomicWrite(Path.Combine(data, "mcp.windows.json"), JsonSerializer.Serialize(new { mcpServers = new { agent_relay = new { command = launch.Command, args = launch.Args } } }));
        var toml = "[mcp_servers.agent_relay]\ncommand = " + JsonSerializer.Serialize(launch.Command) + "\nargs = " + JsonSerializer.Serialize(launch.Args) + "\nrequired = true\ntool_timeout_sec = 180\n";
        await AtomicWrite(Path.Combine(data, "mcp.codex.toml"), toml);
        Console.WriteLine("Scoped MCP fragments exported. No client settings changed; regenerate after changing connection or moving the package.");
    }

    private static async Task AtomicWrite(string path, string text)
    {
        var pending = path + ".pending";
        await File.WriteAllTextAsync(pending, text);
        File.Move(pending, path, true);
    }
}
