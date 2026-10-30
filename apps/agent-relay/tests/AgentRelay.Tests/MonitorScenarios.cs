using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class MonitorScenarios
{
    public static void Run()
    {
        var project = Guid.NewGuid(); var other = Guid.NewGuid(); var owner = Guid.NewGuid(); var endpoint = Guid.NewGuid(); var task = Guid.NewGuid(); var session = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
        var first = new WorkRecord(new(Guid.NewGuid(), "arbitrary", "message", "A", SessionId: session, Scope: new(project, owner, endpoint), TaskId: task), "completed", now, now);
        var second = first with { Request = first.Request with { Id = Guid.NewGuid(), Text = "B" }, State = "awaiting_approval" };
        var independent = new InboxMessage(Guid.NewGuid(), "arbitrary", "sender", "recipient", "Independent", null, now, session, project, endpoint, owner);
        var reply = independent with { Id = Guid.NewGuid(), InReplyTo = first.Request.Id };
        var foreign = reply with { Id = Guid.NewGuid(), ProjectId = other };
        var wrongSession = reply with { Id = Guid.NewGuid(), SessionId = Guid.NewGuid() };
        var aggregate = MonitorProjection.Tasks([first, second], [independent, reply, foreign, wrongSession]).Single();
        if (aggregate.Operations.Count != 2 || aggregate.Messages.Count != 1 || aggregate.State != "awaiting_approval") throw new Exception("Task aggregate leaked or invented message linkage.");
        if (MonitorProjection.Timeline(aggregate).Last().Message?.Id != reply.Id) throw new Exception("Same-time reply precedes its request.");
        if (MonitorProjection.AggregateState(["running", "unknown"]) != "unknown" || !MonitorProjection.NeedsAttention("unknown")) throw new Exception("Uncertain work hidden by ordinary activity.");
        if (MonitorProjection.Tasks([first, first with { Request = first.Request with { Id = Guid.NewGuid(), Scope = new(other, owner, endpoint) } }], []).Count != 2) throw new Exception("Same task ID crossed project boundary.");
        var config = new ProjectConfiguration(1, [new(project, "A"), new(other, "B")],
            [new(owner, "Principal", "arbitrary", "private-binding-reference"), new(endpoint, "Participant", "arbitrary", "private-binding-reference")], [],
            [new(project, owner, "main", [RelayCapabilities.ReadHistory], []), new(project, endpoint, "secondary", [], [])]);
        var a = new AgentRelay.Windows.Monitoring.ProjectObservation(project); a.Operations.Add(first); a.Messages.Add(independent); a.Messages.Add(reply);
        a.Sessions.Add(new(session, "Conversation", new(project, owner, endpoint), "created"));
        var b = new AgentRelay.Windows.Monitoring.ProjectObservation(other); b.Operations.Add(first with { Request = first.Request with { Id = Guid.NewGuid(), Scope = new(other, owner, endpoint) } });
        var snapshot = AgentRelay.Windows.Presentation.PresentationSnapshot.Build(config,
            [new(owner, "External client", "Custom transport"), new(endpoint, "Future connector", "Remote connection", Capabilities: [RelayCapabilities.ManageSession, RelayCapabilities.SendMessage, "session.inspect"])], [a, b], true, null, now);
        if (snapshot.GetProperty("tasks").EnumerateArray().Select(item => item.GetProperty("id").GetString()).Distinct().Count() != 2)
            throw new Exception("Presentation merged identical task IDs from different projects.");
        if (snapshot.GetProperty("messages")[0].GetProperty("taskId").ValueKind != System.Text.Json.JsonValueKind.Null)
            throw new Exception("Presentation assigned a fabricated task to an independent report.");
        if (snapshot.GetRawText().Contains("private-binding-reference", StringComparison.Ordinal)) throw new Exception("Presentation exposed a private endpoint binding.");
        if (snapshot.GetProperty("sessions")[0].GetProperty("capabilities").EnumerateArray().Any(item => item.GetString() == "sessions.manage"))
            throw new Exception("Main role silently gained connector session-management authority.");
        var historical = first with { Metadata = new("Command", "/actual/submission-folder"), Result = new("command", "files", Details: new Dictionary<string, string> { ["program"] = "/bin/list", ["arguments"] = "[\"two words\"]" }) };
        var detail = System.Text.Json.JsonSerializer.SerializeToElement(AgentRelay.Windows.Presentation.PresentationSnapshot.Operation(historical, config), RelayJson.Options);
        if (detail.GetProperty("workspace").GetString() != "/actual/submission-folder" || detail.GetProperty("arguments")[0].GetString() != "two words")
            throw new Exception("Presentation replaced historical execution context or split a command argument.");
        Console.WriteLine("PASS: monitoring projection preserves project/session linkage, independent reports, timeline ordering and uncertainty");
    }

    // Explicit test-tool entry point: persisted fixtures never enter the production UI assembly.
    public static async Task Seed(string data)
    {
        if (Directory.Exists(data) && Directory.EnumerateFileSystemEntries(data).Any()) throw new Exception("Fixture destination must be empty.");
        var project = Guid.NewGuid(); var other = Guid.NewGuid(); var owner = Guid.NewGuid(); var endpoint = Guid.NewGuid(); var observer = Guid.NewGuid(); var workspace = Guid.NewGuid(); var session = Guid.NewGuid();
        var config = new ProjectConfiguration(1, [new(project, "Site Atelier · qualification"), new(other, "API Catalogue · qualification")],
            [new(owner, "Pilote", "local", "principal"), new(endpoint, "Création", "qualification-connector", "creator"), new(observer, "Observation", "consultation", "observer")],
            [new(workspace, endpoint, "Création", "memory://atelier/interface")],
            [new(project, owner, "main", [RelayCapabilities.ReadHistory, RelayCapabilities.ReceiveMessage], []), new(project, endpoint, "secondary", [RelayCapabilities.ReceiveMessage], []), new(other, observer, "observer", [RelayCapabilities.ReadHistory], [])]);
        new ProjectSettingsStore(data).Save(config);
        var database = new RelayDatabase(data); var store = new SqliteWorkStore(database); var now = DateTimeOffset.UtcNow;
        var tasks = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var ids = new List<Guid>();
        for (var i = 0; i < 65; i++)
        {
            var id = Guid.NewGuid(); ids.Add(id); var at = now.AddMinutes(-i * 12);
            var work = new WorkRecord(new(id, endpoint.ToString("D"), i % 3 == 0 ? "command" : "message", i == 0 ? "Préparer la page d’accueil et vérifier les contenus" : "Vérification du contenu " + i,
                CommandId: i % 3 == 0 ? "list-files" : null, SessionId: session, Scope: new(i % 4 == 0 ? other : project, owner, endpoint, workspace), TaskId: tasks[i % 3]),
                i == 0 ? "awaiting_approval" : i == 1 ? "unknown" : "completed", at, at,
                Result: i > 1 ? new("qualification", "Résultat conservé : contenu vérifié.\nAucune commande ni aucun modèle n’a été appelé pour cette fixture.") : null,
                Metadata: new("Qualification de l’interface", "memory://atelier/interface"));
            await store.SaveAsync(work, CancellationToken.None);
            if (i is 1 or 2) await store.AddInboxAsync(new(Guid.NewGuid(), "qualification", "Création", "Pilote", "Réponse liée à la demande conservée.", id, at.AddSeconds(1), session, project, endpoint, owner), CancellationToken.None);
        }
        await store.AddInboxAsync(new(Guid.NewGuid(), "qualification", "Création", "Pilote", "Message indépendant : une décision reste à confirmer avant de poursuivre.\n\n" + string.Join("\n", Enumerable.Repeat("Les contenus longs restent sélectionnables et copiables dans le panneau de détail.", 30)), null, now, session, project, endpoint, owner), CancellationToken.None);
        var template = new WslProfile("fixture", "Fixture non exécutable", "Unavailable", "nobody", "/srv/agent-relay-ui-fixture", "command", []);
        var definition = new SessionDefinition(session, "Interface et contenus", new(project, owner, endpoint, workspace), config.Endpoints[1], config.Workspaces[0], false);
        await new SqliteDocuments<RelaySession>(database, "sessions").SaveAsync(session, new(session, definition.Name, "fixture", template, template.Root, "closed", now, now, Definition: definition), CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(data, "qualification.json"), RelayJson.Serialize(new { project, other, session, syntheticRetainedFacts = true, nativeCalls = 0 }));
        Console.WriteLine("MONITOR_FIXTURE=PASS " + data);
    }
}
