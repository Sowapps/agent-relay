# Agent Session — Technical Target

**Utility:** states the decided architecture boundaries for agent interaction.  
**Usage:** read before designing an endpoint adapter or a domain dependency.

The agent-interaction model is independent of execution technology.

**Functional counterpart:** [Agent session functional target](../functional/agent-session.md).

## Hexagonal architecture

The architecture is hexagonal. The business core knows neither Codex, Claude, Windows, WSL nor tmux. Platform and client behavior belongs to adapters behind neutral application ports
(evidence `2026-10-16-relay-core-and-endpoints-are-neutral`).

## Endpoint identity and role

An endpoint identity is distinct from its project role and technical adapter. Replacing a client or platform adapter does not redefine what pilot, worker, reviewer or observer means
(evidence `2026-10-16-relay-core-and-endpoints-are-neutral`).

## Client compatibility

The public integration boundary accommodates Codex and Claude Code. MCP is the participant-facing integration protocol. Public usage documentation is discoverable with the connection and remains accessible without requiring the application checkout. A client-specific mechanism does not become a
business-core dependency (evidence `2026-10-16-relay-core-and-endpoints-are-neutral`).
