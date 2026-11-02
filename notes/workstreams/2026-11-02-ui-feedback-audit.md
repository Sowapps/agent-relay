# UI feedback audit

**Utility:** preserves the authorized audit scope and completion checkpoint.  
**Usage:** read before proposing or starting the separate correction implementation.

Florent: « GO analyser et déterminer tout ce qu'il faut corriger et les causes. » Scope is analysis and correction planning only. Every correction must later be applied to the prototype first, then to the app. No source/runtime/configuration change, installation, model/WSL call or deployment is authorized by this audit.

Objective: identify causes for all supplied feedback (modal spacing, refresh/state loss, unknown diagnostics, environment identity, breadcrumb hierarchy, tabs, section/list consistency and prototype drift) and define a bounded correction/verification sequence.

Goal API inspection/creation attempted: unavailable because of an unrelated unfinished blocked historical goal. Its consumption/time counters are not reused. This record tracks the audit; separate token accounting is unavailable.

Result: [source findings and correction plan](../../apps/agent-relay/notes/observations/2026-11-02-ui-feedback-audit.md), 17 findings, evidence level and minimal acceptance scenarios. Maintained prototype/standalone equivalence checked in memory, without regeneration; source matches standalone, but both lag production. Current baseline hash differs from the conformance tool's expected frozen reference.

Confirmed main causes: full innerHTML replacement plus ID-only restoration, heartbeat-sensitive whole-snapshot signature, hardcoded unverified states/default green dots and instance count, flattened adapter environment strings, duplicated navigation/tab/section templates, outdated separate prototype/reference, and missing update-preservation coverage.

No implementation performed. Dynamic qualification of stale archive-intent races remains a specific implementation prerequisite, not a claimed reproduced incident. Next action requires implementation GO covering prototype-first corrections and production delivery. No product clarification is required for the current plan.
