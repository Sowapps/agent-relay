# System I/O — Functional Target

**Utility:** states the Windows application lifecycle and durable-data requirements.  
**Usage:** read before defining application shutdown or interruption acceptance criteria.

The relay retains its application data independently of the WSL worker's availability.

**Technical counterpart:** [System I/O technical target](../technical/system-io.md).

## Direct command execution

The relay executes authorized commands in WSL without involving a WSL AI agent. A direct directory listing is a simple qualification case. Its command result is distinct from an
[agent-produced message](agent-session.md#agent-message-replies) (evidence `2026-10-28-mvp-separates-wsl-commands-and-agent-replies`).

The scope of allowed local work belongs to the [configuration target](configuration.md#authorized-local-work). Direct execution does not bypass those permissions or authorize an external effect.

## Windows application lifecycle

Agent Relay is a Windows application with a notification-area icon. It is opened manually by default. Its settings let the operator choose launch at Windows sign-in. The application closes cleanly
at Windows sign-out (evidence `2026-10-16-relay-runtime-is-manual-and-durable`).

## Durable storage

Tasks, messages, states, results and errors survive WSL shutdown and relay application restart. These data remain independent of a live terminal session
(evidence `2026-10-16-relay-runtime-is-manual-and-durable`).

## Worker availability

The product accommodates a stopped worker and a stopped or unstable WSL environment. Worker restart is available on demand or according to an explicit policy. A restart does not implicitly grant
permission for an external effect (evidence `2026-10-16-relay-runtime-is-manual-and-durable`).
