using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace AgentRelay.Protocol;

public sealed record McpTransportPresence(Guid InstanceId, int ProcessId, Guid? BindingId, string Version,
    string GatewayIdentity, string? ActiveWorkerIdentity, string SelectedWorkerIdentity, string State, DateTimeOffset SeenAt,
    long BufferedInputBytes = 0, DateTimeOffset? ProcessStartedAt = null);

/// <summary>Diagnostic process presence; never authentication or permission to execute/replay work.</summary>
public static class McpTransportPresences
{
    public static string DirectoryFor(string stateDirectory) => Path.Combine(stateDirectory, "instances");
    public static void Write(string stateDirectory, McpTransportPresence value)
    {
        var path = Path.Combine(DirectoryFor(stateDirectory), value.InstanceId.ToString("N") + ".json");
        McpPrivateFiles.Demand(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".pending";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, McpProcessSelections.Json));
        File.Move(temporary, path, true);
    }

    public static IReadOnlyList<McpTransportPresence> Read(string stateDirectory)
    {
        var directory = DirectoryFor(stateDirectory);
        if (!Directory.Exists(directory)) return [];
        var values = new List<McpTransportPresence>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal).Take(256))
        {
            McpPrivateFiles.Demand(path);
            if (new FileInfo(path).Length > 16384) throw new IOException("Transport presence exceeds its bound.");
            var value = JsonSerializer.Deserialize<McpTransportPresence>(File.ReadAllText(path), McpProcessSelections.Json)
                ?? throw new IOException("Transport presence is empty.");
            if (Path.GetFileNameWithoutExtension(path) != value.InstanceId.ToString("N")) throw new IOException("Transport presence identity mismatch.");
            if (value.ProcessId <= 0) throw new IOException("Transport process identity is invalid.");
            var state = value.State;
            if (state != "closed")
            {
                try
                {
                    using var process = Process.GetProcessById(value.ProcessId);
                    if (process.HasExited || value.ProcessStartedAt is { } started && new DateTimeOffset(process.StartTime.ToUniversalTime()) != started) state = "offline";
                    else if (DateTimeOffset.UtcNow - value.SeenAt > TimeSpan.FromSeconds(5) || value.SeenAt > DateTimeOffset.UtcNow.AddMinutes(1)) state = "unresponsive";
                }
                catch (ArgumentException) { state = "offline"; }
                catch (Win32Exception) { state = "unresponsive"; }
            }
            values.Add(value with { State = state });
        }
        return values;
    }
}
