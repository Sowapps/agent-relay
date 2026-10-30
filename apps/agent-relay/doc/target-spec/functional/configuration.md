# Configuration — Functional Target

**Utility:** states how relay projects associate workspaces, endpoints and permissions.  
**Usage:** read before changing project membership or configuration authority.

Configuration identifies which participants may exchange work and which workspaces they may use.

**Technical counterpart:** [Configuration technical target](../technical/configuration.md).

## Project membership

A relay project associates workspaces, endpoints and access rules. Its model accommodates multiple workspaces and agents; the restricted execution capacity is owned by the
[agent-session target](agent-session.md#execution-capacity) (evidence `2026-10-28-central-configuration-and-extensible-model-are-accepted`).

## Workspace boundaries

A workspace is an authorized working directory, separate from Agent Relay application files and application data. A Windows workspace and a Linux workspace are distinct resources. Their association
grants no file synchronization or implied identity of their contents (evidence `2026-10-28-project-vocabulary-keeps-french-and-english`).

## Configuration authority

The MVP configuration is centralized in Agent Relay. Workspaces contain working files, not the authority that grants relay permissions. A possible workspace declaration can propose settings, but
cannot grant rights or trigger a worker by itself (evidence `2026-10-28-central-configuration-and-extensible-model-are-accepted`).

## Authorized local work

Project permissions can authorize file changes and local command execution in the assigned workspace. The MVP is not restricted to read-only analysis. Agent tool permissions and direct relay
commands respect the project's declared scope (evidence `2026-10-28-mvp-supports-local-work-and-both-agent-clients`).

This local-work capability does not replace the [specific authorization required for external effects](agent-session.md#external-effects).

## MVP directional capabilities

In the MVP project profile, the Windows endpoint can send messages and request authorized commands on WSL. The WSL endpoint can send messages to Windows but cannot request command execution through
the relay. This restriction does not remove the WSL agent's separately authorized local tools (evidence `2026-10-28-wsl-can-initiate-messages-but-not-relay-commands`).

Capabilities belong to configured endpoints. They do not hard-code operating-system identities into the neutral business core.
