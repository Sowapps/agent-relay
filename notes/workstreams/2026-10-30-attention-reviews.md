# Durable attention review

**State:** complete; tested and published on existing qualification data.  
**Objective:** add « Marquer comme examiné », remove acknowledged operation revisions from alert/attention counts and follow-up filters, preserve actual failed history, re-alert when the operation evolves; test, document and publish on the existing local qualification data.

## Authorization

Proposal: « Marquer comme examiné : retirer l’alerte des points à vérifier, conserver l’échec dans l’historique et la réafficher si son état évolue. Ce petit correctif nécessite ton GO. » Florent: « GO ».

No WSL/model/native-agent request, new dependency, global setting or automatic startup. Existing reviewed HTML prototype remains unchanged. Scope is the monitoring review state, not changing an operation outcome or adding MCP administrative capability.

## Accounting and design

Start2026-10-30 05:22:26UTC /06:22:26Europe/Paris, shared quota43percent used, one reset credit available. get_goal inspected; create_goal refused because an unrelated unfinished blocked integration objective occupies the slot. Do not attribute its145775tokens/901seconds or falsely close it. This record tracks the scoped objective manually.

Native presentation review storage under the selected data directory, separate from journal outcomes. Scope entries by project/operation plus exact outcome-revision token (state, precise UpdatedAt and error code). Validate the current retained operation/token before accepting an action; a stale drawer must not acknowledge a newer result. Keep history states unchanged. Persist atomically; a corrupt preference file must fail open for visibility (no acknowledged hiding), preserve the damaged file and expose a warning. One application host owns the selected data directory.

Verification: durable mark/reopen, same operation ID in another project, stale revision refusal, updated revision visible; native renderer alert/count/filter and history retained, then offline regression and guarded publication. Backup local/apps/agent-relay/migrations/attention-reviews-20261030/before.zip.

## Checkpoint — storage and renderer implemented

AttentionReviews stores project/operation/revision tokens atomically in attention-reviews.json, leaving journal facts unchanged. DTOs expose reviewToken/reviewed/reviewable. Native mark-reviewed passively fetches the exact retained operation and refuses stale tokens. Controller waits for native confirmation; full-detail cache merges updated review state. Shared needsAttention predicate controls banner/counts and operation/task follow-up filters; ordinary history keeps original states and an examined annotation.

Offline full suite passes including scoped restart/revision/corrupt-file tests and Windows process smoke: local/apps/agent-relay/tests/bfaef729257d4b42b35537b4f1c48a0d. Native conformance30views passes including model review removal/reappearance/filter/history cases. End-to-end synthetic native review initially failed because WebView2 ExecuteScriptAsync serializes a Promise without awaiting its fulfillment; the test closed the window before native confirmation. Before capture shows the real button. Corrected test to await observed native state/file with a bounded async C# poll; retry uses a fresh fixture, not overwriting failed artifacts. No deployment yet; old application unchanged.

## Native vertical verification

Second fresh fixture attention-review-20261030b passes actual native controller/action, retained read, flushed store write and window restart; history remains visible annotated examined. Before/after/reopened own captures inspected. Grouped alert actions and compact stacking avoid adding loose buttons or horizontal overflow. Native conformance rerun passes; final compact vertical case adds an actual SqliteWorkStore outcome check. First failed fixture retained as diagnostic; never treated as passing proof. Current docs describe local monitoring state separately from MCP operational outcomes. Next guarded qualification publication, then passive MCP continuity and final report. No real alert automatically acknowledged.

## Closure06:32Europe/Paris

Native compact vertical verification passes including actual unchanged SQLite outcome after restart. Final conformance30views and real passive render pass; the live oversized transfer rejection has not been automatically reviewed. Final publication PID53476, runtime52B8CC7182020F599404287A1D70B0C05C460D690A34514B779B2A7FF2BAD1B1, transactiondeployment-02694a9059b949f28f5404783bcbb409. Metadata at reports/attention-review/host.json and v1-qualification/ui-host.json is authoritative. Actual MCP project discovery works afterward. Current docs and tools README updated, dated observation indexed. No further scoped development remains. Start05:22:26UTC, approximately10minutes, shared quota43→43percent at integer precision, no reset. Goal service remains occupied by unrelated blocked goal; scoped objective manually closed here without falsely completing it.
