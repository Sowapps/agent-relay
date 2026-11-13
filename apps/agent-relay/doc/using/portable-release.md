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

Merge var/apps/agent-relay/default/mcp.windows.json or mcp.codex.toml into the supported shared client configuration after review. The fixed PowerShell 7 entry sets an explicit var-owned runtime location and invokes the self-contained C# transport. It carries the explicit connection UUID, follows selected releases and needs no checkout SDK/Python to serve MCP. Existing bindings never silently change. Moving the installation requires reviewed regeneration; initial legacy registration migration remains separate.

Read resources/list and the public quickstart/tools/bootstrap/portable-release/request-example resources. The four tools are get_connection_status, discover_operations, invoke_operation and get_result. Discover then invoke list_projects with its actual revision and confirm UUIDs/rights; list_results/list_sessions are likewise operations inside invoke_operation. Empty collections are valid for LocalOnly. Native work requires its actual task authorization; connecting alone is not a model call.

## Guide the human and preserve data

The window is a monitoring/debugging interface. Agents primarily configure and operate through their authorized routes. Vue générale shows cross-project activity; select a project for Tâches, Activité, Messages and Sessions → Conversation. Choose Tout l’historique and clear filters for older records; load previous pages when needed. Examiner opens the detail drawer; marking an alert examined preserves its failure in Activité. Copy/interruption/technical detail actions are contextual and permission-dependent. Session conversations are retained relay exchanges, not a full imported native transcript.

The window close button hides the application; tray Ouvrir reopens and Quitter exits. Separate data scopes have separate tray instances. Réglages → Général → Démarrage avec Windows enables/disables optional startup at Windows sign-in. It is off by default, starts only the relay quietly in the notification area and does not launch an agent/task. Registration applies only to the current Windows user and explicit data scope. Windows may independently block the registered launch in its startup settings. Disabling does not quit the running relay; Quitter does not disable future startup.

Use the complete-update procedure below for an existing installation. Do not manually extract over running files or copy a live SQLite database. Immutable release slots and the retained transaction preserve rollback material; the startup opt-in is reconciled to the common entry before completion. A disabled preference stays disabled. Moving installation/data is a separate reviewed operation.

The generated entry follows the selected checked transport/worker while retaining the original client stdio. Keep its var-owned bootstrap and launch script. Generic application updates do not rewrite client registration or require a catalog refresh. Stable and numbered RC are the only version forms. Legacy typed connections require a one-time maintainer migration; this does not grant new project rights.
## Update an existing installation

Use the reviewed new release's `Update-AgentRelay.cmd --data <absolute-existing-data-directory> --release-directory <absolute-release-directory> --version <X.Y.Z>`. Keep the exact downloaded ZIP and SHA256SUMS.txt in that release directory. If verification.json is absent, the bundled verifier first qualifies the archive on its own fictional data, with no WSL or model call; an existing verification must match. A local build and downloaded release use this same procedure. Python 3 and PowerShell 7 operational tooling must already be available; the updater installs neither.

The C# transaction updates the existing installation, documents/tools, checked selected worker/transport, fixed bootstrap and existing startup preference. It preserves projects, grants and history. Preparation alone is passive: explicit resume/start-resume authorizes execution. The matching verified retained runner continues after interruption without the original download directory or a 15-minute countdown. Recovery uses the selected data scope and status/resume/rollback actions; diagnostic retrieves retained error details. MCP status and Réglages → Connexions MCP show the same transaction phase/blocker. Uncertain work remains suspended for explicit reconciliation; it is never replayed. Rollback after admission reopened is refused to protect new work. Completion requires all owned transport readiness and bootstrap publication, not merely host health. Initial legacy migration and synchronization of changed native helpers remain separately qualified gates.

## Recover an unavailable relay

An authorized agent with local command access can use the exact installed `AgentRelay.Windows.exe` with `startup --data-dir <explicit-data-directory>`. Use the data directory from the approved MCP registration. Then call `get_connection_status` and `list_projects` through the registered connection. For work already submitted, inspect its retained result first. An agent with MCP access alone asks the Agent Relay maintainer or the user to start the shared relay. This procedure is also available when the MCP connection itself fails to start.
