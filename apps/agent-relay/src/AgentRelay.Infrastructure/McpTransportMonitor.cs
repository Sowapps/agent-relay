using System.Text.Json;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed record McpTransportView(IReadOnlyList<McpTransportPresence> Connections, string? Error = null);

/// <summary>Passive presentation of generic transport leases; never grants authority.</summary>
public static class McpTransportMonitor
{
    public static McpTransportView Read(string data)
    {
        try
        {
            var display = McpTransportPresences.Read(RuntimePaths.TransportStateDirectory(data))
                .OrderBy(item => item.State is "offline" or "closed")
                .ThenBy(item => item.InstanceId)
                .Take(256).ToArray();
            return new(display);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new([], "Transport diagnostics unavailable: " + error.Message);
        }
    }

    public static bool RequestClose(string data, Guid instance) =>
        McpTransportPresences.RequestClose(RuntimePaths.TransportStateDirectory(data), instance);
}
