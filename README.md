# Agent Relay

**Utility:** connects agents working across Windows and Linux, with shared project activity and history.  
**Usage:** start with your usual agent; follow exchanges and investigate problems in the Windows application.

Your agent's client application on Windows connects to Agent Relay through the **Model Context Protocol (MCP)**. Agent Relay delegates work to supported native agent clients running in **Windows Subsystem for Linux (WSL)** and retains their exchanges in the right project. Its Windows interface lets you monitor activity and investigate results; the current interface is in French.

## What you can do today

| Your need | With Agent Relay |
| --- | --- |
| Delegate from Windows to WSL | Give a supported native agent, currently Codex or Claude, a task in its authorized Linux workspace and retrieve its result. |
| Hear from WSL | Receive task replies and independent progress reports, questions or blocker messages. |
| Inspect without a model turn | Run a configured direct command or inspect an authorized environment through MCP. |
| Continue or parallelize work | Name/reuse sessions or open separate conversations for independent tasks within the configured capacity. |
| Exchange working files | Transfer individual files between authorized Windows and WSL workspaces, with integrity checks and retained results. |
| Follow and investigate | Find project tasks, messages, results, session exchanges and errors; open details or diagnostics when needed. |

## More than a Windows–WSL bridge

- **Reach other agents through your client's remote access.** For example, access your usual Windows client remotely, then ask its agent to delegate to an agent in WSL through Relay. Your client supplies the remote access; Relay carries the authorized tasks and results.
- **Combine complementary agents.** Delegate implementation, review or another division of work that suits you; separate sessions let independent tasks progress in parallel.
- **Keep the useful trail across clients.** Another client bound to the appropriate participant/project can retrieve retained work instead of relying on one originating chat window.
- **Investigate with your agent.** Authorized inspection and retained results help it explain blockers while you follow the same project in the application.

Keep your existing instructions, roles, approvals and working habits. Agent Relay supplies connections and project monitoring, not a mandatory agent methodology.

## Start with your agent

The private [Windows releases](https://github.com/Sowapps/agent-relay/releases) provide a portable archive with .NET included. You need an existing agent client with authorized local tools, or one that can guide your manual steps, plus access to the private repository. Your agent can then guide setup.

### Starting from zero — Agent Relay not installed

Copy this into your usual agent client:

> Help me set up Agent Relay; I already have this agent client, but Agent Relay and its prerequisites may not be installed. Use its private GitHub repository Sowapps/agent-relay: https://github.com/Sowapps/agent-relay. Read its README and getting-started guide, check your local tools, repository access and existing prerequisites, and propose the changes and locations briefly. Respect my instructions and approval rules; clarify ambiguous scope before acting. Once I authorize the scope, retrieve and prepare the appropriate release, handle missing prerequisites, connect this client to the relay through MCP and help create a first project using the existing folders I identify. Verify the connection and an agreed small exchange, then show me where to monitor its progress. Ask only for missing choices, permissions or authentication you cannot handle; guide me through any step your tools cannot perform.

### Starting another project — relay already ready

> Help me connect this work to my existing Agent Relay installation: [purpose and existing folders]. For Agent Relay's documentation, use its private GitHub repository Sowapps/agent-relay: https://github.com/Sowapps/agent-relay. Read its README and getting-started guide, check your access and discover my actual relay installation and connection scope. Keep my agent organization and rules. Propose only missing participants/workspaces/permissions, and follow my approval process before changes. Preserve existing projects, verify the chosen project through MCP and show me where to monitor its activity.

The [getting-started guide](apps/agent-relay/doc/using/getting-started.md) explains what to provide, how to choose paths and what the agent should verify.

## What a relay project connects

A **Relay project / Projet relayé** associates participants, their **Workspaces / Espaces de travail** and permissions with a piece of work. Its sessions, operations and messages share that context. It can span Windows and Linux folders; it is more than one folder or conversation.

Select it in the application's navigation to see its tasks, activity, messages and sessions. **Vue générale** (Overview) shows activity across projects. Agents normally prepare and operate the relay through their authorized tools; the window provides monitoring and diagnostics.

## When you need more

Current adapter, direction, capacity and delivery boundaries are collected in [limits and evolution options](apps/agent-relay/doc/using/limits.md), with practical handling and possible improvements.

Use the [functional guide](apps/agent-relay/doc/using/mvp.md) for everyday details. Agents find operational instructions from the starting guide and MCP resources. Contributors have separate [development documentation](apps/agent-relay/doc/developing/README.md).
