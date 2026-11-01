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

## Delivered

Implementation28f96c4 merged as c9f392b; guide alignment5baf86d is integrated/pushed. Final standard deployment transaction var/apps/agent-relay/deployment/v1-qualification/deployment-37e62f4838a040c5a63da39c952ee39d uses immutable runtime62E14CEB61053EF3E9397C9B6483BFF7D134206F962DF70C3837298618B14156; own host health confirmed (PID44220 at deployment; revalidate identity before future actions). Seven protected project/profile/review/connection files retained identical hashes. No existing project was requested for archive, reactivated or edited; no user workspace touched. Production presentation/native assets match source.

Both matching existing MCP registrations now use that runtime, with identical connection IDs and unrelated settings; CAS backups retained under var/apps/agent-relay/deployment/v1-qualification/deferred-archive-registration. Fresh real stdio MCP discovery succeeds;25tools/5resources and7guide examples validated. Packaged quickstart explicitly documents archiveRequested and project_draining. Verification results: var/apps/agent-relay/deployment/v1-qualification/deferred-archive-qualified.json, local/apps/agent-relay/qualification/deferred-archive-deployed-docs/result.json. Deployed lifecycle scenario passed at local/apps/agent-relay/qualification/project-lifecycle-593bb696864c472685d608fda4ad9d21/result.json, including pending state, diagnostics, actual host restart and automatic finalization. No model or WSL calls. No tag/release.

Completed20:07UTC /21:07Europe/Paris on01/11/2026, about12minutes after the first timed checkpoint (initial discovery preceded it). Scoped token use remains unavailable because goal creation was rejected by the unrelated historical blocked goal; its counters are not this task's usage. Final notes-only commit follows the same dedicated branch and main integration; it requires no runtime replacement.
