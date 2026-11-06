# Development workflow

**Utility:** defines a proportionate path from authorized scope to verified delivery.  
**Usage:** apply when beginning and ending a workstream.

## Authorization and design

Present a concise scope and wait for explicit GO. Information does not authorize execution
(evidence `2026-10-28-work-starts-only-after-explicit-go`). Start a tracked objective and report its final consumption and duration.
For work that can span compaction, retain the scoped GO, decisions, artifact locations and current checkpoint in a dated workstream record under `notes/workstreams/`.
Update the checkpoint after material progress so a resumed agent can continue without repeating paid calls or expanding the authorization.

On an explicit “pause” or “save”, stop starting new work. Preserve the current edits, stop or account for owned running operations, and update the dated checkpoint with the exact safe resume action. Then stop and report using **Point de reprise** with three fields: **État**, **Sauvegarde**, **Reprise**. Resume only when the operator requests it.

Windows qualification must not trigger a Computer Use authorization prompt. Use existing non-interactive qualification methods that respect the approved access boundary. Record visual acceptance as pending if it requires that prompt.

If the goal tool refuses a new objective because an unrelated historic objective is unfinished, record that diagnostic and track this scope/timing in the workstream. Never falsely complete the unrelated objective or claim its consumption. Resume normal goal tracking when available; report unavailable metrics truthfully.

The handoff checkpoint contains authorization/limits, objective, branch and last delivered commit, decisions and their owners, completed checks with retained output locations, unresolved risks and the exact next action. Before resuming, read AGENTS, the design guide and the relevant application decision/workstream, then inspect repository and objective state. An old GO or old test is not new authorization or fresh evidence.

Before coding, identify the nominal scenario, important failure, ownership boundaries and structural prerequisites.
Compare meaningful alternatives, make routine technical choices and record their reasons. Ask the operator about product choices or authorization, not unfamiliar technical details.

For a significant design choice, use a dated application notes/decisions record: context, status, decision, alternatives, consequences, current owner and supersession. Reference operator evidence instead of copying it. Keep proposals separate from accepted decisions and link current docs to the owner only where useful.

Qualify unfamiliar infrastructure with an isolated example in persistent local storage. A build cannot prove interactive debugging or an external integration.
If a prerequisite is absent, record it, continue independent authorized work and prepare the next step. Do not silently install software or cross the gate.

## Implementation and verification

Define and apply the process and conventions with proportionate checks (evidence `2026-10-28-design-and-proportionate-checks-precede-development`).

Implement one small vertical scenario. Run its nominal path and failures that could lose data, violate directionality or hide unsuccessful actions.
Avoid tests that merely repeat code and repeated checks without new evidence.
Run `./tools/verify.ps1` for shared tooling. Passing routing fixtures does not prove application behavior; a Python POC does not prove a C# build.

## Delivery

Use a dedicated `codex/<scope>` branch from the integration branch. Within the authorized scope, commit and push completed changes autonomously. To validate, merge that branch into `main`, push the integration result and deploy only approved changes missing from the selected instance. Check remote changes before merging; do not force-push, discard unrelated edits or merge failing work. An already deployed matching runtime needs no redundant replacement. Tags and GitHub releases retain their own authorization gate.
(evidence `2026-11-01-dedicated-branch-delivery`)

Update current documentation. Keep dated measurements and unresolved proposals in notes. Report changes, passed checks and remaining qualification gaps concisely.
Close the objective when its authorized work ends. An experiment can complete with a negative technical result.
