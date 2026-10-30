# Agent Relay

**Utility:** introduces agent collaboration between Windows and WSL and its monitoring interface.  
**Usage:** start with the project overview, then let your usual agent help prepare the relay for your work.

Your agent's client application on Windows connects to Agent Relay through the Model Context Protocol (MCP). The relay delegates to supported native agent clients in Windows Subsystem for Linux (WSL), retains named sessions and messages, and provides authorized inspection and individual file transfers. Project-scoped permissions and retained results keep collaboration identifiable. The Windows monitoring/diagnostic interface is currently in French.

The [project overview](../../README.md) presents supported uses, indirect benefits and two ready-to-copy starting prompts. The [getting-started guide](doc/using/getting-started.md) explains your project, paths, agent assistance and first connection without requiring source-development knowledge.

## Continue when needed

Your existing instructions and agent organization remain authoritative; configuration should add only what the relay needs. A participant cannot grant itself project access through ordinary MCP tools.

Use the [functional guide](doc/using/mvp.md) for monitoring and intervention, and [limits with evolution options](doc/using/limits.md) for current boundaries. Source contributors have a separate [development entry](doc/developing/README.md).
