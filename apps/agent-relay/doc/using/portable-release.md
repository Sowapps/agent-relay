# Use a portable Windows release

**Utility:** prepares the Windows archive and its Model Context Protocol (MCP) connection without a source checkout or .NET SDK.  
**Usage:** use this operational reference with your agent after agreeing on preparation scope; extract the full archive to a persistent writable folder.

## Start with your usual agent

You need an existing agent client with authorized local tools, or one that guides your manual steps, and access to the private release. “Starting from zero” concerns Agent Relay and its prerequisites, not installation of the assisting client itself. Your agent can help retrieve the release, select locations, prepare prerequisites and create the first project. The [accompanied-start guide](getting-started.md) explains projects/paths before the commands below. Preserve existing instructions, team structure and approvals; propose only missing relay choices. A request for a proposal is not permission to install or configure.

Keep the installation directory, data directory and authorized workspaces distinct. Use the existing approved locations; treat example paths as examples rather than defaults to create. For an existing installation, discover its actual data scope first and preserve its projects/history. The remaining sections are the technical route for the authorized agent/operator, not a demand that the user manage .NET personally.

## Prerequisites and first start

The archive includes the .NET runtime; it is a self-contained Windows x64 deployment. The graphical interface requires the installed Microsoft WebView2 Runtime. It is not installed automatically. Use Microsoft's official runtime distribution instructions when an installation is authorized: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution . Windows monitoring and direct executable MCP need neither Python nor a .NET installation. Source-development instructions in bootstrap.md apply only to a checkout, not this archive.

From the extracted root, or by double-clicking the launchers:

```powershell
./Initialize-Relay.cmd
./Start-AgentRelay.cmd
```

Keep the host running. Default package data lives in var/apps/agent-relay/default/, separate from app/ and user project workspaces. No project, credential, native session, startup registration or global MCP setting is created by initialization. Double-click Start-AgentRelay.cmd for the same scope; directly double-clicking app/AgentRelay.Windows.exe uses that same scope. app/installation.json declares the installation root, so the current directory does not change storage. An explicit --data-dir selects exactly that location and is never silently rewritten.

WebView2 is a separate system prerequisite. The package is unsigned. No signature or clean-machine prerequisite installation is claimed by the package smoke checks. .NET runtime security updates require rebuilding/releasing the package, rather than assuming a global .NET update patches its included runtime. Microsoft documents this deployment model at https://learn.microsoft.com/en-us/dotnet/core/deploying/ .

## Add a project and scoped MCP connection

Here, the MCP client is the Windows application hosting your requesting agent. A native agent client is the supported executable running a delegated conversation inside Windows Subsystem for Linux (WSL); Agent Relay is the intervening Windows server/application.

The current interface is in French. For a native participant, use Réglages → Outils d’administration et de diagnostic → Console technique → Paramètres → Profils techniques, then Réglages → Outils d’administration et de diagnostic → Ajouter un projet. Use existing authorized workspaces and exact discovered distribution/user/client information. Native setup additionally needs Windows python.exe, the selected WSL distribution/user, Linux Python3/tmux, native client executable and human account login. This is separate from starting the Windows relay. Do not blindly run prepare with inferred defaults.

For authorized scripted administration, tools/new-project-request.ps1 (PowerShell7.2+) generates full additive configuration and connection requests. It creates no project/permission itself. Example from the extracted root; supply existing authorized folders:

```powershell
$data = Join-Path (Get-Location) 'var/apps/agent-relay/default'
$requests = Join-Path (Get-Location) 'requests-first'
./tools/new-project-request.ps1 -DataDirectory $data -Name 'My project' -MainWorkspace 'C:\existing\main' -PeerWorkspace 'C:\existing\peer' -OutputDirectory $requests -LocalOnly
# Review configure-project.request.json: preserve every earlier project/grant.
./app/AgentRelay.Windows.exe admin --data-dir $data --input (Join-Path $requests 'configure-project.request.json')
if ($LASTEXITCODE -ne 0) { throw 'Project configuration refused.' }
./app/AgentRelay.Windows.exe admin --data-dir $data --input (Join-Path $requests 'connection.request.json')
if ($LASTEXITCODE -ne 0) { throw 'Connection configuration refused.' }
$context = Get-Content (Join-Path $requests 'example-context.json') -Raw | ConvertFrom-Json
./app/AgentRelay.Windows.exe initialize --data-dir $data --connection-id $context.connectionId
```

