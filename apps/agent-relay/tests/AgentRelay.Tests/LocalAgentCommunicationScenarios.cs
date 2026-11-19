using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Transport;

internal static class LocalAgentCommunicationScenarios
{
    internal static async Task RunAsync(string root, string transport, string app, string distribution, string user)
    {
        if (Directory.Exists(root)) throw new ArgumentException("Select a new qualification directory.");
        var data = McpEntryScenarios.PrepareInstallation(root, transport, app);
        var project = Guid.NewGuid();
        var foreignProject = Guid.NewGuid();
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid(); var commandEndpoint = Guid.NewGuid();
        var aWorkspace = Guid.NewGuid(); var bWorkspace = Guid.NewGuid(); var cWorkspace = Guid.NewGuid(); var cmdWorkspace = Guid.NewGuid();
        var aBinding = Guid.NewGuid(); var bBinding = Guid.NewGuid();
        var ab = Guid.NewGuid(); var bc = Guid.NewGuid(); var ba = Guid.NewGuid(); var ac = Guid.NewGuid();
        var space = "/tmp/agent-relay-local-" + Guid.NewGuid().ToString("N");
        var token = CancellationToken.None;
        var roots = new[] { ab, bc, ba, ac,
            Key($"file-runtime:{project:D}:{b:D}:{bWorkspace:D}"),
            Key($"command-runtime:{commandEndpoint}:{project}:{cmdWorkspace}:{b}") };

        Task<ProcessOutput> Linux(string script, object input) => LocalProcess.RunAsync("wsl.exe",
            ["-d", distribution, "-u", user, "--exec", "/usr/bin/python3", "-B", "-c", script],
            RelayJson.Serialize(input), TimeSpan.FromSeconds(25), token);

        var setup = await Linux("""
            import json, pathlib, sys
            payload=json.load(sys.stdin); root=pathlib.Path(payload['path'])
            root.mkdir(mode=0o700)
            for item in ('a','b','c'): (root/item).mkdir()
            (root/'c/proof.txt').write_text('local relay fixture')
            (root/'fixture-owner').write_text(payload['path'])
            agent=root/'agent'
            agent.write_text(payload['agent']); agent.chmod(0o700)
            """, new { Path = space, Agent = FixtureAgent });
        Check(setup.ExitCode == 0, setup.Error);
        try
        {
            var endpoints = new[] { new RelayEndpoint(a, "A", "wsl-agent", "test-agent"),
                new(b, "B", "wsl-agent", "test-agent"), new(c, "C", "wsl-agent", "test-agent"),
                new(commandEndpoint, "Command target", "wsl-command", "test-command") };
            var workspaces = new[] { new RelayWorkspace(aWorkspace, a, "A files", space + "/a"),
                new(bWorkspace, b, "B files", space + "/b"), new(cWorkspace, c, "C files", space + "/c"),
                new(cmdWorkspace, commandEndpoint, "Commands", space + "/c") };
            var access = workspaces.Select(item => new WorkspaceAccess(item.Id, true, true)).ToArray();
            string[] capabilities = [RelayCapabilities.ReadHistory, RelayCapabilities.ManageSession, RelayCapabilities.SendMessage,
                RelayCapabilities.ReceiveMessage, RelayCapabilities.ReadFile, RelayCapabilities.WriteFile, RelayCapabilities.RunCommand];
            var settings = new ProjectSettingsStore(data);
            settings.Save(new(1, [new(project, "Isolated local communication"), new(foreignProject, "Unrelated")], endpoints, workspaces,
                [new(project, a, "main", [..capabilities, RelayCapabilities.ManageProjectAccess], access, ["pwd"]),
                 new(project, b, "secondary", capabilities, access, ["pwd"], NativeConnectionId: bBinding),
                 new(project, c, "secondary", capabilities, access, ["pwd"]),
                 new(project, commandEndpoint, "secondary", [], []), new(foreignProject, c, "main", capabilities, access)]));
            new SettingsStore(data).Save(new(1, [
                new("test-agent", "Fixture agent", distribution, user, RuntimePaths.NativeSession(Guid.NewGuid()), "codex", [], NativeExecutable: space + "/agent"),
                new("test-command", "Fixture commands", distribution, user, RuntimePaths.NativeSession(Guid.NewGuid()), "command", [new("pwd", "Working directory", "/bin/pwd", [])])
            ]));
            var bindings = new ConnectionStore(data);
            await bindings.CreateAsync(aBinding, a, [project], false, token);
            await bindings.CreateAsync(bBinding, b, [project], false, token);
            await bindings.DemandParticipantBindingAsync(bBinding, b, project, token);
            await using var host = RelayRuntime.Start(data, error => Console.Error.WriteLine(error.Message));
            await using var first = await Client.OpenAsync(data, aBinding, distribution);
            await using var second = await Client.OpenAsync(data, bBinding, distribution);
            Check((await second.Invoke("list_projects", new { })).GetProperty("configuration").GetProperty("projects").GetArrayLength() == 1, "Foreign project disclosed");
            var bootstrap = RuntimePaths.TransportBootstrapExecutable(data);
            var replacement = bootstrap + ".qualification";
            File.Copy(bootstrap, replacement);
            File.Move(replacement, bootstrap, overwrite: true);
            Check(Code(await second.Invoke("read_inbox", new { project_id = foreignProject })) == "capability_denied", "Foreign inbox admitted");
            await Open(first, ab, b, bWorkspace);
            await Open(second, bc, c, cWorkspace);
            var nestedRequest = Guid.NewGuid();
            await Exchange(first, ab, a, b, nestedSession: bc, nestedRequest: nestedRequest);
            var nested = await second.WaitResult(nestedRequest);
            Check(nested.GetProperty("request").GetProperty("scope").GetProperty("senderId").GetGuid() == b,
                "The in-session delegation did not run as B");
            var nestedInbox = (await second.Invoke("read_inbox", new { project_id = project, session_id = bc })).GetProperty("messages").GetProperty("items");
            Check(nestedInbox.EnumerateArray().Any(item => item.GetProperty("inReplyTo").GetGuid() == nestedRequest &&
                item.GetProperty("senderEndpointId").GetGuid() == c && item.GetProperty("recipientEndpointId").GetGuid() == b),
                "The in-session requester did not receive the child reply");
            await Open(second, ba, a, aWorkspace); await Exchange(second, ba, b, a);
            await Open(first, ac, c, cWorkspace);
            Check(Code(await second.Invoke("send_session_message", new { session_id = ac, text = "Unmandated" })) == "capability_denied", "Foreign session admitted without a mandate");

            var initialAccess = (await first.Invoke("get_project_access", new { project_id = project, endpoint_id = b })).GetProperty("projectAccess");
            var revision = initialAccess.GetProperty("revision").GetString();
            object Change(string? expected, bool mandate, Guid[] targets) => new
            {
                project_id = project, endpoint_id = b, expected_revision = expected, capabilities,
                workspaces = access.Select(item => new { workspace_id = item.WorkspaceId, item.Read, item.Write, item.Replace })
                    .Select(item => new { item.workspace_id, read = item.Read, write = item.Write, replace = item.Replace }),
                command_ids = new[] { "pwd" }, all_message_targets = false, message_targets = targets,
                session_mandates = mandate ? new[] { new { session_id = ac, owner_id = a,
                    capabilities = new[] { RelayCapabilities.SendMessage, RelayCapabilities.ReceiveMessage, RelayCapabilities.ReadHistory } } } : []
            };
            var granted = await first.Invoke("set_project_access", Change(revision, true, [c, a]));
            Check(Code(granted) is null, granted.ToString());
            Check(Code(await first.Invoke("set_project_access", Change(revision, false, [a]))) == "state_conflict", "Old access revision was applied");
            var delegatedRequest = await Exchange(second, ac, b, c, a);
            var ownerInbox = (await first.Invoke("read_inbox", new { project_id = project, session_id = ac })).GetProperty("messages").GetProperty("items");
            Check(!ownerInbox.EnumerateArray().Any(item => item.GetProperty("inReplyTo").GetGuid() == delegatedRequest), "Delegated reply was routed to the retained owner");

            var commandId = Guid.NewGuid();
            await second.Invoke("execute_command", new { project_id = project, endpoint_id = commandEndpoint, workspace_id = cmdWorkspace,
                command_id = "pwd", request_id = commandId });
            var command = await second.WaitResult(commandId);
            Check(command.GetProperty("result").GetProperty("output").GetString()?.Trim() == space + "/c", "Command ran in the wrong workspace");

            var transferId = Guid.NewGuid();
            var transfer = await second.Invoke("transfer_file", new { session_id = bc, request_id = transferId,
                source = new { endpoint_id = c, workspace_id = cWorkspace, path = "proof.txt" },
                destination = new { endpoint_id = b, workspace_id = bWorkspace, path = "received.txt" } });
            Check(Code(transfer) is null, transfer.ToString());
            await second.WaitResult(transferId);
            var copied = await Linux("import json,pathlib,sys; print(pathlib.Path(json.load(sys.stdin)).read_text())", space + "/b/received.txt");
            Check(copied.ExitCode == 0 && copied.Output.Trim() == "local relay fixture", "Native file transfer did not preserve the file");

            var next = granted.GetProperty("projectAccess").GetProperty("revision").GetString();
            Check(Code(await first.Invoke("set_project_access", Change(next, false, [c]))) is null, "Revocation failed");
            Check(Code(await second.Invoke("send_session_message", new { session_id = ac, text = "Revoked mandate" })) == "capability_denied", "Revoked mandate still admitted work");
            Check(Code(await second.Invoke("send_session_message", new { session_id = ba, text = "Revoked peer" })) == "capability_denied", "Revoked target still admitted work");
            var launch = await Linux("import json,pathlib,sys; p=pathlib.Path(json.load(sys.stdin)); v=json.loads(p.read_text()); print(v['participantMcp'] is not None)",
                RuntimePaths.NativeSession(ab) + "/codex/launch-state.json");
            Check(launch.Output.Trim() == "True", "B's native conversation did not receive its own project MCP launch");
            File.WriteAllText(Path.Combine(root, "qualification.json"), RelayJson.Serialize(new { Project = project, Sessions = new { ab, bc, ba, ac },
                NestedRequest = nestedRequest, DelegatedRequest = delegatedRequest, Command = commandId, Transfer = transferId,
                ModelCalls = 0, CrossProjectRefused = true }));
            Console.WriteLine("LOCAL_AGENT_COMMUNICATION=PASS (WSL MCP, A→B/B→C/B→A, delegated reply, grants/revocation, command, WSL file)");
        }
        finally
        {
            var cleanup = await Linux("""
                import json,pathlib,shutil,subprocess,sys,uuid
                c=json.load(sys.stdin); root=pathlib.Path(c['path'])
                assert root.parent == pathlib.Path('/tmp') and root.name.startswith('agent-relay-local-')
                assert (root/'fixture-owner').read_text() == str(root)
                for value in c['roots']:
                    p=pathlib.Path(value); identifier=uuid.UUID(p.name)
                    assert p.parent == pathlib.Path('/srv/agent-relay/var/apps/agent-relay/sessions') and not p.is_symlink()
                    if not p.exists(): continue
                    assert json.loads((p/'provision-owner.json').read_text())['sessionId']==str(identifier)
                    contexts=[json.loads(v.read_text()) for v in p.glob('*/context.json')]
                    assert len(contexts)==1 and pathlib.Path(contexts[0]['workspace']).is_relative_to(root)
                    subprocess.run(['tmux','-S',str(p/'tmux.sock'),'kill-server'],capture_output=True,check=False)
                    shutil.rmtree(p)
                shutil.rmtree(root)
                """, new { Path = space, Roots = roots.Select(RuntimePaths.NativeSession) });
            Check(cleanup.ExitCode == 0, cleanup.Error);
        }

        async Task Open(Client client, Guid id, Guid endpoint, Guid workspace)
        {
            var opened = await client.Invoke("open_session", new { project_id = project, endpoint_id = endpoint, workspace_id = workspace,
                session_id = id, name = "Local fixture " + id.ToString("N")[..6] });
            Check(Code(opened) is null, opened.ToString());
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var view = (await client.Invoke("get_session", new { session_id = id })).GetProperty("session");
                if (view.GetProperty("state").GetString() == "ready") return;
                if (view.GetProperty("state").GetString() != "starting") throw new Exception(view.ToString());
                await Task.Delay(250);
            }
            throw new Exception("The fixture session did not initialize.");
        }

