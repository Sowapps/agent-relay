# Agent Relay

**Utility:** Introduces Agent Relay, its supported uses, boundaries and starting paths.  
**Usage:** Read for an overview, then follow bootstrap for preparation or the agent quickstart for an already configured MCP connection.

Agent Relay connects AI agents through a local MCP interface. An agent can reuse named conversations, delegate authorized work, follow durable results and receive reports. Projects identify who may do what, in which workspaces.

The Windows window is primarily for human monitoring and diagnosis: General shows active work and points to verify; each project shows its tasks, activity, messages and sessions. A session opens its retained conversation. Technical actions stay behind contextual details/settings. Agents normally operate through MCP rather than driving the window.

## What it does

- Address a participant in an explicit Relay project / Projet relayé and Workspace / Espace de travail.
- Continue or open separate sessions, with bounded concurrency and explicit write permissions.
- Retain requests, operation states, results, replies and independent messages.
- Run configured direct commands without a model, inspect an authorized environment and transfer an explicit file between authorized workspaces.
- Find errors, inspect details, copy content and mark an alert as examined without erasing its failed outcome.

Client connector, environment connection and Main / Principal or Secondary / Secondaire role are separate concepts. Current native agent support is Codex/Claude over WSL, with local file access and configured WSL commands; other providers or SSH require additional adapters. Roles do not grant capabilities. A relay session is not a full transcript importer, and interruption does not undo external effects.

## Start from source

Windows, the application’s pinned .NET10 SDK/private dependency cache and an installed WebView2 runtime are required for this development checkout. Native WSL work additionally needs an explicitly configured distribution/user, native client authentication and authorized existing workspaces. Setup is described in [development commands](doc/developing/development.md); startup never installs prerequisites automatically.

From the workspace root, once the toolchain is qualified:

```powershell
./dev.ps1 run -App agent-relay -Offline
```

Keep the host running while using MCP. Closing its window hides it; Quitter exits the relay. Native agent sessions have their own lifecycle. The app’s HTML/CSS is embedded locally in its C# window; no browser tab or web server is needed.

## Choose a guide

- **Downloaded ZIP:** [portable release guide](doc/using/portable-release.md); no .NET SDK or Python launcher is needed for Windows startup/MCP.

- **Prepare/deploy a relay or add a project:** follow the [complete bootstrap/administration guide](doc/using/bootstrap.md).
- **Agent using MCP:** [quickstart](doc/using/agent-quickstart.md), then [tool reference](doc/using/mcp-tools.md). These are also available through the connected MCP server as documentation resources.
- **Person using the window:** [setup and monitoring](doc/using/mvp.md).
- **Protocol/session details:** [sessions and MCP](doc/using/sessions-and-mcp.md).
- **Developing/extending:** [development](doc/developing/README.md), [architecture](doc/developing/projects-and-adapters.md), [glossary](doc/developing/glossary.md).
- **All references:** [documentation index](doc/README.md); dated test observations are kept separately in [notes](notes/README.md).

Installation directory, data directory and workspaces are distinct. Do not put project files into the runtime/cache directory or publish local credentials/history. Maintainers use the [release workflow](doc/developing/releases.md) for version, archive verification and authorized publication.
