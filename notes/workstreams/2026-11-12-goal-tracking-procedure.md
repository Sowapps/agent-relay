# Goal tracking procedure correction

**Utility:** retains the demonstrated goal-slot recovery and documentation correction.  
**Usage:** consult with the current workflow before diagnosing a future objective creation refusal.

## Scope and observed resolution

Florent instructed the agent to finish the stale objective, then document its operation and procedure. The old goal API accepted `update_goal(status="complete")`. Its returned historical counters were 145775 tokens and 901 seconds; they do not measure later cleanup work. Creation of this workstream's adequate objective then succeeded with active status and initial counters of zero. This reproduces the successful slot recovery and closes WS-GOAL-01.

Earlier answers confused absence of a delete command with the ability to close and replace an objective. The tool did expose completion. The agent also allowed an exceptional timing-only fallback to continue across multiple workstreams. The workflow now requires resolving that condition before further work and names the actual get/create/update sequence, status restrictions, usage attribution and closure.

## Checkpoint

Branch `codex/goal-tracking-procedure`, from `c85cb31`. A new active tracked objective covers this documentation-only correction. Changes are limited to the workflow, operator evidence, open issue and recovery index. Source commit `2d3e16f` is pushed and integrated by the merge carrying this checkpoint. Verification is complete. Close this workstream's objective after delivery and report its own final usage; the next workstream reads those final counters before replacing it.

A subsequent `get_goal` returned active status with 5267 tokens and 69 seconds, establishing that the new counter is functional. This is a progress measurement, not the final usage. The old historical counter is not added to it. Documentation verification passed for links, anchors, indexes, evidence, entry-point/install contracts and toolchain restoration. No application check is needed for this documentation-only correction. Self-review replaces the misleading indefinite fallback at its existing workflow owner rather than adding another competing procedure.
