# Development workflow

**Utility:** defines a proportionate path from authorized scope to verified delivery.  
**Usage:** apply when beginning and ending a workstream.

## Authorization and design

Present a concise scope and wait for explicit GO. Information does not authorize execution
(evidence `2026-10-28-work-starts-only-after-explicit-go`). Start a tracked objective and report its final consumption and duration.
For work that can span compaction, retain the scoped GO, decisions, artifact locations and current checkpoint in a dated workstream record under `notes/workstreams/`.
Update the checkpoint after material progress so a resumed agent can continue without repeating paid calls or expanding the authorization.

Before coding, identify the nominal scenario, important failure, ownership boundaries and structural prerequisites.
Compare meaningful alternatives, make routine technical choices and record their reasons. Ask the operator about product choices or authorization, not unfamiliar technical details.

Qualify unfamiliar infrastructure with an isolated example in persistent local storage. A build cannot prove interactive debugging or an external integration.
If a prerequisite is absent, record it, continue independent authorized work and prepare the next step. Do not silently install software or cross the gate.

## Implementation and verification

Define and apply the process and conventions with proportionate checks (evidence `2026-10-28-design-and-proportionate-checks-precede-development`).

Implement one small vertical scenario. Run its nominal path and failures that could lose data, violate directionality or hide unsuccessful actions.
Avoid tests that merely repeat code and repeated checks without new evidence.
Run `./tools/verify.ps1` for shared tooling. Passing routing fixtures does not prove application behavior; a Python POC does not prove a C# build.

## Delivery

Update current documentation. Keep dated measurements and unresolved proposals in notes. Report changes, passed checks and remaining qualification gaps concisely.
Close the objective when its authorized work ends. An experiment can complete with a negative technical result.
