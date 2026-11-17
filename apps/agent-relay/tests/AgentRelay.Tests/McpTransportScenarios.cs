using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
        if (behavior == "admission")
        {
            File.WriteAllText(countFile + ".attempt", "contender started");
            new McpDeploymentAdmission(countFile).Suspend(generation, () => { });
            return;
        }
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
                if (behavior == "hold")
                {
                    var deadline = DateTime.UtcNow.AddSeconds(20);
                    while (!File.Exists(countFile + ".release") && DateTime.UtcNow < deadline) await Task.Delay(20);
                    if (!File.Exists(countFile + ".release")) throw new TimeoutException("Fixture work was not released.");
                }
                result = new { structuredContent = new { generation, pid = Environment.ProcessId, calls = File.ReadAllLines(countFile).Length } };
            }
            await McpFrames.WriteAsync(output, Json(new { jsonrpc = "2.0", id, result }));
        }
    }

    public static async Task RunAsync(string data, string gatewaySource)
    {
        using (var oversized = new MemoryStream(new byte[McpWireLimits.MaximumFrameBytes + 1]))
        {
            var frames = new McpFrames(oversized);
            try { await frames.ReadAsync(); throw new Exception("Oversized EOF accepted"); }
            catch (McpProtocolException error) when (error.Code == "invalid_request") { }
            Check(await frames.ReadAsync() is null, "Malformed EOF caused an infinite error loop");
        }
        Directory.CreateDirectory(data);
        var first = Bundle(gatewaySource, Path.Combine(data, "gateway-a"));
        var second = Bundle(gatewaySource, Path.Combine(data, "gateway-b"));
        var count = Path.Combine(data, "calls.txt");
        var testExe = Path.Combine(AppContext.BaseDirectory, "AgentRelay.Tests.exe");
        await VerifyAdmissionBoundaryAsync(Path.Combine(data, "boundary"), testExe);
        McpProcessImage Worker(string generation, string behavior = "ok") => new(testExe,
            ["transport-fixture", generation, count, behavior], Files(Path.GetDirectoryName(testExe)!));
        var selectionPath = Path.Combine(data, "selection.json");
        var state = McpDeploymentAdmission.StateDirectoryFor(data);
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
            var admission = new McpDeploymentAdmission(data);
            Select(first, "inflight", "hold");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(21));
            var workDeadline = DateTime.UtcNow.AddSeconds(20);
            while (!File.ReadAllLines(count).Contains("inflight") && DateTime.UtcNow < workDeadline) await Task.Delay(20);
            Check(File.ReadAllLines(count).Contains("inflight"), "Admitted fixture work did not start");
            admission.Suspend("fixture-deployment", () => { });
            File.WriteAllText(count + ".release", "finish admitted work");
            Check((await Read(21))["result"]?["structuredContent"]?["generation"]?.GetValue<string>() == "inflight", "Deployment interrupted admitted work");
            var callsBefore = File.ReadAllLines(count).Length;
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Json(new { jsonrpc = "2.0", id = 22, method = "tools/call", @params = new { name = "invoke_operation", arguments = new { } } }));
            var refusal = (await Read(22))["result"]?["structuredContent"]?["error"];
            Check(refusal?["code"]?.GetValue<string>() == "deployment_in_progress" && refusal["deploymentId"]?.GetValue<string>() == "fixture-deployment" &&
                refusal["execution"]?.GetValue<string>() == "not_started" && File.ReadAllLines(count).Length == callsBefore, "New operation passed the barrier or was replayed");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(23));
            Check((await Read(23))["result"]?["structuredContent"]?["admissionSuspended"]?.GetValue<bool>() == true, "Deployment control required a worker");
            using (var cold = Process.Start(start) ?? throw new Exception("Cold client start failed"))
            {
                var coldErrors = cold.StandardError.ReadToEndAsync();
                var coldReplies = new McpFrames(cold.StandardOutput.BaseStream);
                await McpFrames.WriteAsync(cold.StandardInput.BaseStream, Json(new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { clientInfo = new { name = "blocked client", version = "1" } } }));
                var blocked = JsonNode.Parse(await coldReplies.ReadAsync().WaitAsync(TimeSpan.FromSeconds(15)) ?? throw new Exception("Missing cold refusal"));
                Check(blocked?["error"]?["data"]?["code"]?.GetValue<string>() == "deployment_in_progress", "New MCP initialization was admitted during deployment");
                Check(McpTransportPresences.Read(state).Any(item => !item.Admitted && item.Activity == "blocked" && item.Client == "blocked client"), "Refused connection not identifiable");
                cold.StandardInput.Close();
                await cold.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
                Check(cold.ExitCode == 0, "Refused client did not close cleanly: " + await coldErrors);
            }
            var identified = McpTransportPresences.Read(state).Single(item => item.ProcessId == parent.Id);
            Check(identified.Admitted && identified.ConnectedAt is not null && identified.ClientVersion == "1" && identified.Client == "C# isolated fixture", "Connection metadata incomplete");
            admission.Release("fixture-deployment");
            Select(first, "two");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(3));
            Check((await Read(3))["result"]?["structuredContent"]?["generation"]?.GetValue<string>() == "two", "Worker did not switch on same connection");
            Select(second with { Arguments = ["--invalid"] }, "not_admitted");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(30));
            var refused = (await Read(30))["result"]?["structuredContent"]?["error"];
            Check(refused?["code"]?.GetValue<string>() == "transport_update_failed" && refused["execution"]?.GetValue<string>() == "not_started",
                "A failed precommit successor admitted work");
            Check(!parent.HasExited && File.ReadAllLines(count).Length == callsBefore + 1, "Parent recovery duplicated work or lost ownership");
            Select(first, "recovered");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Json(new { jsonrpc = "2.0", id = 31, method = "ping" }));
            Check((await Read(31))["result"] is not null, "Parent could not resume after refusal");
            var split = Call(4);
            var half = split.Length / 2;
            await parent.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(split[..half]));
            await parent.StandardInput.BaseStream.FlushAsync();
            var partialDeadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (!McpTransportPresences.Read(state).Any(item => item.BufferedInputBytes > 0) && DateTimeOffset.UtcNow < partialDeadline) await Task.Delay(50);
            Check(McpTransportPresences.Read(state).Any(item => item.BufferedInputBytes > 0), "Partial input was not retained by the reader");
            Select(second, "three");
            await parent.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Check(parent.ExitCode == 0, "Idle parent failed to hand over without another client frame");
            await parent.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(split[half..] + "\n" + Call(5) + "\n" +
                Json(new { jsonrpc = "2.0", id = 6, method = "tools/list" }) + "\n"));
            await parent.StandardInput.BaseStream.FlushAsync();
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
            Check(File.ReadAllLines(count).Length == callsBefore + 4, "Lost request was replayed");
            Select(second, "reconciled");
            await McpFrames.WriteAsync(parent.StandardInput.BaseStream, Call(8));
            Check((await Read(8))["result"]?["structuredContent"]?["calls"]?.GetValue<int>() == callsBefore + 5, "Retained fixture result/counter inaccessible after loss");
            parent.StandardInput.Close();
            using var successor = Process.GetProcessById(successorPid);
            await successor.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(Path.Combine(data, "protocol.json"), Json(observations));
            File.WriteAllText(Path.Combine(data, "stderr.log"), await errors.WaitAsync(TimeSpan.FromSeconds(5)));
            Console.WriteLine("MINIMAL_MCP_TRANSPORT=PASS (cross-process admission; retained barrier; cold client refusal; inflight drain; local control; identified clients; worker/successor switch; no replay)");
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

    private static async Task VerifyAdmissionBoundaryAsync(string data, string testExe)
    {
        Directory.CreateDirectory(data);
        var admission = new McpDeploymentAdmission(data);
        var boundary = admission.Acquire();
        var start = new ProcessStartInfo(testExe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var value in new[] { "transport-fixture", "owned-deployment", data, "admission" }) start.ArgumentList.Add(value);
        using var contender = Process.Start(start) ?? throw new Exception("Admission contender failed");
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!File.Exists(data + ".attempt") && DateTime.UtcNow < deadline) await Task.Delay(20);
            Check(File.Exists(data + ".attempt") && !admission.Suspended && !contender.HasExited, "Closing admission bypassed an ongoing connection registration");
        }
        finally { boundary.Dispose(); }
        await contender.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Check(contender.ExitCode == 0 && admission.DeploymentId == "owned-deployment", "Cross-process admission closure failed");
        var resumed = new McpDeploymentAdmission(data);
        try { resumed.DemandOpen(); throw new Exception("Process exit reopened admission"); }
        catch (McpDeploymentException error) { Check(error.DeploymentId == "owned-deployment", "Retained barrier identity changed"); }
        try { resumed.Release("another-deployment"); throw new Exception("Foreign transaction reopened admission"); }
        catch (IOException) { }
        resumed.Release("owned-deployment");
        using (resumed.Acquire()) resumed.DemandOpen();
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
