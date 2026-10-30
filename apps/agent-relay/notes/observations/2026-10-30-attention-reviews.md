# Durable attention review

**Date:** 2026-10-30  
**Status:** implementation and local qualification complete; measurements await Florent’s validation.  
**Utility:** records the scoped review action and verification without changing historical outcomes.  
**Usage:** consult alongside current Windows interface documentation.

Florent gave GO for « Marquer comme examiné »: remove an alert from points to verify, preserve the failed history and re-alert if the operation evolves. The native host now stores revision-bound project/operation tokens in attention-reviews.json under its selected data directory. Alert banner, General count and operation/task follow-up filters share the same predicate. Ordinary history retains actual state plus an examined annotation. The detail drawer and inline alert expose the action; browser display changes only after native confirmation. Stale tokens are refused after a passive exact retained read. No native approval, retry, command or model is triggered.

Storage uses atomic flushed replacement, separate from journal facts and browser cache. A corrupt/unreadable file hides nothing, is preserved and blocks silent overwrite with a visible warning. Existing participant MCP outcomes/tool capabilities are unchanged. No new dependencies or global settings were added.

## Verification

- Offline build passes without warnings/errors. Full integration/state/session/permissions/transfer tests and Windows process smoke pass under local/apps/agent-relay/tests/bfaef729257d4b42b35537b4f1c48a0d.
- Added storage tests: close/reopen durability, one-tick revision change, different project with same operation ID, state change, stale-token refusal and corrupt-file preservation.
- Native conformance30views passes; added UI cases for removal from banner/follow-up, retained failed history and reappearance of an evolved operation.
- Actual native controller/action/store bridge passes on fresh synthetic fixtures attention-review-20261030b and attention-review-20261030c; the latter runs compact and checks the retained SQLite outcome directly after reopening. Captures before/after/reopened were inspected. No fixture invokes native agents or WSL.
- First vertical fixture attention-review-20261030 was inconclusive because ExecuteScriptAsync returns a Promise representation without awaiting native completion. The test window closed too early. It was retained as a failed diagnostic, then corrected to bounded asynchronous polling of observed state and durable file in a new fixture. Do not count the failed attempt as a pass.
- Final passive real-data render: local/apps/agent-relay/qualification/attention-review-real/render, manually inspected. The actual oversized transfer rejection remains awaiting the operator’s review; no real alert was automatically acknowledged.
- MCP list_projects succeeds from this conversation after publication; no inbox import, native inspection, native agent prompt or WSL command was used.

## Publication and continuity

Guarded publication applies only to v1-qualification, with process identity/idle checks, copied preflight, backup and rollback. Current ownership/runtime and transaction are recorded in local/apps/agent-relay/reports/attention-review/host.json and mirrored v1-qualification/ui-host.json. Existing review files remain in place across deployment; no journal outcome is changed. Earlier source snapshot: local/apps/agent-relay/migrations/attention-reviews-20261030/before.zip. The reviewed HTML prototype remains unchanged.

## Accounting

Start05:22:26UTC/06:22:26Europe/Paris; finish approximately05:32UTC/06:32Europe/Paris (~10minutes). Shared quota43percent before and after at integer precision; no attributable token count is available, no reset used (one credit remains). New tracked objective creation was refused because an unrelated blocked goal occupies the slot; its frozen145775tokens/901seconds are not this task’s consumption. Scoped objective/checkpoints are retained in notes/workstreams/2026-10-30-attention-reviews.md, now complete.