LocalOnly is discovery/history preparation, not a native agent simulation. For a real native profile, use an explicit existing ProfileId and absolute Linux peer workspace instead of LocalOnly, as described in bootstrap.md. Never infer profile/provider from role or grant all capabilities to make a test pass. The administrative CLI requires separate local authority, and refuses unsafe changes while busy.

Alternatively prepare a connection from Console technique → select intended project/principal → Paramètres → Préparer la connexion MCP de ce projet, then rerun Initialize-Relay.cmd. The initializer exports the nonsecret UUID reference; protected credentials remain in var/apps/agent-relay/default/. It never edits a client's settings.

## Connect and verify MCP

Merge var/apps/agent-relay/default/mcp.windows.json or var/apps/agent-relay/default/mcp.codex.toml into the caller's supported MCP configuration after review. This package uses app/AgentRelay.Windows.exe directly with mcp, explicit --data-dir and --connection-id; it does not use the checkout's Python launcher, runtime pointer format or SDK. Start/reload through that client's supported lifecycle, never an invented restart control. Regenerate fragments after changing identity or moving the package. Existing connections do not silently change identity.

Read resources/list and the public quickstart/tools/bootstrap/portable-release/request-example resources. Call list_projects and confirm the expected UUID and explicit rights. For LocalOnly, empty list_results/list_sessions with the selected project_id is valid. Native open/send/inspect work requires its actual task authorization; read agent-quickstart.md before it. Opening a connection alone is not a model call.

## Guide the human and preserve data

The window is a monitoring/debugging interface. Agents primarily configure and operate through their authorized routes. Vue générale shows cross-project activity; select a project for Tâches, Activité, Messages and Sessions → Conversation. Choose Tout l’historique and clear filters for older records; load previous pages when needed. Examiner opens the detail drawer; marking an alert examined preserves its failure in Activité. Copy/interruption/technical detail actions are contextual and permission-dependent. Session conversations are retained relay exchanges, not a full imported native transcript.

The window close button hides the application; tray Ouvrir reopens and Quitter exits. Separate data scopes have separate tray instances. Réglages → Général → Démarrage avec Windows enables/disables optional startup at Windows sign-in. It is off by default, starts only the relay quietly in the notification area and does not launch an agent/task. Registration applies only to the current Windows user and explicit data scope. Windows may independently block the registered launch in its startup settings. Disabling does not quit the running relay; Quitter does not disable future startup.

Upgrades are deliberate: quit, back up data, extract another version, initialize it with the same explicit data directory and retain rollback material. The retained var/apps/agent-relay/default/startup-launcher bootstrap follows var/apps/agent-relay/default/mcp-runtime.json, so successful initialization selects the new runtime without a second Run entry. Keep the old runtime until verification/rollback is finished. Disable startup before moving installation/data paths, then initialize and enable again at the new location. Keep startup-launcher while startup is enabled. Its runtime files use hard links only from immutable staged payloads; other installations/filesystems use copies (roughly the bundled runtime size). It makes no network request or prerequisite installation. Startup failures are recorded in var/apps/agent-relay/default/startup.log. Do not copy a running SQLite database as an upgrade strategy or run two hosts on one data scope.

The exported MCP command points to the retained native mcp-launcher under the selected data scope. Keep that launcher while clients use it: it follows mcp-runtime.json between requests rather than pinning a versioned executable. A one-time client reconnection is needed when upgrading an old direct-executable registration. No Python is required for this portable broker. Stable and RC are the only version forms; see the status/tool guide for contract updates. Moving the installation still requires deliberately regenerating its scoped paths; the broker does not guess a new principal or grant.
