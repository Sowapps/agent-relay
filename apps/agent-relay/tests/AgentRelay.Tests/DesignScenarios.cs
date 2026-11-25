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
        var operations = McpToolCatalog.Operations;
        foreach (var name in new[] { "open_session", "rename_session" })
            Check(operations.Single(operation => operation.Id == name).InputSchema.GetProperty("properties").GetProperty("name").GetProperty("maxLength").GetInt32() == SessionNames.MaximumLength, "schema uses name policy");
        McpRoutes();
        Console.WriteLine("DESIGN_CONTRACTS=PASS");
    }

    private static void McpRoutes()
    {
        var id = Guid.NewGuid();
        var other = Guid.NewGuid();
        ProjectAction Resolve(string name, object arguments) =>
            McpToolCatalog.ResolveOperation(new(id, name, 1, JsonSerializer.SerializeToElement(arguments)));

        var session = new { session_id = id };
        var message = new { session_id = id, request_id = other, text = "test", task_id = id };
        Check(Resolve("list_projects", new { }) == Resolve("list_profiles", new { }), "project discovery alias");
        Check(Resolve("get_session", session) == Resolve("get_agent_status", session), "session lookup alias");
        Check(Resolve("start_session", session) == Resolve("start_agent", session), "session start alias");
        var sent = Resolve("send_session_message", message);
        Check(sent == Resolve("send_agent_message", message) && sent is { Name: "send_message", Text: "test" } &&
            sent.SessionId == id && sent.RequestId == other && sent.TaskId == id, "message alias and argument mapping");
        Check(Resolve("close_session", session) is { Name: "control_session", Control: "close" } close &&
            close.SessionId == id, "close argument mapping");
        var query = Resolve("list_results", new { project_id = id, limit = 2, cursor = 2, since = "2026-11-05T12:00:00+00:00" }).Query;
        Check(query is { Limit: 2, Cursor: 2 } && query.ProjectId == id && query.Since?.Offset == TimeSpan.Zero, "query argument mapping");
        var transfer = Resolve("transfer_file", new { session_id = id, source = new { endpoint_id = id, workspace_id = id, path = "source" },
            destination = new { endpoint_id = other, workspace_id = other, path = "destination" }, request_id = other, replace = true });
        Check(transfer.Transfer == new ArtifactTransfer(new(id, id, "source"), new(other, other, "destination"), true), "transfer preserves source and destination");
        try { Resolve("get_session", new { session_id = id.ToString("N") }); throw new InvalidOperationException("Unreadable UUID accepted"); }
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
