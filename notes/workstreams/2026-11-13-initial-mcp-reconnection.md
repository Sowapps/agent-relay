# Initial MCP reconnection qualification

**Utility:** retains the live preconditions, operator corrections and pending client-lifecycle action.  
**Usage:** read before asking another MCP-settings question or touching registration; no U1 implementation or stable deployment has occurred.

## Scope and progress

Operator GO: establish/qualify initial reconnection of the sole MCP without restarting Codex, preserving existing data and permissions, then state whether U1 may start. Branch codex/initial-mcp-reconnection from main 0cce427. First retained time 13/11/2026 11:47:13 Europe/Paris. The unfinished rollout objective still reports its blocked historical counters; they do not measure this continuation.

Baseline in local/apps/agent-relay/qualification/initial-mcp-reconnection-2026-11-13/: before.json has registration hash, nine logical-table count/digests and nine protected-file hashes through existing preservation helpers. mcp-before.json retains the live status. Do not export these private raw artifacts. Stable get_connection_status returned 1.1.1, broker 1.1.0, clientRefreshRequired=false. The dev connection returned relay_timeout BEFORE any action. These are baseline facts, not proof of reconnect or U1 readiness.

AP's actual chat was active when read. No native/model call, task replay, live facade stop or configuration edit has been made. Do not infer idle from historical snapshots. Legitimate concurrent AP work may alter business tables; changes cannot automatically be blamed on this qualification or hidden by claiming byte-identical preservation.

## Operator/UI facts to reuse

The operator's screenshot shows Plugins > MCP with four server rows: agent_relay, agent_relay_personal_assistant, node_repl and phpstorm. Each has an activation switch and a settings gear; no reconnect/restart button is visible on THIS LIST. The contents of the gear page have not been observed in this run. Do not ask again whether the list exists or invent an action behind the gear. The activation switch has NOT been exercised; its actual reconnection behavior is unqualified.

Operator correction: « Il ne devrait pas y avoir un MCP personal assistant, il n'est pas dédié à un agent ». Preserve the requirement of one shared stable relay for project agents; development belongs only to this development context. A registration label is not a server instance, but the current naming/configuration is still misleading and must not become the accepted target merely because it works.

Read-only registration mapping established: shared config has agent_relay pointing to the qualification/dev scope, and agent_relay_personal_assistant pointing to the stable installation. The workspace also has an agent_relay override pointing to qualification/dev. Exact paths/binding identities are retained only in the private baseline. No registration, credential, grant or data was edited. The shared development registration conflicts with the required stable/development separation.

The earlier request to identify the visible reconnect control has been answered by this screenshot. Do not repeat it. The switch experiment was suspended after the operator corrected the target naming/scope. Correcting the mapping cannot silently widen a binding's project rights or delete a project/credential; distinguish connection registration from authorization.

## Additional recorded instructions, no review invoked

The operator supplied the TR rule and explicitly said not to run a review. It is now recorded in doc/developing/technical-review.md and linked from AGENTS/development index. TR alone permits analysis/proposals, no implementation/commit/push/deployment/data changes. This current reconnection GO remains distinct. No TR review has been started.

Operator also required learning to be saved in the correct owner before repeating questions. Shared recovery instructions now make this explicit. Current edits record these instructions and observations; they are not permission to alter global configuration beyond the scoped reconnection/migration authorization.

## Next point and limits

Before any reconnect attempt, resolve the intended stable registration mapping without changing identities/rights. Inspect the existing registration procedure and prepare an exact reversible change proposal; do not simply toggle the project-named entry and call it the finished shared target. A supported client lifecycle action is still required to make an already-loaded Codex connection use changed registration. No such action has yet been qualified in the actual client; no private IPC injection, UI takeover, app restart or plugin-maintenance side effect is allowed.

Keep U1 not-ready until both correct shared-stable selection and actual reconnect with preserved identity/rights have been demonstrated. Preserve the candidate's positive isolated cooperative-handover proof separately; it does not repair the current mapping or prove this client's reload.

## Refined next point: actual client switch qualification

The shown activation switch is a concrete existing client control. Its behavior is not assumed from its label. Request sent to the operator: only when AP is at rest, disable/re-enable the existing stable registration agent_relay_personal_assistant, without quitting Codex, and report completion. This is a bounded lifecycle test of the currently loaded stable connection, not acceptance of that name as the final shared target. The previous generic visibility question was answered by the screenshot. This request is now answered by the incident below; DO NOT repeat the switch/restart experiment.

Before action, this chat's stable broker instance is retained in private mcp-before.json. After confirmed action, call get_connection_status once through the SAME named connection and compare instance change, unchanged binding/endpoint/project IDs, current contract and readiness; read scoped list_projects to compare actual permissions. Inspect effective config for unchanged unrelated entries and enabled state. Credential/project files must remain unchanged. If live business-table digests differ from the active baseline, report the boundary and natural AP activity rather than fabricating preservation or rolling data back. Never replay work to qualify reconnect.

If the same old instance survives, distinguish an enable flag change from actual reconnect. If Transport closed/unavailable occurs, record it and do not close another process, restart Codex or blindly repeat toggles. The authorized qualification can produce a negative result. Registration renaming and development isolation are prepared as required migration corrections, not silently applied while AP is active.

