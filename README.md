# Agent Relay workspace

**Utility:** Introduces this multi-application workspace and directs people or agents to Agent Relay.  
**Usage:** Start here for the project overview; choose the human, bootstrap or MCP guide before acting.

Agent Relay lets an AI agent coordinate with other agents through MCP while keeping projects, workspaces, permissions and retained results explicit. Its Windows application lets a person follow work, read exchanges and investigate errors.

The workspace can host several applications under `apps/<id>/`; Agent Relay is the current application.

The private [Windows release](https://github.com/Sowapps/agent-relay/releases) provides a portable ZIP with its .NET runtime included. Extract it and read START-HERE.txt; the source checkout workflow below is separate.

- **Prepare/deploy a relay or add a project:** follow the [complete bootstrap/administration guide](apps/agent-relay/doc/using/bootstrap.md).
- **Using MCP as an agent:** start with the [agent quickstart](apps/agent-relay/doc/using/agent-quickstart.md). Connected clients can also read the MCP resource `agent-relay://docs/quickstart` without access to this checkout.
- **Understanding or running the application:** read the [Agent Relay introduction](apps/agent-relay/README.md) and [user guide](apps/agent-relay/doc/using/mvp.md).
- **Working on source:** read [workspace rules](AGENTS.md), the [documentation index](doc/README.md) and [commands](doc/operating/entrypoints.md).

Agent Relay is a local tool. Its current Windows host supports native Codex/Claude sessions and configured commands/files in WSL. Connectors, environment connections and project roles are separate abstractions; additional providers/transports require implemented adapters. An existing adapter is not permission to use it.

The monitoring interface uses local HTML/CSS inside the C# application through WebView2. C#/.NET owns the relay, authorization and storage. No remote website or local HTTP server is required for the normal application. See the [portable guide](apps/agent-relay/doc/using/portable-release.md) for release usage and [release workflow](apps/agent-relay/doc/developing/releases.md) for maintainers.
