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

Start each authorized workstream with a functioning tracked objective matching its scope. Resolve an unfinished historical objective before beginning another workstream. A dated note supports recovery but does not replace this prerequisite (evidence `2026-11-12-functional-objective-required`). Follow the objective procedure below.

The handoff checkpoint contains authorization/limits, objective, branch and last delivered commit, decisions and their owners, completed checks with retained output locations, unresolved risks and the exact next action. Before resuming, read AGENTS, the design guide and the relevant application decision/workstream, then inspect repository and objective state. An old GO or old test is not new authorization or fresh evidence.

Before coding, identify the nominal scenario, important failure, ownership boundaries and structural prerequisites.
Compare meaningful alternatives, make routine technical choices and record their reasons. Ask the operator about product choices or authorization, not unfamiliar technical details.

For a significant workstream, define the target first: required behavior, responsibility boundaries, contracts, invariants, failure/recovery guarantees and explicit limitations. Do not constrain it to preserving a flawed existing decomposition. Compare meaningful alternatives and challenge the target against requirements, existing rules, security, compatibility, concurrency, interruption, deployment and maintenance. Revise until another focused review finds no new concrete contradiction; record review passes and unresolved items rather than claiming exhaustive certainty. Only then derive the gap map and intermediate lots (evidence `2026-11-13-target-first-point-planning`).

Immediately before each point, refine its local execution plan using the actual checkpoint: owning code and complementary operations, exact changes and exclusions, prerequisites, effects, error/recovery path, useful verification, completion criterion and next point. Treat points sequentially. A discovery that invalidates the target returns to design; do not accumulate ad hoc bypasses. Preserve the updated plan and result before advancing. This supplements the existing authorization, branch, tracking and delivery processes; it does not create a separate competing workflow.

Use analysis to choose the necessary proofs. Reuse retained valid results, and rerun a test only when changed code, environment, a failure or an unresolved hypothesis justifies it. Prefer one decisive qualification of a structural uncertainty to repeated broad regression runs. A test cannot compensate for a contradictory target or silently authorize a product compromise.

For a significant design choice, use a dated application notes/decisions record: context, status, decision, alternatives, consequences, current owner and supersession. Reference operator evidence instead of copying it. Keep proposals separate from accepted decisions and link current docs to the owner only where useful.

Qualify unfamiliar infrastructure with an isolated example in persistent local storage. A build cannot prove interactive debugging or an external integration.
If a prerequisite is absent, record it, continue independent authorized work and prepare the next step. Do not silently install software or cross the gate.

Before operating, find the current procedure in the application documentation and check the [open issues register](../../notes/open-issues.md) for a recurring symptom. Reuse the existing command or checklist. When a step is missing or demonstrably wrong, correct its owner and record the change in the same lot (evidence `2026-11-09-procedure-improvement-loop`).

Add unresolved blockers, errors, defects and process gaps to that register as they are encountered. Reuse the existing ID for a recurrence and update its last observation and occurrence count. Record the observed impact, a proof reference and the next action. After a correction, require a check that reproduces the original scenario before closing the item. Preserve the result in its workstream or observation.

## Tracked objective procedure

The exposed tools are `get_goal`, `create_goal` and `update_goal`. They provide no delete, cancel or objective-renaming command. Replacing a completed objective with a newly created one is different from deleting it.

`blocked` and `paused` are unfinished states and do not free the slot. Use `blocked` only after the same actual impasse recurs for at least three consecutive goal turns and no meaningful progress is possible. Pause only at the operator's explicit request. A turn ending or a context compression is not a reason to close an ongoing objective.

1. Before work, call `get_goal` and inspect its objective, status and counters. Match it to the authorized scope rather than relying on conversation memory.
2. If the same workstream remains active, retain it and its baseline counters. Compression or a resumed conversation does not create another scope. Pausing requires the operator's request; resumption is controlled by the user/system.
3. Before treating an old objective as blocked, reread its original scoped workstream and use retained/fresh proportionate evidence to determine whether it is already finished. Do not silently extend its completion criteria to later corrections, another version or a new architecture. Close a completed scope yourself; do not ask the operator to manage the tool or approve a tracking workaround. If a prior objective is finished in substance, use `update_goal` with `status: "complete"` and report its returned usage. Preserve its identity and final counters in its own historical record. Do not attribute them to the next workstream or mark incomplete work achieved merely to free the slot.
4. Call `create_goal` with the new concrete objective. Supply `token_budget` only when the operator explicitly requested one. An unfinished existing objective causes creation to fail. Retrying the same request does not resolve it.
5. Verify the returned objective is appropriate and active before starting the workstream. If creation is refused, identify and resolve the previous objective's disposition. Report the blocker immediately and obtain the necessary operator action. Do not proceed through several lots with a timing-only fallback.
6. At each lot boundary, inspect the tracked counters and update the dated checkpoint with scope, progress, checks and next action. State the measurement boundary and avoid interpreting cumulative or historical usage as the current lot's consumption.
7. Once all authorized work and delivery are complete, close that objective with `update_goal`, report the final returned token/time data, and leave the next workstream to create its own objective.

When tool behavior is uncertain, read its current schema and test the supported operation within the authorized scope. Distinguish the tool's actual refusal from an assumed limitation. Record the successful resolution and update the open issue, rather than repeatedly copying a historical blocker.

## Implementation and verification

Define and apply the process and conventions with proportionate checks (evidence `2026-10-28-design-and-proportionate-checks-precede-development`).

Implement one small vertical scenario. Run its nominal path and failures that could lose data, violate directionality or hide unsuccessful actions.
Avoid tests that merely repeat code and repeated checks without new evidence.
Run `./tools/verify.ps1` for shared tooling. Passing routing fixtures does not prove application behavior; a Python POC does not prove a C# build.

## Delivery

Use a dedicated `codex/<scope>` branch from the integration branch. Within the authorized scope, commit and push completed changes autonomously. To validate, merge that branch into `main`, push the integration result and deploy only approved changes missing from the selected instance. Check remote changes before merging; do not force-push, discard unrelated edits or merge failing work. An already deployed matching runtime needs no redundant replacement. Tags and GitHub releases retain their own authorization gate.
(evidence `2026-11-01-dedicated-branch-delivery`)

Update current documentation. Keep dated measurements and unresolved proposals in notes. Report changes, passed checks and remaining qualification gaps concisely.
End each workstream with a short self-review. Identify one concrete improvement to the tool, procedure or decision that caused avoidable work. Implement a small in-scope correction or propose the next authorized step and track it while open.
Close the objective when its authorized work ends. An experiment can complete with a negative technical result.