The current stable helper hardcodes SERVER=agent_relay_personal_assistant. A future registration correction must update that procedure owner as well as the shared/workspace mapping; renaming the settings alone would break later maintenance. register-codex.py already owns configuration CAS and single-table preservation. Reuse/refactor that owner for any authorized remapping; do not introduce a second text-rewrite policy. These observations are retained now so they are not rediscovered after compaction.

## Actual client incident and failed qualification

Operator report: after the toggle, a Restart button appeared; clicking it caused Codex to alternate displaying and error repeatedly. The operator clarified that it recovered ONLY after completely quitting and reopening Codex. Therefore this run does NOT qualify a no-Codex-restart reconnection. The button's appearance is now known; do not ask the operator whether such a button exists or repeat it as a presumed safe server-only action.

Read-only desktop logs around 13/11/2026 11:56:08–11:56:30 Europe/Paris show the local AppServerConnection entering restart_requested/restarting and repeated manager errors, including Hosted AppServerManager identity is not ready. The scoped pre-exit excerpt contains 26 transitions into restarting, 26 into error and 51 occurrences of that identity-not-ready message. This establishes a local Codex backend restart/init failure, not a successful isolated MCP reconnect. Warnings from other components were wrapped in connection-failed messages; they do not establish that those components caused the loop. Do not uninstall/edit unrelated plugins, change other projects or claim the root software defect is proven.

Retained private incident artifacts: pre-exit-client-incident.txt, scoped Codex warnings/timeline, minimized desktop-incident-excerpts.json and mcp-after-client-restart.json in the same private baseline directory. No private app source, unrelated workspace logs, tokens or raw user data are published. The post-restart broker instance differs from the baseline; that proves a new connection AFTER the manual full-client restart only. Binding, endpoint, project IDs and relay version 1.1.1 remain the same, clientRefreshRequired=false, and a scoped list_projects call succeeds.

Preservation check: all nine logical tables are unchanged, all nine protected files (including grants/credentials/runtime metadata) are unchanged, same binding and data scope. Global Codex configuration bytes changed through the user's settings action; the complete pre-action configuration was not retained, so unrelated effective settings equality cannot be independently claimed. No agent-side registration, data, permission or installed runtime edit was performed. after-preservation.json and preservation-result.json retain the exact checks. Do not attribute the post-full-restart health to the failed Restart action.

## Decision and safe next action

U1 is NOT ready under G0: the actual initial no-client-restart path failed and required a full manual restart to recover. The positive isolated future-handover proof remains valid in its separate scope. The shared stable/dev mapping defect is unchanged and separately recorded; do not silently rename connections while the client lifecycle is unqualified.

Stop live reconnection experiments. Retain a concise client incident report and identify whether a supported client-only reconnect can avoid the backend-wide restart; if none is exposed, bring that boundary back as an explicit dependency instead of asking for another identical retry. No app-source modification, global workaround, new listener, plugin trigger or U1 implementation is authorized by this qualification. TR rules have been recorded; no TR review was invoked.

## Delivery checkpoint

The [sanitized result](2026-11-13-initial-reconnect-result.json) preserves the failed qualification and successful data/permission checks without private identities/logs. User-side screenshot and incident replies supersede earlier pending questions; no reply or switch repetition is required. Current shared/dev registrations have not been changed by the agent.

Record the TR procedure, updated AGENTS/indexes, incident/issue and qualification result on this scoped branch; commit/push/merge under the explicit reconnection workstream's delivery authorization. This is not delivery authorized by a TR invocation: no TR occurred. No executable/runtime artifact, raw Codex logs, config backup, release or stable deployment is included. Verify only changed document references, JSON and whitespace. The overall deployment goal remains unfinished; do not mark it complete after a negative client qualification.

## Resume after the unrelated screenshot was withdrawn

The operator withdrew the Tailscale/Vigie Windows request as sent to the wrong agent. Only thread inventory was read; no message was sent to Vigie Windows and no installation/UI work was done. « Reprend ton sujet » resumes this reconnection work, not that withdrawn request.

The next concrete correction is prepared in apps/agent-relay/notes/proposals/2026-11-13-shared-stable-registration.md as IR-01 — Prepare and qualify shared stable registration. It proposes owner-aligned tooling and migration on copied settings, with exact identity/grant preservation, idempotence and rollback. Implementation awaits explicit GO; live settings, Codex lifecycle and stable deployment remain excluded. Do not conflate routing correctness with a fix for AR-CLIENT-09 or restart earlier diagnostics. This proposal/index/checkpoint are saved locally pending the operator's decision; the last delivered incident commit is 024c123.

Subsequent update: the operator's « GO ! » explicitly approved IR-01 implementation/qualification on copies. That work is now complete in the [IR-01 checkpoint](2026-11-13-shared-stable-registration.md); do not ask again for its implementation GO. Actual settings and client lifecycle remain unchanged/unqualified and need a separately concrete activation scope. This supersedes the earlier awaiting-GO statement only for the copied-settings lot.
