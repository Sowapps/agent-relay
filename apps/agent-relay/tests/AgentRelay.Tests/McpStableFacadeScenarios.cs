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
        var peer = new RelayEndpoint(Guid.NewGuid(), "Isolated peer", "wsl-agent", "fixture-peer");
        var workspace = new RelayWorkspace(Guid.NewGuid(), peer.Id, "Fixture", "/fixture");
        var scope = new OperationScope(project, endpoint, peer.Id, workspace.Id);
        var session = Guid.NewGuid();
        var binding = Guid.NewGuid();
        new ProjectSettingsStore(data).Save(new(1, [new(project, "Visible"), new(Guid.NewGuid(), "Hidden")],
            [new(endpoint, "Caller", "external", "fixture"), peer], [workspace],
            [new(project, endpoint, "main", [RelayCapabilities.ReadHistory, RelayCapabilities.ManageSession], []),
             new(project, peer.Id, "secondary", [], [])]));
        var connections = new ConnectionStore(data);
        await connections.CreateAsync(binding, endpoint, [project], false, CancellationToken.None);
        var request = Guid.NewGuid();
        var awaiting = Guid.NewGuid();
        var database = new RelayDatabase(data);
        var store = new SqliteWorkStore(database);
        var now = DateTimeOffset.UtcNow;
        var profile = new WslProfile(peer.BindingId, "Fixture", "unused", "unused", "/fixture", "codex", []);
        await new SqliteDocuments<RelaySession>(database, "sessions").SaveAsync(session,
            new(session, "Fixture", profile.Id, profile, profile.Root, "created", now, now,
                Definition: new(session, "Fixture", scope, peer, workspace, false)), CancellationToken.None);
        var submitted = new WorkRequest(request, peer.Id.ToString("D"), "message", "retained fixture", SessionId: session, Scope: scope);
        await store.SaveAsync(new(submitted, "completed", now, now), CancellationToken.None);
        await store.SaveAsync(new(submitted with { Id = awaiting }, "awaiting_approval", now, now, ErrorCode: "approval_required"), CancellationToken.None);
        var due = Guid.NewGuid();
        var oldWait = now.AddHours(-3);
        await store.SaveAsync(new(submitted with { Id = due }, "awaiting_approval", oldWait, oldWait), CancellationToken.None);
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
        Task<JsonElement> Cancel(Guid id) => Call("invoke_operation", new { call_id = Guid.NewGuid(),
            operation_id = "cancel_operation", revision = 1, arguments = new { request_id = id } });
        Check(Error(await Cancel(request)) == "operation_already_finished", "Finished cancellation refusal survives MCP");
        var cancellation = await Cancel(awaiting);
        var progress = cancellation.GetProperty("cancellation");
        Check(cancellation.GetProperty("operation").GetProperty("state").GetString() == "awaiting_approval" &&
            progress.GetProperty("sessionId").GetGuid() == session && progress.GetProperty("state").GetString() == "close_required" &&
            progress.GetProperty("nextAction").GetString() == "close_session" && progress.GetProperty("sessionRequiresResolution").GetBoolean(), "MCP cancellation identifies the session and action");
        using (var humanResult = JsonDocument.Parse(observations[^1].GetProperty("content")[0].GetProperty("text").GetString()!))
            Check(humanResult.RootElement.GetProperty("cancellation").GetProperty("nextAction").GetString() == "close_session", "Text content includes the cancellation continuation");
        // The isolated native test supplies stop proof separately; exercise the persisted outcome through the same MCP path.
        Check(await store.BeginApprovalStopAsync(awaiting, CancellationToken.None), "Approval stop intent was not retained");
        var stopping = (await Call("get_result", new { request_id = awaiting })).GetProperty("cancellation");
        Check(stopping.GetProperty("state").GetString() == "stop_unconfirmed" && stopping.GetProperty("sessionRequiresResolution").GetBoolean(),
            "MCP must distinguish a closure attempt from the initial cancellation");
        await store.RecordApprovalSessionClosureAsync(awaiting, true, CancellationToken.None);
        var confirmed = await Call("get_result", new { request_id = awaiting });
        progress = confirmed.GetProperty("cancellation");
        Check(confirmed.GetProperty("operation").GetProperty("state").GetString() == "unknown" &&
            progress.GetProperty("state").GetString() == "stop_confirmed" && progress.GetProperty("nextAction").ValueKind == JsonValueKind.Null &&
            !progress.GetProperty("sessionRequiresResolution").GetBoolean(), "MCP distinguishes confirmed stop from historical effects");
        using (var expiryDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
            while ((await store.FindAsync(due, expiryDeadline.Token))?.State == "awaiting_approval")
                await Task.Delay(100, expiryDeadline.Token);
        var expiry = await Call("get_result", new { request_id = due });
        Check(expiry.GetProperty("operation").GetProperty("approvalExpiredAt").GetDateTimeOffset() == oldWait + ApprovalExpiry.WaitingLimit &&
              expiry.GetProperty("cancellation").GetProperty("nextAction").GetString() == "close_session" &&
              expiry.GetProperty("cancellation").GetProperty("state").GetString() == "approval_expired",
            "Host startup did not expire a retained approval, or MCP omitted its recovery");
        Check(Error(await Call("invoke_operation", new { call_id = Guid.NewGuid(), operation_id = "list_projects", revision = 1, arguments = new { } }, "wrong")) == "transport_incompatible", "Incompatible wire admission");
        await connections.RevokeAsync(binding, CancellationToken.None);
        Check(Error(await Call("get_connection_status", new { })) == "authentication_required", "Revoked credential access");
        File.WriteAllText(Path.Combine(data, "qualification.json"), RelayJson.Serialize(observations));
        Console.WriteLine("STABLE_MCP_FACADE=PASS (scoped discovery/invocation, cancellation continuation and confirmed stop, wire mismatch, revocation; no native/model calls)");
    }
}
