using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Protocol;
using AgentRelay.Transport;

internal static class McpTransportScenarios
{
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private static string Json(object value) => JsonSerializer.Serialize(value, McpProcessSelections.Json);

    public static async Task FixtureAsync(string generation, string countFile, string behavior)
    {
        var input = new McpFrames(Console.OpenStandardInput());
        var output = Console.OpenStandardOutput();
        while (await input.ReadAsync() is { } line)
        {
            var request = JsonNode.Parse(line)!;
            if (request["id"] is not { } id) continue;
            object result;
            if (request["method"]!.GetValue<string>() == "initialize")
                result = new { _meta = new { agentRelay = new { compatibility = new { transport = McpStableProtocol.Requirement } } } };
            else
            {
                File.AppendAllText(countFile, generation + "\n");
                if (behavior == "lose") return;
                result = new { structuredContent = new { generation, pid = Environment.ProcessId, calls = File.ReadAllLines(countFile).Length } };
            }
            await McpFrames.WriteAsync(output, Json(new { jsonrpc = "2.0", id, result }));
        }
    }

    public static async Task RunAsync(string data, string gatewaySource)
    {
        Directory.CreateDirectory(data);
        var first = Bundle(gatewaySource, Path.Combine(data, "gateway-a"));
        var second = Bundle(gatewaySource, Path.Combine(data, "gateway-b"));
        var count = Path.Combine(data, "calls.txt");
        var testExe = Path.Combine(AppContext.BaseDirectory, "AgentRelay.Tests.exe");
        McpProcessImage Worker(string generation, string behavior = "ok") => new(testExe,
            ["transport-fixture", generation, count, behavior], Files(Path.GetDirectoryName(testExe)!));
        var selectionPath = Path.Combine(data, "selection.json");
        var state = Path.Combine(data, "state");
        void Select(McpProcessImage gateway, string generation, string behavior = "ok")
        {
            var temporary = selectionPath + ".pending";
            File.WriteAllText(temporary, Json(new McpProcessSelection(1, McpStableProtocol.Requirement, Worker(generation, behavior), gateway)));
            File.Move(temporary, selectionPath, true);
        }
        Select(first, "one");
        var start = new ProcessStartInfo(first.Executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "--selection", selectionPath, "--state-dir", state }) start.ArgumentList.Add(argument);
        using var parent = Process.Start(start) ?? throw new Exception("Gateway start failed");
        var errors = parent.StandardError.ReadToEndAsync();
        var responses = new McpFrames(parent.StandardOutput.BaseStream);
        var observations = new List<JsonNode>();
        async Task<JsonNode> Read(int id)
        {
            var line = await responses.ReadAsync().WaitAsync(TimeSpan.FromSeconds(75));
            var value = JsonNode.Parse(line ?? throw new Exception("Gateway closed"))!;
            observations.Add(value.DeepClone());
            Check(value["id"]?.GetValue<int>() == id, "Response lost, duplicated or reordered");
            return value;
        }
        string Call(int id) => Json(new { jsonrpc = "2.0", id, method = "tools/call", @params = new { name = "get_connection_status", arguments = new { } } });
        var successorPid = 0;
        try
        {
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Json(new { jsonrpc = "2.0", id = 1, method = "initialize",
                @params = new { protocolVersion = "2025-11-25", clientInfo = new { name = "C# isolated fixture", version = "1" }, capabilities = new { } } }));
            await Read(1);
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(2));
            Check((await Read(2))["result"]?["structuredContent"]?["generation"]?.GetValue<string>() == "one", "Initial worker missing");
            Select(first, "two");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(3));
            Check((await Read(3))["result"]?["structuredContent"]?["generation"]?.GetValue<string>() == "two", "Worker did not switch on same connection");
            Select(second with { Arguments = ["--invalid"] }, "not_admitted");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(30));
            var refused = (await Read(30))["result"]?["structuredContent"]?["error"];
            Check(refused?["code"]?.GetValue<string>() == "transport_update_failed" && refused["execution"]?.GetValue<string>() == "not_started",
                "A failed precommit successor admitted work");
            Check(!parent.HasExited && File.ReadAllLines(count).Length == 2, "Parent recovery duplicated work or lost ownership");
            Select(first, "recovered");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Json(new { jsonrpc = "2.0", id = 31, method = "ping" }));
            Check((await Read(31))["result"] is not null, "Parent could not resume after refusal");
            Select(second, "three");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(4) + "\n" + Call(5) + "\n" +
                Json(new { jsonrpc = "2.0", id = 6, method = "tools/list" }));
            Check((await Read(4))["result"]?["structuredContent"]?["generation"]?.GetValue<string>() == "three", "Successor did not dispatch pending frame");
            Check((await Read(5))["result"]?["structuredContent"]?["generation"]?.GetValue<string>() == "three", "Queued unread frame lost");
            var tools = (await Read(6))["result"]?["tools"]?.AsArray();
            Check(tools?.Count == 4, "Stable tool surface changed");
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(parent.ExitCode == 0, "Parent did not release original handles cleanly");
            var active = Directory.GetFiles(Path.Combine(state, "succession"), "*.active").Single();
            successorPid = int.Parse(File.ReadAllText(active));
            Check(successorPid != parent.Id, "No process succession occurred");
            Select(second, "lost", "lose");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(7));
            var lost = (await Read(7))["result"]?["structuredContent"]?["error"];
            Check(lost?["code"]?.GetValue<string>() == "mcp_transport_lost" && lost["execution"]?.GetValue<string>() == "unconfirmed", "Lost response incorrectly declared not started");
            Check(File.ReadAllLines(count).Length == 5, "Lost request was replayed");
            Select(second, "reconciled");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(8));
            Check((await Read(8))["result"]?["structuredContent"]?["calls"]?.GetValue<int>() == 6, "Retained fixture result/counter inaccessible after loss");
            parent.StandardInput.Close();
            using var successor = Process.GetProcessById(successorPid);
            await successor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(Path.Combine(data, "protocol.json"), Json(observations));
            File.WriteAllText(Path.Combine(data, "stderr.log"), await errors.WaitAsync(TimeSpan.FromSeconds(5)));
            Console.WriteLine("MINIMAL_MCP_TRANSPORT=PASS (worker switch; C# parent exit/successor; pending/unread frames; unchanged tools; lost response not replayed)");
        }
        finally
        {
            parent.StandardInput.Close();
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(); }
            // The isolated successor identity came only from this fixture's private state.
            if (successorPid == 0 && Directory.Exists(Path.Combine(state, "succession")))
                foreach (var active in Directory.GetFiles(Path.Combine(state, "succession"), "*.candidate")) successorPid = int.Parse(File.ReadAllText(active));
            if (successorPid != 0)
            {
                try
                {
                    using var successor = Process.GetProcessById(successorPid);
                    if (!successor.HasExited && string.Equals(successor.MainModule?.FileName, second.Executable, StringComparison.OrdinalIgnoreCase)) successor.Kill();
                }
                catch (ArgumentException) { }
            }
            File.WriteAllText(Path.Combine(data, "protocol.json"), Json(observations));
            if (errors.IsCompleted) File.WriteAllText(Path.Combine(data, "stderr.log"), await errors);
        }
    }

    private static McpProcessImage Bundle(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        return new(Path.Combine(destination, "AgentRelay.Transport.exe"), [], Files(destination));
    }

    private static Dictionary<string, string> Files(string root) => Directory.GetFiles(root)
        .Where(path => !path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
        .ToDictionary(path => Path.GetFileName(path), path => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))));
}
