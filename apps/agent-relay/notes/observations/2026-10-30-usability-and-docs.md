# Usability and documentation lots1–4

**Date:** 2026-10-30  
**Status:** implemented, tested and locally published; observations await Florent’s validation.  
**Utility:** records the authorized monitoring/documentation improvements and qualification limits.  
**Usage:** consult with the scoped workstream and current Windows interface/user documentation.

## Scope and behavior

Florent authorized lots1–4 and explicitly deferred version/GitHub selection. No tag, release/version script, repository initialization or GitHub publication was performed. No new dependency, global client setting, automatic startup or existing native agent session was changed. The reviewed HTML mock remains immutable.

Lot1 gives every clickable monitoring control a perceptible hover while preserving keyboard focus, including active tabs/navigation and the review cross. Its title states both acknowledgement and the project → Activité → Tout l’historique / Tous les états path. Native success exposes Retrouver dans l’historique, clearing unrelated filters and opening the retained operation. Review state/outcomes remain separate.

Lot2 defaults histories/reset to all-history, explicitly names Tous les états, distinguishes shown/matching/loaded counts, identifies partial pages and explains empty filters with a reset. Tasks, Activity, Messages and Sessions use shared helpers; badges describe loaded context rather than an implicit filtered total. Session date filtering means last retained activity, or unknown when no dated activity exists. No date is invented.

Lot3 adds project → Sessions → Conversation and links from session/operation/message details. One selected session uses existing passive observe_operations/observe_messages with project/session scope, separate bounded cursors, retained canonical merge, explicit older loading and loaded/error state. Requests, retained results/errors, correlated replies and independent reports are ordered chronologically. Exact correlated reply suppresses duplicate result text. Unknown request linkage is labelled, not invented. Previews open exact retained details. The page does not import native transcripts/inboxes, inspect a client or start a model.

Lot4 introduces one canonical agent quickstart/tool reference, linked from root AGENTS.md, root/application README and documentation indexes; the human setup/monitoring guide and current architecture references are updated. The main MCP facade packages and serves two whitelisted public Markdown resources and advertises them on initialize: agent-relay://docs/quickstart and agent-relay://docs/tools. No resource URI grants filesystem access or project privileges. The native one-tool Python endpoint source contains a narrower self-contained usage resource/instructions; existing WSL processes/files were not synced/restarted. Older connected facades may need their client’s normal supported reconnect to discover added resources; no client reload was forced.

## Checks and observations

- Offline build: zero warnings/errors. Complete local regression suite plus Windows process smoke passes, artifacts local/apps/agent-relay/tests/58c092ea05044030a3ec650bdfd48458.
- Production C# rendering:16views at1120x728 and960x648 (32captures), including the conversation page. Bounds, content, horizontal overflow and expected drawer checks pass. Historical data with reference date2040 checks all four project collections, all-history default, today-empty behavior and reset; all three General feeds pass. Review shortcut, independent thread reports and cross-project isolation are checked. Captures inspected under qualification/native-conformance.
- Actual native UI/source fixture: qualification/usability-20261030 exercises a target conversation behind60newer foreign-session operations, >100own operations and independent report, then loads earlier scoped pages and checks all collection contexts. qualification/usability-review-20261030 confirms real acknowledgement/store/reopen/history regression. No native agent/WSL execution.
- Fresh MCP documentation-only client reads both packaged guides, checks24tool names/required arguments and7JSON argument examples against live schemas, and verifies arbitrary resource paths are refused. Templates are not executed. Published-package check repeats successfully under qualification/mcp-docs-published-20261030/result.json.
- Secondary documentation first attempted a Windows fixture and correctly hit the Linux /srv root/ownership guard. It was not bypassed. Native guide verification is explicitly static AST/schema/syntax only; no Linux/native runtime qualification is claimed. Existing native processes remain untouched.
- Actual published application interaction: Computer Use targets only Agent Relay. Hovering Opérations without activating it produces a visibly different background/border; hovering the cross changes its appearance and exposes the full action/history description. Project → Sessions → Conversation opens real retained history without native inspection. Navigation and cross were not used to acknowledge any real alert.
- Passive published real-data captures pass, including conversation-real.png, under qualification/usability-real/render. Actual current-conversation MCP list_projects succeeds after publication. No model, command, inbox import or permission-changing tool call was used.
- Changed documentation links/Python syntax pass. External operator-supplied WSL documentation paths returned not found; existing repository conventions were retained rather than inventing external rules or starting WSL.

## Deployment and continuity

Current identity/runtime/transaction are in local/apps/agent-relay/reports/usability-docs/host.json and mirrored v1-qualification/ui-host.json. Publication used copied preflight, exact owned/idle host validation, rollback snapshot and health confirmation, only on v1-qualification. Source/docs backup migrations/usability-docs-20261030/before.zip is retained. Application is left on a real conversation. No real alert is automatically acknowledged.

Reproduce with normal offline build/test; tools/check-interface-conformance.ps1; AgentRelay.Tests.dll monitor-usability <empty-data-directory> <capture-directory>; and tools/check-mcp-docs.py with explicit private dotnet/staged assembly/output paths. These helpers keep fake data out of normal UI and model calls at zero.

## Accounting

Start05:52:49UTC/06:52:49Europe/Paris; finish approximately06:27UTC/07:27Europe/Paris (~34minutes). Shared quota43→44percent at integer precision; no reset used, one credit remains. The goal service refused creation because an unrelated blocked unfinished goal occupies the slot; its145775tokens/901seconds are not this workstream. The scoped objective is tracked and closed in notes/workstreams/2026-10-30-usability-and-docs.md. No required lot1–4 development remains; version/tag/GitHub are deferred by the operator.
