using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class DesignScenarios
{
    public static void Contracts(string corpus)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(corpus));
        var value = document.RootElement;
        var id = value.GetProperty("id").GetGuid();
        Check(NativeLayout.Session(id) == value.GetProperty("session").GetString(), "current layout construction");
        Check(NativeLayout.Session(id, true) == value.GetProperty("legacySession").GetString(), "legacy layout construction");
        foreach (var name in new[] { "session", "legacySession" }) Check(NativeLayout.IsSession(value.GetProperty(name).GetString()!, id), "owned session recognition");
        foreach (var item in value.GetProperty("valid").EnumerateArray()) Check(NativeLayout.IsRoot(item.GetString()!), "valid root");
        foreach (var item in value.GetProperty("invalid").EnumerateArray()) Check(!NativeLayout.IsRoot(item.GetString()!), "invalid root");
        Check(SessionNames.Normalize("  Étude — 中文 😀  ") == "Étude — 中文 😀", "Unicode name normalization");
        try { SessionNames.Normalize(new string('x', SessionNames.MaximumLength + 1)); throw new Exception("Long name accepted"); }
        catch (RelayException error) when (error.Code == "invalid_session") { }
        var failure = NativeFailures.Read("{\"error\":\"arbitrary prose\",\"code\":\"capability_denied\",\"effectsPossible\":false}", "");
        Check(failure.Code == "capability_denied" && failure.EffectsPossible == false, "typed native refusal");
        var approval = NativeFailures.Read("{\"error\":\"approval\",\"code\":\"approval_required\",\"effectsPossible\":null}", "");
        Check(approval.Code == "approval_required" && approval.EffectsPossible is null, "approval retains its distinct state without promising no effects");
        Check(NativeFailures.Read("{\"error\":\"unavailable: untyped legacy diagnostic\"}", "").Code == "wsl_error", "legacy prose never determines code");
        var tools = JsonSerializer.SerializeToElement(McpFacade.ToolDefinitions, RelayJson.Options).EnumerateArray().ToArray();
        foreach (var name in new[] { "open_session", "rename_session" })
            Check(tools.Single(tool => tool.GetProperty("name").GetString() == name).GetProperty("inputSchema").GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32() == SessionNames.MaximumLength, "schema uses name policy");
        McpRoutes(tools);
        Console.WriteLine("DESIGN_CONTRACTS=PASS");
    }

    private static void McpRoutes(JsonElement[] tools)
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        object Example(string key, JsonElement schema) => schema.GetProperty("type").GetString() switch
        {
            "object" => schema.GetProperty("properties").EnumerateObject().ToDictionary(p => p.Name, p => Example(p.Name, p.Value)),
            "integer" => 2,
            "boolean" => true,
            "string" when schema.TryGetProperty("format", out _) => key == "request_id" ? other : id,
            "string" when key is "since" or "until" => "2026-11-05T12:00:00+00:00",
            "string" => "test",
            _ => throw new InvalidOperationException("Unsupported fixture schema")
        };
        var routes = new Dictionary<string, ProjectAction>(StringComparer.Ordinal);
        foreach (var tool in tools)
        {
            var name = tool.GetProperty("name").GetString()!;
            var args = JsonSerializer.SerializeToElement(Example(name, tool.GetProperty("inputSchema")));
            var resolved = McpToolCatalog.Resolve(name, args);
            Check(resolved.AllowStaleCatalog == (name is "list_projects" or "get_connection_status"), "catalog refresh exception");
            if (resolved.Route is { } route) routes.Add(name, route(new(args)));
            else Check(name == "get_connection_status", "only connection status is local");
        }
        Check(routes.Count == tools.Length - 1, "every advertised project tool routes");
        foreach (var (name, action) in routes)
        {
            var expected = name switch
            {
                "list_projects" or "list_profiles" => "configuration",
                "get_agent_status" => "get_session",
                "send_session_message" or "send_agent_message" => "send_message",
                "rename_session" or "start_session" or "start_agent" or "reopen_session" or "interrupt_session" or "close_session" => "control_session",
                _ => name
            };
            Check(action.Name == expected, "wrong host action for " + name);
        }
        foreach (var (canonical, alias) in new[] { ("list_projects", "list_profiles"), ("get_session", "get_agent_status"),
            ("start_session", "start_agent"), ("send_session_message", "send_agent_message") })
            Check(routes[canonical] == routes[alias], "alias routes exactly like its canonical tool");
        Check(routes["send_session_message"] is { Name: "send_message", Text: "test" } message && message.SessionId == id && message.RequestId == other && message.TaskId == id, "message argument mapping");
        Check(routes["close_session"] is { Name: "control_session", Control: "close" } close && close.SessionId == id, "close argument mapping");
        Check(routes["list_results"].Query is { Limit: 2, Cursor: 2 } query && query.ProjectId == id && query.Since?.Offset == TimeSpan.Zero, "query argument mapping");
        var transferArgs = JsonSerializer.SerializeToElement(new { session_id = id, source = new { endpoint_id = id, workspace_id = id, path = "source" },
            destination = new { endpoint_id = other, workspace_id = other, path = "destination" }, request_id = other, replace = true });
        var transfer = McpToolCatalog.Resolve("transfer_file", transferArgs).Route!(new(transferArgs));
        Check(transfer.Transfer == new ArtifactTransfer(new(id, id, "source"), new(other, other, "destination"), true), "transfer preserves source and destination");
        try { McpToolCatalog.Resolve("get_session", JsonSerializer.SerializeToElement(new { session_id = id.ToString("N") })); throw new InvalidOperationException("Unreadable UUID accepted"); }
        catch (RelayException error) when (error.Code == "invalid_request") { }
    }

    public static async Task Seed(string data, bool includeFailures = false)
    {
        if (Directory.Exists(data)) throw new IOException("Use a fresh explicit fixture directory.");
        Directory.CreateDirectory(data);
        var project = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var peer = Guid.NewGuid();
        var workspace = Guid.NewGuid();
        var session = Guid.NewGuid();
        var connection = Guid.NewGuid();
        var settings = new ProjectSettingsStore(data);
        var config = new ProjectConfiguration(1, [new(project, "Étude — 中文 😀")],
            [new(owner, "Principal", "local-files", "main"), new(peer, "Peer", "wsl-agent", "fixture")],
            [new(workspace, peer, "Fixture", "/not-an-executed-workspace")],
            [new(project, owner, "main", [RelayCapabilities.ManageSession, RelayCapabilities.ReadHistory, RelayCapabilities.SendMessage, RelayCapabilities.ReadFile], [new(workspace, true, false, false)]),
             new(project, peer, "secondary", [RelayCapabilities.ReceiveMessage], [new(workspace, true, false, false)])]);
        settings.Save(config);
        var template = new WslProfile("fixture", "Never executed", "Unavailable", "nobody", RuntimePaths.NativeSession(session), "codex", []);
        new SettingsStore(data).Save(new(1, [template]));
        var definition = new SessionDefinition(session, "Initial", new(project, owner, peer, workspace), config.Endpoints[1], config.Workspaces[0], false);
        await new SqliteDocuments<RelaySession>(new(data), "sessions").SaveAsync(session,
            new(session, definition.Name, "fixture", template, template.Root, "closed", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Definition: definition), CancellationToken.None);
        await new ConnectionStore(data).CreateAsync(connection, owner, [project], false, CancellationToken.None);
        var errors = new Dictionary<string, Guid>();
        if (includeFailures)
        {
            var store = new SqliteWorkStore(new RelayDatabase(data));
            foreach (var (code, effects) in new (string, bool?)[] { ("approval_required", null), ("reply_unconfirmed", true) })
            {
                var requestId = Guid.NewGuid();
                errors.Add(code, requestId);
                var request = new WorkRequest(requestId, peer.ToString("D"), "message", "fixture", SessionId: session,
                    Scope: definition.Scope, TaskId: requestId, AttemptId: requestId);
                await WorkScenarios.RetainNativeFailureAsync(store, request, RelayJson.Serialize(new { error = "Fixture failure", code, effectsPossible = effects }));
            }
        }
        await File.WriteAllTextAsync(Path.Combine(data, "design-fixture.json"), RelayJson.Serialize(new { project, session, connection, errors, nativeCalls = 0 }));
        Console.WriteLine("DESIGN_FIXTURE=PASS");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
