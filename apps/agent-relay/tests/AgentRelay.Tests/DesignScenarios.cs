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
        Console.WriteLine("DESIGN_CONTRACTS=PASS");
    }

    public static async Task Seed(string data)
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
        await File.WriteAllTextAsync(Path.Combine(data, "design-fixture.json"), RelayJson.Serialize(new { project, session, connection, nativeCalls = 0 }));
        Console.WriteLine("DESIGN_FIXTURE=PASS");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }
}
