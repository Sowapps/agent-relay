using System.Runtime.InteropServices;
using System.Text.Json;

namespace AgentRelay.Infrastructure;

public static class McpInstallation
{
    public static string PrepareBroker(string data)
    {
        var destination = Path.Combine(Path.GetFullPath(data), "mcp-launcher");
        var executable = Path.Combine(destination, "AgentRelay.Windows.exe");
        RuntimePaths.DemandPrivatePath(executable);
        if (File.Exists(Path.Combine(destination, "mcp-launcher.ready")) && File.Exists(executable)) return executable;
        if (Directory.Exists(destination)) throw new IOException("The retained MCP launcher is incomplete; repair it without replacing a live executable.");
        var source = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(source, "hostfxr.dll"))) throw new IOException("MCP installation requires a prepared self-contained native runtime.");
        var stage = destination + ".pending-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(stage);
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative == "installation.json" || relative == "mcp-launcher.ready") continue;
                if (!RetainedLauncherPayload.Includes(relative, documentation: true)) continue;
                var target = Path.Combine(stage, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!CreateHardLink(target, file, IntPtr.Zero)) File.Copy(file, target);
            }
            var root = RuntimePaths.InstallationRoot() ?? throw new IOException("Installation ownership is missing.");
            File.WriteAllText(Path.Combine(stage, "installation.json"), RuntimePaths.InstallationDescriptor(root, destination));
            File.WriteAllText(Path.Combine(stage, "mcp-launcher.ready"), "Retained native MCP broker protocol1. Keep while client registrations refer to it.\n");
            DemandOwned(data, stage); DemandOwned(data, destination);
            // Windows can briefly lock newly linked runtime files (scanner/indexer). Retry only
            // the owned atomic directory publication, never a client call or an agent operation.
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Move(stage, destination); break; }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException && attempt < 40 && Directory.Exists(stage) && !Directory.Exists(destination))
                { Thread.Sleep(50); }
            }
        }
        finally { if (Directory.Exists(stage)) { DemandOwned(data, stage); Directory.Delete(stage, true); } }
        return executable;
    }
    private static void DemandOwned(string data, string target)
    {
        var root = Path.GetFullPath(data).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(target).StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("MCP launcher preparation escaped its explicit data directory.");
    }
    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLink(string link, string existing, IntPtr security);
}
