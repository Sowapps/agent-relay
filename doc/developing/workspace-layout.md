# Workspace layout

**Utility:** assigns files to stable owners in a multi-application repository.  
**Usage:** consult before adding an application, moving files or introducing shared code.

## Ownership

This organization follows the operator's multi-application requirement (evidence `2026-10-28-workspace-supports-multiple-applications`).

| Path | Purpose |
| --- | --- |
| `apps/<id>/` | One application's manifest, source, tests, tools, documentation and notes |
| `workspace.psd1` | Explicit application registry |
| `dev.ps1`, `tools/` | Shared commands without application business logic |
| `doc/` | Shared current practices |
| `notes/evidence/` | Canonical operator decisions for the whole workspace |
| `local/apps/<id>/` | Ignored application experiment data |
| `local/toolchains/` | Ignored tool configuration and qualification reports |
| `local/migrations/` | Reversible snapshots and file maps |

Use lowercase kebab-case application identifiers. Register each application and its driver explicitly. Mutating commands require application selection.
Keep SDK pins inside each application. Future applications may use another language without inheriting Agent Relay's .NET requirements.
Create shared packages only after concrete reuse exists.
Generated .NET `bin/` and `obj/` directories and local data are ignored. Local snapshots are temporary rollback aids, not a substitute for version control.

## Agent Relay

Current application folders are `poc/`, `doc/`, `notes/` and `tools/`, alongside `app.psd1` and `global.json`.
The source structure is `src/AgentRelay.Core/`, `src/AgentRelay.Infrastructure/`, `src/AgentRelay.Windows/` and `tests/AgentRelay.Tests/`.
The core owns relay rules, infrastructure owns storage/transport adapters and Windows owns composition/UI. Add projects only when an actual boundary requires one.

## Historical ownership

Application observations and proposals live under its own `notes/`. Operator evidence remains a single common registry with stable identifiers.
Do not duplicate proofs during moves. Preserve exact quotations and link application notes to the common registry.
