# Start with your agent

**Utility:** guides a person and their agent from an initial need to a working relay project.  
**Usage:** use the README's new-installation prompt or existing-relay prompt, then follow the applicable path here.

## Keep your way of working

The agent is the assistant you work with; its client application hosts the conversation and tools. The Windows client connects to Agent Relay through the Model Context Protocol (MCP). Relay is the local Windows application/server that routes and records work. A native agent client is the supported executable running the delegated session in Windows Subsystem for Linux (WSL). These clients and the relay have distinct installation, login and connection requirements.

Agent Relay connects agents and records their exchanges. It does not prescribe a planner/worker hierarchy, mandatory reviewer, branching strategy, number of agents or sequence of tasks. Preserve the user's existing instructions, roles, approval process and workspace rules. Main / Principal and Secondary / Secondaire are relay roles, not instructions to reorganize the team or automatic permissions.

When a required relay choice is missing, propose only the minimum needed: which work belongs together, its participants, existing folders and the actions they may perform. Ask when an ambiguity changes authority, destination or scope. Discover technical facts yourself when authorized rather than asking the user to diagnose .NET or invent a Linux path. Preparation is not installation/configuration/publication permission; confirm the intended scope before those actions. Once authorized, complete that scope without requesting approval again for every routine step.

## What to give your agent

Describe what you want to accomplish, the repository/release address and any existing installation. Starting from zero means Agent Relay and its prerequisites may be absent: you still need an existing agent client and access to the private repository. The client needs authorized local tools to perform setup, or must guide your manual steps. Identify existing project folders, if known, and whether an agent already works in WSL. State the permitted changes or ask for a preparation proposal first. The agent should inspect available tools/access before claiming it can download, configure a client or control the application.

An agent with only a scoped relay MCP connection can operate within its grants. Preparing an installation or adding a project requires separately authorized local administration. If it lacks filesystem/shell or application access, it should give exact steps rather than pretend it performed them. Private GitHub access, native client login and approval prompts may require the account holder; never send credentials in a conversation.

## Understand your project and its paths

A **Relay project / Projet relayé** associates a purpose with participants, their **Workspaces / Espaces de travail** and explicit access rules. For example, “Website refresh” may connect your agent running on Windows with implementation and review agents in WSL. Each has a chosen workspace and permissions suited to its actual job. This is an example, not a mandatory team.

If your Windows client offers remote access, you can use that access to ask its agent to delegate through Relay to a supported WSL agent. The remote access belongs to the entry client: Relay routes authorized requests/results, not desktop video or arbitrary tools from another client. Each agent retains its own permissions and client capabilities.

The project has an identity separate from its display name, folders and sessions. Renaming it does not make a different folder safe to use. Adding a project should preserve existing projects and grants. A session belongs to its chosen project, participant and workspace; a new conversation does not silently move it to another project.

| Location | What belongs there | Example only |
| --- | --- | --- |
| Installation directory / Dossier d'installation | The extracted application and launchers | `C:\Tools\AgentRelay` |
| Data directory / Dossier de données | Relay configuration, protected connection credentials and retained history | `C:\Tools\AgentRelay\data` for the portable launcher's default scope |
| Windows Workspace / Espace de travail | Existing files an authorized participant may read or change | `C:\Work\website` |
| Linux Workspace / Espace de travail | Existing working folder in the selected WSL distribution | `/home/me/work/website` |

These are examples, not folders to create without agreement. Windows and Linux paths can point to different copies of a project; matching names do not prove shared files or automatic synchronization. Confirm which environment owns each workspace and the intended transfer direction. Specify the WSL distribution and Linux user as well as its path. Native session runtime/configuration folders are separate from the user's workspace.

Keep installation, data and workspaces in persistent locations. Prefer the existing approved location; otherwise propose a writable installation folder and explicit data scope. Do not put working files into the relay's runtime/cache. Another data directory represents another relay instance, with separate configuration/history and potentially another tray icon.

## With Agent Relay not yet installed

Ask the agent for a concise proposal first. It identifies the approved release/account access, checks existing prerequisites and proposes only missing changes. Source compilation is not the normal user's installation path.

After the corresponding authorization:

1. Retrieve the Windows x64 release and checksum from the approved private repository; verify and extract the whole archive to the selected persistent folder.
2. Read START-HERE.txt and the [portable guide](portable-release.md). Verify prerequisites rather than installing implicitly. Windows monitoring/direct MCP includes .NET; graphical runtime and native WSL prerequisites are handled only if needed and authorized.
3. Initialize the chosen data scope and start the relay. Keep that scope explicit throughout configuration and client registration.
4. Associate a first real project with selected participants, existing workspaces and required permissions. Create its participant-scoped MCP connection through authorized administration.
5. Register the connection using the caller's actually supported configuration/lifecycle, then call list_projects from that client. A settings entry or running window alone is not proof.
6. Verify a small, agreed useful exchange. Check readiness and necessary account login before involving a native agent; a model call is not a free setup check. Confirm where its result and session discussion appear in the application.

Finish by reporting installation/data locations, selected project/workspaces, passed checks and any remaining action. Do not leave guessed paths or an unspecified “reconnect” instruction when account login is required.

## With an existing relay

Discover projects and actual connection scope before adding anything. Reuse a matching project/session when appropriate to the user's intent; otherwise propose a new project, needed participants and narrow permissions. A similar name is not proof of identity. Preserve existing configuration and history.

For an archive, continue with portable administration. For an explicitly chosen source checkout, use [bootstrap and administration](bootstrap.md). An already connected operational agent uses its MCP quickstart resource, starting with list_projects. Do not substitute source scripts, data scopes or default workspaces for the installed version.

## Find the result in the application

The application interface is currently in French. Vue générale (Overview) shows work across projects. Selecting a project opens its Synthèse (Summary), Tâches (Tasks), Activité (Activity), Messages and Sessions. Project context and item details identify who acted and where; open technical details only when needed.

Use Sessions → Conversation for retained exchanges. Examiner opens details for a point to verify. Marking it examined removes that alert revision, not its historical result; it remains in the project's Activité. Filters and earlier pages can hide older records: choose Tout l'historique and clear filters before concluding a record is missing.

Closing the window hides it; reopen from the tray. Quitter stops the relay. Keep it running for MCP. To move or upgrade, stop the selected host, back up its data and retain the old version until verification. Moving a package changes its absolute MCP launch paths: regenerate and review the fragment for that same data scope.
