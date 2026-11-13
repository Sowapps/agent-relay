using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Protocol;

internal static class McpContractScenarios
{
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, RelayJson.Options);
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refused(Action action, string code)
    {
        try { action(); throw new Exception("Accepted " + code); }
        catch (RelayException error) when (error.Code == code && error.EffectsPossible == false) { }
        catch (McpProtocolException error) when (error.Code == code) { }
    }

    public static async Task RunAsync(string data)
    {
        var wire = McpStableProtocol.WireHash;
        var schema = Json(new { type = "object", properties = new { text = new { type = "string", maxLength = 40 } }, required = new[] { "text" }, additionalProperties = false });
        var nextSchema = Json(new { type = "object", properties = new { number = new { type = "integer", minimum = 0 } }, required = new[] { "number" }, additionalProperties = false });
        var routed = 0;
        ProjectAction Route(JsonElement args) { routed++; return new("fixture", Text: args.GetProperty("text").GetString()); }
        var old = new McpOperationRegistration("fixture", 1, "Existing", schema, Route);
        var original = new McpOperationRegistry([old]);
        var updated = new McpOperationRegistry([old,
            new("fixture", 2, "New revision", nextSchema, args => new("fixture2", Text: args.GetProperty("number").GetInt32().ToString())),
            new("new_operation", 1, "New capability", schema, Route)]);
        var id = Guid.NewGuid();
        var invocation = McpStableProtocol.Invocation(Json(new { call_id = id, operation_id = "new_operation", revision = 1, arguments = new { text = "hello" } }));
        Check(invocation.CallId == id && updated.Resolve(invocation).Text == "hello", "new operation through unchanged envelope");
        Check(updated.Resolve(new(id, "fixture", 1, Json(new { text = "old" }))).Text == "old", "old revision retained");
        Check(updated.Resolve(new(id, "fixture", 2, Json(new { number = 7 }))).Name == "fixture2", "explicit revision routing");
        Check(wire == McpStableProtocol.WireHash && original.Hash != updated.Hash, "operation change must not change wire contract");
        McpStableProtocol.ValidateRevisionHistory(original.Descriptors, updated.Descriptors);
        var prettySchema = JsonSerializer.SerializeToElement(JsonSerializer.Deserialize<JsonElement>(schema.GetRawText()),
            new JsonSerializerOptions { WriteIndented = true });
        Check(McpStableProtocol.HashJson(schema) == McpStableProtocol.HashJson(prettySchema), "manifest whitespace changed schema identity");
        using (var reordered = JsonDocument.Parse("{\"additionalProperties\":false,\"required\":[\"text\"],\"properties\":{\"text\":{\"maxLength\":40,\"type\":\"string\"}},\"type\":\"object\"}"))
            Check(McpStableProtocol.HashJson(schema) == McpStableProtocol.HashJson(reordered.RootElement), "manifest object order changed schema identity");
        var before = routed;
        Refused(() => updated.Resolve(new(id, "fixture", 99, Json(new { text = "x" }))), "operation_revision_unsupported");
        Refused(() => updated.Resolve(new(id, "missing", 1, Json(new { text = "x" }))), "operation_unknown");
        Refused(() => updated.Resolve(new(id, "fixture", 1, Json(new { text = "x", actor_id = Guid.NewGuid() }))), "invalid_request");
        Refused(() => McpStableProtocol.Invocation(Json(new { call_id = id, operation_id = "fixture", revision = 1, arguments = new { text = "x" }, actor_id = Guid.NewGuid() })), "invalid_request");
        Check(before == routed, "refused calls reached the route");
        var reused = new McpOperationRegistry([new("fixture", 1, "Changed", nextSchema, _ => new("unused"))]);
        Refused(() => McpStableProtocol.ValidateRevisionHistory(original.Descriptors, reused.Descriptors), "operation_revision_reused");
        Refused(() => McpStableProtocol.ValidateRevisionHistory(original.Descriptors,
            [original.Descriptors[0] with { SchemaHash = "forged" }]), "invalid_operation_manifest");
        using (var duplicate = JsonDocument.Parse("{\"request_id\":\"" + id + "\",\"request_id\":\"" + id + "\"}"))
            Refused(() => McpStableProtocol.Validate("get_result", duplicate.RootElement), "invalid_request");
        Check(McpToolCatalog.Operations.Count == McpFacade.ToolDefinitions.Length - 1, "every legacy project route has one registry descriptor");
        var project = Guid.NewGuid();
        var endpoint = Guid.NewGuid();
        var settings = new ProjectSettingsStore(data);
        var configuration = new ProjectConfiguration(1, [new(project, "Contract fixture")],
            [new(endpoint, "Caller", "memory", "fixture")], [],
            [new(project, endpoint, "main", [RelayCapabilities.InspectHost], [])]);
        settings.Save(configuration);
        var store = new SqliteWorkStore(new RelayDatabase(data));
        var adapter = new ProjectApplicationScenarios.MemoryAdapter(store);
        await using var work = new WorkService(store, adapter, () => []);
        var inspector = new Inspector();
        var app = new RelayApplication(new(settings), work, adapter, new ProjectJournal(new RelayDatabase(data)), inspector);
        var caller = new CallerIdentity(endpoint, [project]);
        var action = McpToolCatalog.ResolveOperation(new(id, "inspect_host", 1, Json(new { project_id = project, endpoint_id = endpoint })));
        await app.InspectHostAsync(caller, action.ProjectId!.Value, action.EndpointId!.Value, CancellationToken.None);
        settings.Save(configuration with { Memberships = [configuration.Memberships[0] with { Capabilities = [] }] });
        try
        {
            await app.InspectHostAsync(caller, action.ProjectId.Value, action.EndpointId.Value, CancellationToken.None);
            throw new Exception("Cached discovery bypassed revoked rights");
        }
        catch (RelayException error) when (error.Code == "capability_denied") { }
        Check(inspector.Calls == 1, "revoked request reached the inspector");
        var metadata = McpIdentity.Current.Compatibility ?? throw new Exception("Compatibility metadata absent");
        Check(metadata.Transport == McpStableProtocol.Requirement && metadata.OperationsHash == McpToolCatalog.OperationsHash,
            "manifest does not match its owners");
        Console.WriteLine("STABLE_MCP_CONTRACT=PASS (new/old revision, stable wire, refusal before route, manifest drift, live revocation; no native calls)");
    }

    private sealed class Inspector : IHostInspector
    {
        public int Calls { get; private set; }
        public Task<HostReport> InspectAsync(RelayEndpoint endpoint, CancellationToken token)
        {
            Calls++;
            return Task.FromResult(new HostReport(endpoint.Id, new Dictionary<string, string> { ["source"] = "memory" }));
        }
    }
}
