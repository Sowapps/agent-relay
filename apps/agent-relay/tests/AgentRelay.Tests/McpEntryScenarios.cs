using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Infrastructure;
using AgentRelay.Protocol;
using AgentRelay.Transport;

internal static class McpEntryScenarios
{
    public static async Task RunAsync(string root, string bundle, string appSource)
    {
        var app = Path.Combine(root, "app");
        var transport = Path.Combine(root, "transport");
        Directory.CreateDirectory(app); Directory.CreateDirectory(transport);
        foreach (var source in Directory.EnumerateFiles(appSource, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(app, Path.GetRelativePath(appSource, source));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
        }
        File.WriteAllText(Path.Combine(app, "installation.json"), RuntimePaths.InstallationDescriptor(root, app));
        File.Copy(bundle, Path.Combine(transport, "AgentRelay.Transport.exe"));
        var data = RuntimePaths.PortableDataDirectory(root);
        Directory.CreateDirectory(data);
        McpTransportInstallation.Prepare(data, app, transport);
        var launch = McpTransportInstallation.Launch(data);
        var start = new ProcessStartInfo(launch.Command) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in launch.Args) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new Exception("Common launch entry did not start");
        var errors = child.StandardError.ReadToEndAsync();
        var responses = new McpFrames(child.StandardOutput.BaseStream);
        var retained = new List<JsonNode>();
        async Task<JsonNode> Call(int id, string method, object parameters)
        {
            await McpFrames.WriteAsync(child.StandardInput.BaseStream, JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters }));
            var value = JsonNode.Parse(await responses.ReadAsync().WaitAsync(TimeSpan.FromSeconds(60)) ?? throw new Exception("Entry closed its MCP stream"))!;
            retained.Add(value.DeepClone());
            File.WriteAllText(Path.Combine(root, "protocol.json"), JsonSerializer.Serialize(retained));
            if (value["id"]?.GetValue<int>() != id || value["error"] is not null) throw new Exception("Unexpected launch response: " + value);
            return value["result"]!;
        }
        try
        {
            await Call(1, "initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "isolated common entry", version = "1" } });
            var tools = await Call(2, "tools/list", new { });
            if (tools["tools"]?.AsArray().Count != 4) throw new Exception("Common entry exposed wrong tool surface");
            var resource = await Call(3, "resources/read", new { uri = "agent-relay://docs/quickstart" });
            if (string.IsNullOrEmpty(resource["contents"]?[0]?["text"]?.GetValue<string>())) throw new Exception("Selected real worker did not serve documentation");
            var refusal = await Call(4, "tools/call", new { name = "get_connection_status", arguments = new { } });
            if (refusal["structuredContent"]?["error"]?["code"]?.GetValue<string>() != "authentication_required") throw new Exception("Unbound entry silently acquired a principal");
            var presence = McpTransportPresences.Read(RuntimePaths.TransportStateDirectory(data)).Single(item => item.State == "current");
            using (var gateway = Process.GetProcessById(presence.ProcessId))
            {
                var nativeRuntimeModules = gateway.Modules.Cast<ProcessModule>().Where(module => module.ModuleName.Contains("coreclr", StringComparison.OrdinalIgnoreCase) ||
                    module.ModuleName.Contains("hostpolicy", StringComparison.OrdinalIgnoreCase)).Select(module => module.FileName).ToArray();
                File.WriteAllText(Path.Combine(root, "runtime-location.json"), JsonSerializer.Serialize(new { presence.BundleDirectory, nativeRuntimeModules, configured = RuntimePaths.TransportBundleDirectory(data) }));
                if (presence.BundleDirectory != RuntimePaths.TransportBundleDirectory(data)) throw new Exception("Transport did not inherit explicit var extraction configuration");
            }
            McpTransportPresences.RequestClose(RuntimePaths.TransportStateDirectory(data), presence.InstanceId);
            if (await responses.ReadAsync().WaitAsync(TimeSpan.FromSeconds(10)) is not null)
                throw new Exception("Closure produced an unexpected response");
            child.StandardInput.Close();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            if (child.ExitCode != 0) throw new Exception("Common entry did not close cleanly");
            Console.WriteLine("MCP_COMMON_ENTRY=PASS (PowerShell stdio, single-file fixed bootstrap to selected C# process, real worker resource, unbound refusal, explicit var runtime configuration; no host/model/settings effects)");
        }
        finally
        {
            child.StandardInput.Close();
            if (!child.HasExited) { child.Kill(entireProcessTree: true); await child.WaitForExitAsync(); }
            File.WriteAllText(Path.Combine(root, "stderr.log"), await errors.WaitAsync(TimeSpan.FromSeconds(10)));
        }
    }
}
