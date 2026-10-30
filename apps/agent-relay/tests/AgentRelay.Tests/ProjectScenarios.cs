using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class ProjectScenarios
{
    public static async Task Run(string data)
    {
        var projectA = Guid.NewGuid(); var projectB = Guid.NewGuid();
        var main = Guid.NewGuid(); var secondary = Guid.NewGuid(); var other = Guid.NewGuid(); var workspace = Guid.NewGuid();
        var configuration = new ProjectConfiguration(1, [new(projectA, "A"), new(projectB, "B")],
            [new(main, "Main", "memory", "main"), new(secondary, "Secondary", "memory", "secondary"), new(other, "Other", "memory", "other")],
            [new(workspace, secondary, "Work", "memory://work")],
            [new(projectA, main, "main", [RelayCapabilities.SendMessage, RelayCapabilities.ReadHistory, RelayCapabilities.ReadFile], [new(workspace, true)]),
             new(projectA, secondary, "secondary", [RelayCapabilities.ReceiveMessage, RelayCapabilities.SendMessage], []),
             new(projectB, other, "main", [RelayCapabilities.ReceiveMessage], [])]);
        var store = new ProjectSettingsStore(data); store.Save(configuration); var policy = new ProjectPolicy(store);
        var caller = new CallerIdentity(main, [projectA]);
        if (policy.Message(caller, projectA, secondary, workspace).SenderId != main) throw new Exception("Sender identity changed.");
        Refused(() => policy.Message(caller, projectB, other));
        Refused(() => policy.Message(caller, projectA, other));
        Refused(() => policy.Demand(new(other, [projectA]), projectA, RelayCapabilities.SendMessage));
        Refused(() => policy.Workspace(caller, projectA, workspace, write: true));
        Refused(() => policy.Demand(new(secondary, [projectA]), projectA, RelayCapabilities.RunCommand));
        var nativeConfiguration = configuration with { Memberships = configuration.Memberships.Select(member => member.ProjectId == projectA ? member with
            { Capabilities = member.Capabilities.Append(RelayCapabilities.LocalWork).ToArray(), Workspaces = [new(workspace, true, true)] } : member).ToArray() };
        store.Save(nativeConfiguration); policy.LocalWork(caller, projectA, workspace, secondary);
        Refused(() => policy.Transfer(caller, projectA, new(new(secondary, workspace, "source"), new(secondary, workspace, "destination"))));
        store.Save(configuration with { Projects = [new(projectA, "A", false), new(projectB, "B")] });
        Refused(() => policy.Message(caller, projectA, secondary));
        Console.WriteLine("PASS: neutral two-project membership, sender/recipient scope, role-independent grants and revocation");
        var connections = new ConnectionStore(data);
        connections.EnsureAdministrator();
        var identity = Guid.NewGuid(); await connections.CreateAsync(identity, main, [projectA], false, CancellationToken.None);
        var secret = connections.Secret(identity);
        if (File.ReadAllBytes(Path.Combine(data, "connections", identity.ToString("N") + ".bin")).AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(secret)) >= 0)
            throw new Exception("Connection secret was stored unprotected.");
        var authenticated = await connections.AuthenticateAsync(secret, CancellationToken.None);
        if (authenticated.Administrator || authenticated.Caller.EndpointId != main || authenticated.Caller.ProjectIds.Single() != projectA) throw new Exception("Credential scope changed.");
        await connections.RevokeAsync(identity, CancellationToken.None);
        try { await connections.AuthenticateAsync(secret, CancellationToken.None); throw new Exception("Revoked connection accepted."); }
        catch (RelayException error) when (error.Code == "authentication_required") { }
        Console.WriteLine("PASS: Windows protected scoped credential, authenticated identity and revocation");
        var database = new RelayDatabase(data); var workStore = new SqliteWorkStore(database);
        var scope = new OperationScope(projectA, main, secondary, workspace);
        var transfer = new ArtifactTransfer(new(main, workspace, "a.bin"), new(secondary, workspace, "b.bin"));
        var operation = new WorkRequest(Guid.NewGuid(), secondary.ToString("D"), "transfer", SessionId: Guid.NewGuid(), Scope: scope, Artifact: transfer);
        var uncertain = new WorkRecord(operation, "unknown", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ErrorCode: "host_stopped");
        await workStore.SaveAsync(uncertain, CancellationToken.None);
        var receipts = new SqliteDocuments<TransferReceipt>(database, "transfers");
        await receipts.SaveAsync(operation.Id, new(operation.Id, operation.SessionId!.Value, new("upload", "a.bin", "b.bin"), new("file", "verified"), transfer, scope), CancellationToken.None);
        var adapter = new ScopedSessions(data, new(data), store, workStore, _ => false);
        if ((await adapter.ReconcileAsync(uncertain, CancellationToken.None)).State != "completed") throw new Exception("Durable scoped transfer receipt was not recovered.");
        var mismatch = uncertain with { Request = operation with { Scope = scope with { ProjectId = projectB } } };
        if ((await adapter.ReconcileAsync(mismatch, CancellationToken.None)).State != "unknown") throw new Exception("Foreign receipt was accepted.");
        Console.WriteLine("PASS: scoped transfer recovery confirms a matching durable receipt without replay, refuses a foreign scope");
        var localFiles = Path.Combine(data, "transfer-work"); Directory.CreateDirectory(localFiles);
        var final = Path.Combine(localFiles, "target.bin"); await File.WriteAllTextAsync(final, "keep");
        var partial = Path.Combine(localFiles, ".relay-transfer-" + operation.Id.ToString("D") + ".part"); await File.WriteAllTextAsync(partial, "partial");
        var bridge = new WslBridge(); var settings = new SettingsStore(data); var sessions = new SessionService(data, settings, new(settings, bridge), bridge);
        var fileAdapter = new FileTransferService(sessions, bridge, database);
        var placeholder = new WslProfile("unused", "Unused", "Unused", "unused", "/srv/agent-relay-unused", "codex", []);
        var inspected = await fileAdapter.PartialAsync(operation, "download", "target.bin", localFiles, placeholder, false, CancellationToken.None);
        if (!inspected.Exists || inspected.Bytes != 7 || inspected.Removed) throw new Exception("Partial inspection modified the file.");
        var cleaned = await fileAdapter.PartialAsync(operation, "download", "target.bin", localFiles, placeholder, true, CancellationToken.None);
        if (!cleaned.Removed || File.Exists(partial) || await File.ReadAllTextAsync(final) != "keep") throw new Exception("Partial cleanup affected the final destination.");
        Console.WriteLine("PASS: explicit owned-partial inspection and cleanup preserve the final destination");
    }

    private static void Refused(Action action)
    {
        try { action(); throw new Exception("Unauthorized project action accepted."); }
        catch (RelayException error) when (error.Code == "capability_denied") { }
    }
}
