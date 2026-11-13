using System.Security.Cryptography;
using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed record McpLaunch(string Command, string[] Args);

/// <summary>Composition for the one stable launch entry; transport code/policy is never copied into the script.</summary>
public static class McpTransportInstallation
{
    public static McpLaunch Launch(string data, Guid? binding = null)
    {
        var args = new List<string> { "-NoProfile", "-NonInteractive", "-File", RuntimePaths.TransportEntryScript(data) };
        if (binding is { } id) { args.Add("-ConnectionId"); args.Add(id.ToString("D")); }
        return new(PowerShell(), args.ToArray());
    }

    public static McpLaunch HostLaunch(string data, bool show = false)
    {
        // Keep the Windows Run command within its native length limit; these CLI abbreviations are unambiguous.
        var args = new List<string> { "-NoP", "-NonI", "-W", "Hidden", "-F", RuntimePaths.TransportEntryScript(data), "-HostLaunch" };
        if (show) args.Add("-Show");
        return new(PowerShell(), args.ToArray());
    }

    public static string PowerShell()
    {
        if (Environment.GetEnvironmentVariable("AGENT_RELAY_PWSH") is { Length: > 0 } configured)
        {
            if (Path.IsPathFullyQualified(configured) && File.Exists(configured) && Path.GetFileName(configured).Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase)) return configured;
            throw new IOException("AGENT_RELAY_PWSH must name the installed PowerShell 7 executable.");
        }
        var installed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
        if (File.Exists(installed)) return installed;
        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            if (Path.IsPathFullyQualified(folder) && File.Exists(Path.Combine(folder, "pwsh.exe"))) return Path.GetFullPath(Path.Combine(folder, "pwsh.exe"));
        throw new RelayException("launch_prerequisite_missing", "PowerShell 7 must be available on PATH for this installation's launch entry.", false);
    }

    public static object PortableLayout(string root)
    {
        var data = RuntimePaths.PortableDataDirectory(root);
        string Relative(string path) => Path.GetRelativePath(root, path).Replace('\\', '/');
        return new { schema = 1, data = Relative(data), runtimePointer = Relative(RuntimePaths.RuntimePointer(data)),
            entryScript = Relative(RuntimePaths.TransportEntryScript(data)), initialExecutable = "app/AgentRelay.Windows.exe" };
    }

    public static McpProcessSelection Selection(string data, string appDirectory, string transportDirectory)
    {
        McpProcessImage Image(string directory, string executable, string[] args) => new(Path.Combine(directory, executable), args,
            Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'), Hash));
        var worker = Image(appDirectory, "AgentRelay.Windows.exe", ["mcp-stable-worker", "--data-dir", data]);
        return new(1, McpStableProtocol.Requirement, worker, Image(transportDirectory, "AgentRelay.Transport.exe", []),
            worker with { Arguments = ["tray", "--data-dir", data] });
    }

    public static void Prepare(string data, string appDirectory, string transportDirectory)
    {
        _ = PowerShell();
        var root = RuntimePaths.InstallationRoot(appDirectory) ?? throw new IOException("The installation root is not declared.");
        if (!string.Equals(Path.GetFullPath(data), RuntimePaths.PortableDataDirectory(root), StringComparison.OrdinalIgnoreCase))
            throw new RelayException("update_scope_invalid", "Transport preparation requires this installation's retained portable data scope.", false);
        var selected = Selection(data, appDirectory, transportDirectory);
        McpProcessSelections.Validate(selected.Worker); McpProcessSelections.Validate(selected.Gateway);
        if (selected.Gateway.Files.Count != 1) throw new IOException("The fixed bootstrap requires the qualified single-file transport image.");
        var target = RuntimePaths.TransportBootstrapExecutable(data);
        RuntimePaths.DemandPrivatePath(target);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (!File.Exists(target))
        {
            var pending = target + ".pending";
            File.Copy(selected.Gateway.Executable, pending, true);
            File.Move(pending, target);
        }
        else if (Hash(target) != selected.Gateway.Files["AgentRelay.Transport.exe"])
            throw new RelayException("update_required", "The bootstrap belongs to another build. Use the complete-update transaction rather than replacing it during initialization.", false);
        WriteKnown(RuntimePaths.TransportEntryScript(data), EntryScript(data));
        var selectionPath = RuntimePaths.TransportSelection(data);
        var content = JsonSerializer.Serialize(selected, McpProcessSelections.Json);
        if (!File.Exists(selectionPath)) Atomic(selectionPath, content);
        else if (McpStableProtocol.HashJson(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(selectionPath))) != McpStableProtocol.HashJson(JsonSerializer.Deserialize<JsonElement>(content)))
            throw new RelayException("update_required", "A different release is selected. Use the complete-update procedure; initialization never rebinds an existing installation.", false);
    }

    public static string EntryScript(string data)
    {
        static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
        return "param([string]$ConnectionId,[switch]$HostLaunch,[switch]$Show)\n" +
            "$ErrorActionPreference = 'Stop'\n[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n$OutputEncoding = [Console]::OutputEncoding\n" +
            "$env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = " + Literal(RuntimePaths.TransportBundleDirectory(data)) + "\n" +
            "$relayArguments = @('--selection'," + Literal(RuntimePaths.TransportSelection(data)) + ",'--state-dir'," + Literal(RuntimePaths.TransportStateDirectory(data)) + ")\n" +
            "if ($HostLaunch) { $relayArguments += '--launch-host'; if ($Show) { $relayArguments += '--show' } }\n" +
            "elseif ($ConnectionId) { $relayArguments += @('--connection-id',$ConnectionId) }\n" +
            "& " + Literal(RuntimePaths.TransportBootstrapExecutable(data)) + " @relayArguments\nexit $LASTEXITCODE\n";
    }

    private static string Hash(string path)
    {
        RuntimePaths.DemandPrivatePath(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
    private static void WriteKnown(string path, string content)
    {
        if (File.Exists(path) && File.ReadAllText(path) != content) throw new IOException("The owned launch script was changed; review it before replacing it.");
        if (!File.Exists(path)) Atomic(path, content);
    }
    private static void Atomic(string path, string content)
    {
        RuntimePaths.DemandPrivatePath(path);
        var pending = path + ".pending";
        File.WriteAllText(pending, content, new System.Text.UTF8Encoding(false));
        File.Move(pending, path, true);
    }
}
