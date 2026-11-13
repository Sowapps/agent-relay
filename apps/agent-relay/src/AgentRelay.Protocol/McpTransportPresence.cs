using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace AgentRelay.Protocol;

public sealed record McpTransportPresence(Guid InstanceId, int ProcessId, Guid? BindingId, string Version,
    string GatewayIdentity, string? ActiveWorkerIdentity, string SelectedWorkerIdentity, string State, DateTimeOffset SeenAt,
    long BufferedInputBytes = 0, DateTimeOffset? ProcessStartedAt = null, string? BundleDirectory = null, string? Client = null);

/// <summary>Diagnostic process presence; never authentication or permission to execute/replay work.</summary>
public static class McpTransportPresences
{
    public static string DirectoryFor(string stateDirectory) => Path.Combine(stateDirectory, "instances");
    public static bool CloseRequested(string stateDirectory, Guid instance) => File.Exists(Path.Combine(DirectoryFor(stateDirectory), instance.ToString("N") + ".close"));

    /// <summary>Requests closure at the next safe boundary; does not revoke credentials or replay work.</summary>
    /// <seealso cref="CloseRequested"/>
    public static void RequestClose(string stateDirectory, Guid instance)
    {
        if (!Read(stateDirectory).Any(item => item.InstanceId == instance && item.State is not ("offline" or "closed")))
            throw new IOException("No active generic transport has that identity.");
        var path = Path.Combine(DirectoryFor(stateDirectory), instance.ToString("N") + ".close");
        McpPrivateFiles.Demand(path);
        File.WriteAllText(path, "Close at a safe boundary; preserve credentials and never replay work.");
    }
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
            var value = JsonSerializer.Deserialize<McpTransportPresence>(McpPrivateFiles.ReadText(path, 16384), McpProcessSelections.Json)
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
