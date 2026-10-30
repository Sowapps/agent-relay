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
        var connection = requestedConnection ?? (Guid.TryParse(previous?["connectionId"]?.GetValue<string>(), out var id) ? id : (Guid?)null);
        if (connection is { } scoped)
        {
            var store = new ConnectionStore(data);
            var authenticated = await store.AuthenticateAsync(store.Secret(scoped), CancellationToken.None);
            if (authenticated.Administrator) throw new RelayException("capability_denied", "Export requires a participant-scoped connection, never an administrator credential.");
        }
        Directory.CreateDirectory(data);
        var pointer = new { executable, distribution = "portable-win-x64", connectionId = connection?.ToString("D") };
        await AtomicWrite(pointerPath, JsonSerializer.Serialize(pointer));
        if (connection is null)
        {
            Console.WriteLine("Local portable data initialized. Create a project and scoped connection, then initialize again. No client settings or permissions changed.");
            return;
        }
        string[] arguments = ["mcp", "--data-dir", data, "--connection-id", connection.Value.ToString("D")];
        await AtomicWrite(Path.Combine(data, "mcp.windows.json"), JsonSerializer.Serialize(new { mcpServers = new { agent_relay = new { command = executable, args = arguments } } }));
        var toml = "[mcp_servers.agent_relay]\ncommand = " + JsonSerializer.Serialize(executable) + "\nargs = " + JsonSerializer.Serialize(arguments) + "\nrequired = true\ntool_timeout_sec = 180\n";
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
