using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Protocol;

internal static class McpStableFacadeScenarios
{
    public static async Task RunAsync(string data)
    {
        var project = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var binding = Guid.NewGuid();
        new ProjectSettingsStore(data).Save(new(1, [new(project, "Visible"), new(Guid.NewGuid(), "Hidden")],
            [new(endpoint, "Caller", "external", "fixture")], [], [new(project, endpoint, "main", [RelayCapabilities.ReadHistory], [])]));
        var connections = new ConnectionStore(data);
        await connections.CreateAsync(binding, endpoint, [project], false, CancellationToken.None);
        var request = Guid.NewGuid();
        await new SqliteWorkStore(new RelayDatabase(data)).SaveAsync(new(new(request, endpoint.ToString("D"), "message", "retained fixture",
            Scope: new(project, endpoint, endpoint)), "completed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), CancellationToken.None);
        var errors = new List<Exception>();
        await using var host = RelayRuntime.Start(data, errors.Add);
        var client = new RelayPipeClient(data, binding);
        var observations = new List<JsonElement>();
        async Task<JsonElement> Call(string name, object arguments, string? wire = null)
        {
            var parameters = JsonSerializer.SerializeToElement(new { name, arguments,
                _meta = new { agentRelayWireHash = wire ?? McpStableProtocol.WireHash, agentRelayContract = "older application/docs digest" } }, RelayJson.Options);
            var result = JsonSerializer.SerializeToElement(await McpFacade.CallAsync(client, data, binding, parameters, null, true), RelayJson.Options);
            observations.Add(result);
            File.WriteAllText(Path.Combine(data, "qualification.json"), RelayJson.Serialize(observations));
            if (result.TryGetProperty("isError", out var failed) && failed.GetBoolean() && name == "discover_operations" && observations.Count == 1)
                throw new Exception("Initial facade discovery failed: " + result.GetRawText());
            return result.GetProperty("structuredContent");
        }
        static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
        static string? Error(JsonElement value) => value.GetProperty("error").GetProperty("code").GetString();
        var first = await Call("discover_operations", new { limit = 2 });
        Check(first.GetProperty("operations").GetArrayLength() == 2 && first.GetProperty("context").GetProperty("projects").GetArrayLength() == 1, "Discovery scope/paging");
        var hash = first.GetProperty("registryHash").GetString();
        Check((await Call("discover_operations", new { cursor = 2, limit = 2, registry_hash = hash })).TryGetProperty("operations", out _), "Discovery continuation");
        Check(Error(await Call("discover_operations", new { cursor = 2, registry_hash = "changed" })) == "operation_registry_changed", "Registry snapshot conflict");
        var invocation = await Call("invoke_operation", new { call_id = Guid.NewGuid(), operation_id = "list_projects", revision = 1, arguments = new { } });
        Check(invocation.GetProperty("configuration").GetProperty("projects").GetArrayLength() == 1, "Scoped generic invocation");
        Check((await Call("get_result", new { request_id = request })).GetProperty("operation").GetProperty("request").GetProperty("id").GetGuid() == request, "Retained result");
        Check(Error(await Call("invoke_operation", new { call_id = Guid.NewGuid(), operation_id = "list_projects", revision = 1, arguments = new { } }, "wrong")) == "transport_incompatible", "Incompatible wire admission");
        await connections.RevokeAsync(binding, CancellationToken.None);
        Check(Error(await Call("get_connection_status", new { })) == "authentication_required", "Revoked credential access");
        File.WriteAllText(Path.Combine(data, "qualification.json"), RelayJson.Serialize(observations));
        Console.WriteLine("STABLE_MCP_FACADE=PASS (authenticated host, scoped discovery/paging, generic invocation, retained result, wire mismatch, credential revocation; no native/model calls)");
    }
}
