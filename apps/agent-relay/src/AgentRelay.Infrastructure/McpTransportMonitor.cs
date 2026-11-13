using System.Text.Json;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed record McpTransportView(IReadOnlyList<McpTransportPresence> Connections, string? Error = null);

/// <summary>Passive presentation of generic transport leases; never grants authority.</summary>
public static class McpTransportMonitor
{
    public static McpTransportView Read(string data)
    {
        try { return new(McpTransportPresences.Read(RuntimePaths.TransportStateDirectory(data))); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new([], "Transport diagnostics unavailable: " + error.Message);
        }
    }

    public static bool RequestClose(string data, Guid instance)
    {
        if (!Read(data).Connections.Any(item => item.InstanceId == instance)) return false;
        McpTransportPresences.RequestClose(RuntimePaths.TransportStateDirectory(data), instance);
        return true;
    }
}
