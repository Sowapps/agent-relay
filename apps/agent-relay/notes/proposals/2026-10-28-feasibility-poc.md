# Feasibility POC proposal

**Creation:** 2026-10-28 — Codex.  
**Utility:** defines the bounded experiment needed before committing to the MVP's adapters.  
**Usage:** read before authorizing a real-client experiment or judging its outcome.

## Objective

Establish an end-to-end delegation and a recoverable result with one real pilot and one real worker, while distinguishing terminal delivery, task receipt and task completion. The
[product target](../../doc/target-spec/README.md) constrains the experiment; the [MVP proposal](2026-10-28-mvp-framing.md) lists mechanisms it may confirm or reject.

## Existing findings

The [observation report](../observations/2026-10-28-wsl-and-tmux-checks.md) records environment discovery, a cat echo on an isolated tmux socket, and loss of a disposable tmux session across a
distribution stop/restart. These checks are not a real-agent POC and are not yet operator-validated evidence.

The supplied guide reports prior real-agent measurements. They justify a concrete tmux experiment instead of rejecting the adapter in theory. Installed versions and launch environments still
require qualification; the guide is not a substitute for measuring this selected client pair.

## Authorization boundary

The early assistant interpretation of morning WSL availability did not constitute a workstream GO. Florent explicitly corrected that interpretation (evidence
`2026-10-28-work-starts-only-after-explicit-go`). The broader experiment below remains a proposal, not a standing authorization.

The outstanding experiment needs explicit authorization for its temporary implementation, workspace writes, real model calls and any client MCP or hook configuration. No software install, client
upgrade, permanent process, startup registration or production deployment is included. Existing agent sessions receive no input.

## Narrowed authorized experiment

The later GO authorizes one short Claude response and retrieval after relay/WSL restart, with objective-based tracking (evidence `2026-10-28-a-go-authorizes-only-the-one-response-poc`). It does not
authorize the wider multi-message, busy-input or takeover campaign below. Read the [outcome report](../observations/2026-10-28-one-response-poc.md) before proposing another attempt.

## Required choices

Select the pilot and worker products, a disposable Linux workspace, an isolated data area, a model-usage budget and the specific client configuration needed for the experiment. Reuse existing
authentication without reading or copying tokens into project files. Stop if authentication or permissions require an unapproved change.

Choose an explicit task-receipt and result mechanism before a real task is submitted. The POC must not define completion as text leaving the input area or an unchanged screen.

## Experiment sequence

1. Record selected binaries, versions, launch environment, workspace and test identifiers.
2. Start a disposable worker on a dedicated tmux socket; record session, pane, process and conversation identities.
3. Inspect startup screens and permission state; request human action rather than accepting them automatically.
4. Qualify literal terminal input at idle with a short message and a message around 400 characters.
5. Qualify a message while busy, distinguishing queued input from a received task.
6. Submit one read-only fixture task through the pilot's actual MCP tool and correlate the worker's receipt.
7. Produce a durable result, import it and confirm the exact task/attempt relationship.
8. Interrupt the worker environment at a chosen boundary and reconcile after restart, without automatic duplicate execution.
9. Test application-side interruption after result persistence but before import acknowledgment.
10. Suspend writes for human takeover, then explicitly restore control and recheck the worker.
11. Remove only experiment-owned resources and report success, failure or an unresolved limitation.

The pilot interface and worker transport are exercised together, but failures are reported separately so an MCP integration problem is not misdiagnosed as a tmux delivery problem.

## Measurements

For each send, record client version, correlation identifiers, text length, typing/wait/Enter/re-read timing and a verdict such as delivered-to-input-handler, still-in-input, unverifiable or not-sent.
Then record task receipt and result confirmation separately. Observe permission prompts and rate limits without silently retrying or granting approval.

Capture only test material and relevant diagnostics. Do not dump pre-existing conversations or credentials. Screens and process activity can support diagnosis but cannot replace correlated
acknowledgments.

## Stop conditions

Stop on ambiguous target identity, an unexpected shell/trust screen, an unrelated session, an unapproved permission change, an unknown task outcome or exhausted experiment budget. Preserve the
report and relevant test state, naming any resources left behind and why. Cleanup never uses the shared default tmux server or a broad filesystem deletion.

## Success criteria

A real pilot tool submission reaches the selected real worker. Receipt and completion are independently identifiable. One result survives a selected interruption boundary and is imported without
duplication. A lost or unverifiable execution produces an explicit outcome rather than a false success. Human takeover prevents conflicting writes. The test leaves unrelated settings and sessions
unchanged.

If task receipt, completion or result recovery remains heuristic, record the limitation and adjust the adapter before approving MVP development. Basic WSL and tmux availability alone cannot close
the feasibility gate.

## Deliverables

Produce a dated report with exact observed outputs, versions, experiment scope, remaining resources and limitations. Present it to Florent for validation before creating evidence records. Separate
that validation from decisions about production architecture and the authorization to build the MVP.
