# Workspace working rules

**Utility:** Defines workspace working rules and the first documentation links for agents.  
**Usage:** Read before changing this workspace or preparing an authorized relay/project; scope and approval rules apply throughout.

## Using Agent Relay through MCP

For first preparation, deployment, project addition or human UI assistance, read the [bootstrap/administration guide](apps/agent-relay/doc/using/bootstrap.md) (`agent-relay://docs/bootstrap` over MCP). It includes the administrative CLI example and its review/authorization gates.

For an agent consuming the relay (rather than changing its source), start with the [MCP agent quickstart](apps/agent-relay/doc/using/agent-quickstart.md) and [live tool contract reference](apps/agent-relay/doc/using/mcp-tools.md). Connected clients without checkout access can read MCP resources `agent-relay://docs/quickstart` and `agent-relay://docs/tools`. Discover projects/rights before acting; read session/inbox/uncertainty rules before submitting work. The rules below still govern work in this repository.

## Authorization

Information, prerequisites, availability and acceptance of a proposal are not permission to start work. Present the concrete scope, ask for an explicit GO and wait before starting any workstream.
A GO applies only to its stated scope. Do not expand a POC into MVP development or apply configurations globally.
(evidence `2026-10-28-work-starts-only-after-explicit-go`)

## Goals and reporting

Start an authorized workstream with a tracked objective. Use its consumption and timing data in the result. Keep user-facing plans and reports short; keep technical detail in the appropriate files.
(evidence `2026-10-28-a-go-authorizes-only-the-one-response-poc`)

## Recovery across compaction

Before resuming interrupted work, read the applicable dated record in [workstreams](notes/workstreams/README.md) and inspect the tracked objective.
Keep the authorized scope, current checkpoint, material diagnostics and next actions in that record. Do not infer permission for a new workstream from a completed lot's GO.

## Documentation

Read [documentation maintenance](doc/documenting/doc-maintenance.md). Keep current documentation in `doc/`, dated material in `notes/`, and one operator-validated fact per file in `notes/evidence/`.
Preserve the French and English terms in the [glossary](apps/agent-relay/doc/developing/glossary.md). A measured result remains an observation until Florent validates it.

## Development

Follow the [workflow](doc/developing/workflow.md), [layout](doc/developing/workspace-layout.md) and [conventions](doc/developing/conventions.md).
Qualify structural prerequisites before application implementation. Keep application rules and experiments inside their application.
Do not interact with existing agent sessions, install software or create automatic startup mechanisms without authorization covering those actions.
