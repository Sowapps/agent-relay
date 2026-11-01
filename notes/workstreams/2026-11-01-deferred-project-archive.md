# Deferred archival and list actions

**Utility:** recovery record for the explicitly authorized correction.  
**Usage:** continue through checks, branch commit/push, merge/push and selected-instance deployment. No release, models, existing project archival or unrelated settings changes.

## Scope and design

Florent's GO covers direct project/settings icons with title; archival must persist its intent, block new work immediately, allow existing work to finish and finalize automatically. Only Active/Archived remain public states; archiveRequested/diagnostic are transitional metadata. Unknown/approval/verification problems retain the request and explain why it cannot finish; reactivation cancels that request without replay. Idle ready sessions need not be closed: retained conversations remain historical and archived policy blocks subsequent commands. The native host owns a cancellation-aware periodic reconciliation loop, not the UI or an individual MCP call; restart retains intent. Admission serializes transitions; all pages and existing work are rechecked before finalization. Existing session interrupt/close and history/recovery remain available during drain; new start/reopen/rename/messages/commands/transfers/inspection are blocked.

## Checkpoint

Branch codex/deferred-project-archive from main d1d930f. First recorded checkpoint19:55UTC /20:55Europe/Paris on01/11/2026. Goal creation attempted but refused because the API retains an unrelated historical unfinished/blocked goal; do not attribute its counters to this work. Rule recorded in AGENTS and evidence. Implementation next.

## Verification checkpoint

Ordinary suite passed at local/apps/agent-relay/tests/50542af64244475d84b24e56ab6eeda8 including actual paused fake work, pending admission refusal, reconstruction, idle-ready acceptance and finalization without replay. A first concurrent preparation hit a transient locked staging directory; rerunning sequential preparation completed. UI test found an undefined component alias in the new directory; fixed by importing the shared iconButton. Native UI then passed at local/apps/agent-relay/qualification/deferred-archive-visual/render/verification.json; captures inspected at1120/816px, including pending diagnostic and cancellation. Follow-up CSS keeps pending row action icons aligned; final visual rerun next. Actual stdio MCP candidate test passed at local/apps/agent-relay/qualification/project-lifecycle-0f1f5786b5514ba8ba76be105d101477/result.json: persisted request, unknown diagnostic, restart, automatic completion after resolving only fictional work; no model/WSL calls.

Remaining: final visual/docs check after small CSS/toast refinement, prepare current candidate for packaged guide qualification, branch commit/push and merge/push, standard selected-instance deployment with seven protected files preserved, update only matching existing MCP command entries (identities/settings unchanged), final branch bookkeeping. No real project archive request.

Final UI check passed at local/apps/agent-relay/qualification/deferred-archive-dpi/render/verification.json, with direct settings icon clicks, titles/accessibility labels and both normal/pending geometry checks. The desktop moved to150% scaling during qualification (observed innerWidth530/dpr1.5 for the narrow native window); a responsive two-row layout now preserves full text and action bounds. Captures inspected after the fix. Shared verification and whitespace checks passed. Source ready for branch integration and deployment; latest source package/document guides will be verified through the deployed binary.
