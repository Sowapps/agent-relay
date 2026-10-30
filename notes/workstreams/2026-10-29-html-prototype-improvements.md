# HTML prototype improvements

**Date:** 2026-10-29  
**State:** completed; awaiting operator review of the prototype.  
**Objective:** improve the isolated prototype's neutral participants/connectors/connections, task grouping, actionable follow-up, history filters and realistic simulated data; verify the resulting views without changing the application or adding translation.

## Authorization and scope

Florent: « Laisse la traduction de coté, sinon GO pour ajouter le reste à la maquette ». Accepted proposal: generic participants with connector/connection details; shared task operation/message/result timeline; distinguish running/approval/blocker/uncertain states with contextual actions; simple date/participant filters and optional advanced filters; scenarios with long messages, retained history and limited connector capabilities. Preserve the existing visual language, reusable components and exterior organized simulation tools.

Only local/apps/agent-relay/prototypes/interface-v1 and workstream notes/index may change. No C# application, runtime/configuration, global settings, actual agent session, native model prompt or installation. Browser interaction is limited to the isolated prototype tab. Translation is expressly excluded.

## Accounting and recovery

Start08:35:39UTC (09:35 Europe/Paris), quota80%. Both reset credits intact. Native create_goal was attempted and refused because the unrelated integration objective remains blocked/unfinished at frozen145775tokens/901seconds. Do not falsely complete it; this dated objective and quota/wall-clock observations carry the scope. Read this record and inspect the objective before resuming after compaction.

## Design and checkpoint

Separate participant identity, agent connector capabilities and environment connection descriptors in fixtures; renderers must not branch on a product/transport name. Add explicit task IDs, one shared task aggregate/timeline and no invented reply/task linkage for independent messages. Filter predicates are reused across task/activity/inbox views. Native approval and uncertain results never imply auto-approval/replay. Unsupported actions are explained using connector capabilities. Data-heavy scenarios remain bounded and deterministic.

Next: preserve initial prototype bundle; update fixtures/domain/view renderers, rebuild portable HTML, verify new and existing nominal flows plus compact layout in the isolated browser, record results and final consumption. Previous prototype baseline is in 2026-10-29-html-prototype.md.

## Implementation checkpoint

Initial baseline retained as before-improvements.zip. Fixtures now declare participants, agent connectors/capabilities and environment connections independently; no provider or transport branch remains in prototype.js/model.js/views.js. New pure model.js owns task aggregates/shared filter predicates; views.js owns reused history/task lists, bounded task timelines and contextual follow-up; scenarios.js owns deterministic bounded fixture variations. Main renderers/controllers use declared identities and capability gates. No translation/catalogue work.

Portable HTML currently about96KB, built with explicit module order. JavaScript syntax compiled successfully before browser checks. Browser tests so far pass task linkage, approval handoff, blocked-to-message action, uncertain-to-evidence without replay, unsupported-session-action hiding, a4300+character message with wrapping/copy,186-operation history bounded to5/15 rows, date filters and participant filtering. Task/activity project views needed their attention banner added; corrected. Task events tied at the same timestamp now put the operation before its reply. Simulation panel height/compact task rows were adjusted after visual checks.

Existing browser tab acquisitions reported browser.user unavailable after the intervening client session, and numeric tab ID2 ambiguously matched a provider ID. Used the documented same-browser tabs.new() API to create a fresh isolated verification tab; no other desktop/window control. Full-page screenshot produced a broken compositor result while DOM geometry was correct; ordinary viewport screenshot is verified and must be used for this workstream's saved previews. Need finish advanced filters, task grouping/history scope, simulator linkage/reset, existing views and compact rendering, then update verification/README/accounting.

## Completed outcome and observations

All authorized improvements are implemented in the isolated prototype. Shared task/event history, contextual follow-up, participant/date/type/state filters, capability-based actions and deterministic robustness scenarios are complete. Baseline archive and verification remain intact. No C# source, actual runtime/configuration or real agent was changed; no translation was added.

verification-improvements.json records36 browser assertions with no unresolved failure. One initial clipboard read preceded asynchronous completion; the visible-completion retest passed with4273characters and explicitly resolves the retained initial assertion. Five pure model checks pass in verification-model.json. No console warning/error was observed. History filtering was exercised over186operations; compact960x680 and standard1120x760 layouts were visually inspected. Screenshots preview-improved-general/project/compact/long-message, preview-task and preview-limited-connector preserve the actual isolated-browser rendering. Observations are not operator-validated evidence.

Portable artifact: local/apps/agent-relay/prototypes/interface-v1/agent-relay-maquette.html,96068bytes, SHA2565DE37F5B43AA25C036F23FCC62F2F3FBDF1C6059A18D43F260A368B2B81871C9. README describes module ownership, navigation, simulation and reproduction. The temporary owned localhost preview remains on127.0.0.1:8767; the portable file also works offline. Refresh the existing browser page to load the new version.

End checkpoint09:10:39UTC (10:10 Europe/Paris),35minutes from08:35:39UTC. Quota observed80%→81% used, rounded service measurements, no reset consumed. A workstream-specific token count is unavailable because native goal creation refused the pre-existing blocked goal. That unrelated goal remains unchanged; the dated manual objective above is achieved. No remaining authorized implementation work; application porting requires a separate scope and GO.