        async Task<Guid> Exchange(Client client, Guid session, Guid caller, Guid target, Guid? owner = null,
            Guid? nestedSession = null, Guid? nestedRequest = null)
        {
            var id = Guid.NewGuid();
            var text = nestedSession.HasValue ? $"Fixture nested {nestedSession:D} {nestedRequest:D}" : "Fixture exchange";
            var accepted = await client.Invoke("send_session_message", new { session_id = session, request_id = id, text });
            Check(Code(accepted) is null, accepted.ToString());
            var work = await client.WaitResult(id);
            Check(work.GetProperty("result").GetProperty("output").GetString()?.Contains("Isolated fixture reply") == true,
                "The native fixture reported an error");
            var workRequest = work.GetProperty("request");
            Check(workRequest.GetProperty("scope").GetProperty("senderId").GetGuid() == caller, "Requester identity was replaced");
            if (owner is { } delegated)
                Check(workRequest.GetProperty("sessionOwnerId").GetGuid() == delegated, "Technical ownership was lost");
            var replies = (await client.Invoke("read_inbox", new { project_id = project, session_id = session })).GetProperty("messages").GetProperty("items");
            Check(replies.EnumerateArray().Any(item => item.GetProperty("inReplyTo").GetGuid() == id &&
                item.GetProperty("senderEndpointId").GetGuid() == target && item.GetProperty("recipientEndpointId").GetGuid() == caller), "Reply did not reach the requester");
            return id;
        }
    }

    private static Guid Key(string input) => new(SHA256.HashData(Encoding.UTF8.GetBytes(input)).AsSpan(0, 16));
    private static string? Code(JsonElement result) => result.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Client(Process process) : IAsyncDisposable
    {
        private readonly McpFrames frames = new(process.StandardOutput.BaseStream);
        private readonly Task<string> errors = process.StandardError.ReadToEndAsync();
        private int counter;

        internal static async Task<Client> OpenAsync(string data, Guid binding, string distribution)
        {
            var launch = McpTransportInstallation.Launch(data, binding, wslStdio: true);
            var converted = await LocalProcess.RunAsync("wsl.exe", ["-d", distribution, "--exec", "wslpath", "-u", launch.Command],
                null, TimeSpan.FromSeconds(10), CancellationToken.None);
            Check(converted.ExitCode == 0, converted.Error);
            var info = new ProcessStartInfo("wsl.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var part in new[] { "-d", distribution, "--exec", converted.Output.Trim() }.Concat(launch.Args)) info.ArgumentList.Add(part);
            var client = new Client(Process.Start(info) ?? throw new Exception("Could not start WSL MCP fixture"));
            try
            {
                await client.Call("initialize", new { protocolVersion = "2025-11-25", capabilities = new { }, clientInfo = new { name = "isolated WSL client", version = "1" } });
                return client;
            }
            catch
            {
                await client.DisposeAsync();
                throw;
            }
        }

        private async Task<JsonElement> Call(string method, object arguments)
        {
            var id = ++counter;
            await McpFrames.WriteAsync(process.StandardInput.BaseStream, RelayJson.Serialize(new { jsonrpc = "2.0", id, method, @params = arguments }));
            var raw = await frames.ReadAsync().WaitAsync(TimeSpan.FromSeconds(65)) ??
                throw new Exception("WSL MCP closed: " + await errors.WaitAsync(TimeSpan.FromSeconds(4)));
            var response = RelayJson.Deserialize<JsonElement>(raw);
            Check(response.GetProperty("id").GetInt32() == id && !response.TryGetProperty("error", out _), raw);
            return response.GetProperty("result");
        }

        internal async Task<JsonElement> Invoke(string operation, object arguments) =>
            (await Call("tools/call", new { name = "invoke_operation",
                arguments = new { call_id = Guid.NewGuid(), operation_id = operation, revision = 1, arguments } })).GetProperty("structuredContent");

        internal async Task<JsonElement> WaitResult(Guid id)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            while (!deadline.IsCancellationRequested)
            {
                var work = (await Call("tools/call", new { name = "get_result", arguments = new { request_id = id } }))
                    .GetProperty("structuredContent").GetProperty("operation");
                var state = work.GetProperty("state").GetString();
                if (state == "completed") return work;
                Check(state is "pending" or "running", work.ToString());
                await Task.Delay(200, deadline.Token);
            }
            throw new TimeoutException("Fixture operation did not complete.");
        }

        public async ValueTask DisposeAsync()
        {
            process.StandardInput.Close();
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(8)); }
            catch (TimeoutException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            finally { process.Dispose(); }
        }
    }

    private const string FixtureAgent = """
        #!/usr/bin/python3
        import json,os,pathlib,re,select,subprocess,sys,time,tty,uuid
        from datetime import datetime,timezone
        if sys.argv[1:3] == ['login','status']: sys.exit(0)
        folder=pathlib.Path(os.environ['CODEX_HOME']).parent
        root=folder.parent
        sys.path.insert(0,str(root/'install'))
        from native_clients import process_stamp,project_mcp_entry
        from relay_files import commit,context,store_message
        scope=context(root,'codex')
        tty.setraw(0)
        commit(folder/'data/mcp-initialized.json',{'initialized':True,'nativeAncestors':[process_stamp(os.getpid())['generation']]})
        def run_nested(session_id,request_id):
            launch=json.loads((folder/'launch-state.json').read_text())['participantMcp']
            entry=project_mcp_entry(launch)
            with subprocess.Popen([entry['command'],*entry['args']],stdin=subprocess.PIPE,stdout=subprocess.PIPE,
                                  stderr=subprocess.DEVNULL,text=True,encoding='utf-8') as child:
                counter=0
                def call(method,params):
                    nonlocal counter
                    counter+=1
                    child.stdin.write(json.dumps(dict(jsonrpc='2.0',id=counter,method=method,params=params))+'\n')
                    child.stdin.flush()
                    if not select.select([child.stdout],[],[],12)[0]: raise TimeoutError('Nested MCP did not reply')
                    value=json.loads(child.stdout.readline())
                    assert value.get('id')==counter and 'error' not in value, value
                    return value['result']
                try:
                    call('initialize',dict(protocolVersion='2025-11-25',capabilities={},clientInfo=dict(name='native fixture',version='1')))
                    call('tools/call',dict(name='invoke_operation',arguments=dict(call_id=str(uuid.uuid4()),
                        operation_id='send_session_message',revision=1,arguments=dict(session_id=session_id, request_id=request_id,
                        text='Fixture exchange'))))
                    for attempt in range(20):
                        reply=call('tools/call',dict(name='get_result',arguments=dict(request_id=request_id)))
                        state=reply['structuredContent']['operation']['state']
                        if state=='completed': break
                        if state not in ('pending','running'): raise RuntimeError(state)
                        time.sleep(.2)
                    else: raise TimeoutError('The child request did not finish')
                finally: child.stdin.close()
        while True:
            content=b''
            while not content.endswith(b'\r'):
                value=os.read(0,1)
                if not value: sys.exit(0)
                content+=value
            match=re.search(rb'Relay request ([0-9a-f-]{36})',content)
            if not match: continue
            result='Isolated fixture reply'
            nested=re.search(rb'Fixture nested ([0-9a-f-]{36}) ([0-9a-f-]{36})',content)
            if nested:
                try: run_nested(nested[1].decode(),nested[2].decode())
                except Exception as error: result='Native fixture error: '+str(error)
            store_message(root,'codex',{'id':str(uuid.uuid4()),'profileId':'codex','sender':'codex-wsl',
              'recipient':'windows-pilot','text':result,'inReplyTo':match[1].decode(),
              'receivedAt':datetime.now(timezone.utc).isoformat(),'sessionId':scope['sessionId'],
              'projectId':scope['projectId'],'senderEndpointId':scope['recipientId'],'recipientEndpointId':scope['senderId']})
        """;
}
