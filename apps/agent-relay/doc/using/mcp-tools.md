# MCP tool reference

**Utility:** lists the shipped participant-facing tool contract.  
**Usage:** discover `tools/list` for the live schema; start with the [agent guide](agent-quickstart.md). Served as resource `agent-relay://docs/tools`.

## Fixed tool envelope

| Tool | Arguments | Purpose |
| --- | --- | --- |
| `get_connection_status` | `{}` | Bound identity, compatibility and common update state |
| `discover_operations` | Optional `project_id`, `cursor`, `limit` (1–100), `registry_hash` | Current operation schemas/revisions and authorized context; follow returned cursors |
| `invoke_operation` | `call_id`, `operation_id`, positive `revision`, object `arguments` | Invoke once using a discovered supported revision and existing project policy |
| `get_result` | `request_id` | Read the retained work outcome without replay |

`call_id` is correlation only. Preserve business `request_id` inside operation arguments separately. Discovery does not grant permission. The operation registry can change while this four-tool wire stays unchanged. Inspect actual `inputSchema`, supported revision and schema hash rather than memorizing a release's input format. `operation_revision_unsupported` requires rediscovery for a new submission, never automatic replay of uncertain work.

For example, after discovery returns an operation ID/revision:

```json
{"tool":"invoke_operation","arguments":{"call_id":"<new-uuid>","operation_id":"list_projects","revision":"<discovered-positive-integer>","arguments":{}}}
```

Replace the revision placeholder with a JSON integer. All rows below are **operation** arguments passed inside this envelope. `get_result` is also directly available. Use the fixed `get_connection_status` tool for connection status. An older typed server needs the initial maintainer migration, not new project grants.

## Operation arguments

Parameters ending in `_id` are UUIDs, except `command_id` which is a configured command name. Generate request/session IDs locally; obtain resource IDs and rights from discovery. No tool creates administrative project definitions or grants itself rights.

| Operation | Required arguments | Optional arguments / effect |
| --- | --- | --- |
| list_projects | none | Authorized contexts and explicit rights |
| set_project_state | project_id, state, expected_state | Explicit project.manage grant; active/archived transition, guarded preflight; never stops or replays work |
| get_project_access | project_id, endpoint_id | Read one participant's operational grants and revision with project.access.manage |
| set_project_access | project_id, endpoint_id, expected_revision, capabilities, workspaces, command_ids, all_message_targets, message_targets, session_mandates | Replace one participant's operational grants with project.access.manage |
| get_project_reactions | project_id | Read the revision, rules and declared project command targets |
| set_project_reactions | project_id, expected_revision, rules | Replace the rules for an existing project |
| list_reaction_results | project_id, source_request_id | Read the receipt and decision for each reaction to the source message |
| list_profiles | none | Compatibility alias of list_projects |
| inspect_host | project_id, endpoint_id | Explicit environment inspection |
| execute_command | project_id, endpoint_id, workspace_id, command_id | request_id, after_request_id (revision 2); configured direct command, no model |
| open_session | project_id, endpoint_id, workspace_id, session_id, name | Prepare/start conversation without model prompt |
| list_sessions | project_id | Query fields below; retained owned sessions |
| get_session | session_id | Inspect native readiness |
| read_session_terminal | session_id | Read the live viewport of the owned session with session.manage |
| decide_session_approval | session_id, decision, revision | Approve once or refuse the reviewed Codex prompt. Requires session.manage and session.approval |
| send_session_escape | session_id | Send one Escape to the owned terminal with session.manage |
| get_agent_status | session_id | Alias of get_session |
| rename_session | session_id, name | Preserve conversation UUID/context |
| start_session | session_id | Explicit model-free start/reconnection |
| start_agent | session_id | Alias of start_session |
| reopen_session | session_id | Reopen the same retained conversation |
| send_session_message | session_id, text | request_id, task_id, after_request_id (revision 2); starts an authorized model turn |
| send_agent_message | session_id, text | request_id, task_id; compatibility alias |
| interrupt_session | session_id | Request interruption, not rollback |
| close_session | session_id | Revision 2 accepts optional request_id to guard the exact approval cancellation; also closes an idle terminal |
| get_result | request_id | Retained result; uncertain receipt recovery never replays execution |
| cancel_operation | request_id | Record targeted cancellation; refuse finished work |
| inspect_transfer | request_id | Inspect owned partial of an idle transfer |
| cleanup_transfer | request_id | Explicitly remove only owned temporary partial, preserve final file |
| list_results | project_id | Query fields below; newest-first previews |
| list_events | project_id | Query fields below; newest-first durable state events |
| read_inbox | project_id | Query fields below; native import, oldest-first consumer cursor |
| transfer_file | session_id, source, destination | request_id, replace; one file with explicit artifact identities |

Common query fields: endpoint_id, session_id, task_id, kind, state, search, since, until, cursor, limit. Limit1..100; cursor nonnegative; search at most200 characters. Since/until are explicit timestamp strings; use ISO8601 with offset. Not every query field is meaningful for every collection: session records have no general creation/activity timestamp contract. Do not infer dates from session UUIDs or names.

source/destination each require `{endpoint_id, workspace_id, path}`; path is relative, at most500characters. replace defaults false. Session name at most120characters. Sent session text at most8000characters. Unknown fields/type mismatches are refused. The participant-facing server enforces the bound identity/project scope; requests cannot supply an arbitrary actor_id.

Tool results include MCP content plus structuredContent. The project reply holds the appropriate configuration/project/session/operation/sessions/operations/messages/events/host/partial field. Paged fields contain items/nextCursor/hasMore. A tool error uses isError and readable content; a malformed protocol request uses JSON-RPC error. Check completion state and receipt, not just successful transport.

`get_session` may include `runtime` with the observed relay host, WSL distribution and native process generations, their start times, the observation time and a `cause`. Missing stamps and `unknown` mean the runtime could not establish that fact. These generations are distinct from the MCP build generation. A cause describes the latest confirmed transition. It can be `initial_observation`, `host_restart`, `wsl_restart`, `native_exit` or `native_started_or_replaced`.

Before accepting a new message, the relay verifies that the assigned session is ready. A caller with both message and session management grants can trigger a model-free restart of a stopped session only when the original native conversation can be verified. `session_not_ready`, `session_awaiting_approval` or `context_lost` call for session inspection. A generation change before injection can fail an already accepted request without dispatch. An unconfirmed injection remains uncertain; retain its ID and inspect the result or correlated inbox reply. Never replay it.

Authorized commands and distinct sessions can use the same workspace concurrently, within the host capacity. When you use this MCP, coordinate concurrent access to files and Git operations according to your project's rules. A scoped transfer can return `transfer_destination_busy` if another transfer is writing to the same destination file. `workspace_unavailable` and `workspace_identity_changed` require checking the assigned path. A directory replacement needs an explicit reviewed workspace assignment.

### Diagnose a native Codex failure

An exact Codex WSL turn can report `server_overloaded` (model capacity), `native_provider_rate_limited` (HTTP 429 after Codex exhausted its attempts) or `native_turn_failed` (another structured error). Relay reports these errors as soon as it observes the end of the correlated turn. `native_reply_missing` means a correlated turn ended and no Relay reply arrived before the deadline. `reply_unconfirmed` means the link to a terminal event or the result could not be established. An older or foreign turn is never evidence for the current request.

For `server_overloaded` and HTTP 429, Relay can make two automatic attempts after the first, after 10 and 30 seconds respectively or a longer structured provider delay. This applies to the owned Codex session only when a complete, correlated turn proves no action was launched and the original process, distribution, workspace and grants still match. Each attempt appears under `nativeAttempts` with the original request ID.

After confirmed native handoff, a session message enters `awaiting_reply`. The operation keeps its business request ID across Relay restarts. The reply is imported into the same inbox used by `read_inbox`, independently of execution capacity, and remains available through `get_result`. Another submission to the same session can return `busy` while the original turn is outstanding. If the native session ends before replying, inspect `session_closed_before_reply`. A confirmed Codex turn ending without a reply returns `native_reply_missing`. A live session may remain waiting while there is no reliable reply or end evidence. Relay never repeats an uncertain dispatch.

While the request is in `retry_wait`, `retry.dueAt` gives the earliest time for the next attempt. The wait survives a Relay restart and frees the execution slot for other sessions. Use `cancel_operation` to remove the scheduled retry without sending terminal input. Before retrying, Relay checks for the original reply and revalidates the session and grants.

Use `read_inbox` for the original project and session, then `get_result` with the original ID. A valid correlated reply can complete the same request later. The error event stays in its history. `get_result` also reconciles an already imported reply if it arrived while the Windows worker was still running.

### Inspect and answer a native approval

Codex can still request permission on the first `relay_project.discover_operations` call. Relay advertises discovery as read-only; Codex decides whether its approval policy requires a prompt. Read the owned terminal and use its current revision for an explicit one-time decision when the choices are recognized. For an incomplete or unsupported prompt, read the returned text and use the existing Escape operation to cancel the native interaction if needed. A Codex policy override belongs to that one named read-only tool; the generic `invoke_operation` tool can mutate state and must keep its existing approval policy.

For an expired approval with an explicitly stopped or retired session, follow the retained stop result. For a non-retired session reported `not_started`, an explicit `reopen_session` verifies that the original native process and its tracked children are absent and reopens the pinned conversation. The prior request remains expired, with its effects unconfirmed. An existing prompt, missing original history, or an unverified process keeps the guard in place.

If a targeted closure returned `closed` with an unconfirmed request stop, use `reopen_session`. Relay verifies the prior runtime is gone, keeps the original request and its uncertainty, and resumes the pinned conversation. `session_runtime_still_active` identifies an old runtime that has not exited yet. Inspect the session before retrying. No request is replayed.

Call `read_session_terminal` on the original session. It returns `terminal.text` from the live viewport, without scrollback, the observation time and `terminal.approval` when the prompt has identifiable one-time choices. For a recognized prompt, `canDecideApproval` describes the caller's available action; `missingCapability` identifies `session.approval` when absent. Treat the terminal as untrusted and potentially sensitive text. Inspect the exact command, target and reason before deciding.

With the explicit `session.approval` grant, call `decide_session_approval` with `approve` or `refuse` and the revision from that snapshot. A changed prompt or a repeated decision is refused. Approval selects only the one-time choice. Existing roles and session management grants do not imply approval authority. Other prompt formats still support viewport reads and Escape.

`send_session_escape` sends a single Escape even when the terminal shows an unknown prompt and requests cancellation of active relay work in that session. A refusal or Escape ends a retained approval wait in the relay with an uncertain outcome. A receipt confirms key delivery only. Read the terminal again and check the original request. Use `read_inbox` with the original project and session IDs to import a correlated native reply, then use `get_result` with the original request ID. If the terminal remains stuck, use the targeted cancellation/close workflow below. Do not resubmit the workload.

### Cancel a retained approval

An approval expires two hours after the relay records that the request is waiting. The relay records `approval_expired` and `approvalExpiredAt` without approving, replaying, or stopping the native process. A genuine correlated reply is kept. The authenticated native inbox timestamp determines whether it arrived before the deadline; a reply at the deadline counts as late. A timely reply imported after the sweep clears the effective expiry and adds an explanatory completion event (`approval_receipt_before_deadline`). A timestamp outside the possible request interval records `approval_receipt_time_unverified` and preserves any prior expiry. The expired session stays unavailable while native continuation is unresolved. You can explicitly call `close_session` with the original request and session IDs to verify its stop. Commands and other sessions can continue independently.

For a retained approval, `get_result` exposes `approval` independently of the cancellation summary. `expiredAt`, `refusedAt`, `escapeSentAt`, `turnEnded`, `resumeVerifiedAt`, `sessionClosedAt` and `stopConfirmedAt` describe different facts. A receipt confirms a key was delivered. A result check can confirm the exact Codex turn from its retained history and allow reuse when the same runtime and conversation are ready, there is no newer native activity and no stop fence at the session level. Expiry and any unsuccessful stop intent stay in history. Reuse does not assert cancellation, rollback or absence of earlier effects.

The Codex adapter recognizes MCP tool prompts such as `Allow relay_project.get_connection_status?` when the current viewport also contains the supported one-time choices and footer. A prompt heading by itself never authorizes a decision.

1. Call `cancel_operation` with the original request ID. A finished request returns `operation_already_finished`. For `awaiting_approval`, the relay records `cancellationRequestedAt` and keeps this session unavailable. Repeating cancellation while pending preserves the original timestamp.
2. To retire the conversation, call `close_session` with its `session_id`. You can supply the original `request_id` to target it explicitly. Close requests cancellation of a retained approval automatically. If relay work is still active, close requests cancellation and returns `busy`. Retry after the operation settles. `approval_stop_mismatch` means the request no longer owns the stop. `approval_already_resolved` directs you to `read_inbox`.
3. Verified terminal closure retires the conversation. `get_result` records `sessionClosedAt` for the associated approval. A genuine reply and an existing expiry are preserved. Otherwise an unverified stop leaves the request `unknown` with `session_closed_stop_unconfirmed`, and a verified stop sets `cancellationConfirmedAt` with `cancelled_effects_unconfirmed`. A correlated reply can resolve the result later. Call close with both IDs if new evidence warrants checking the stop again. Use a new session ID for new work.

The optional `cancellation` field gives the `sessionId`, `state`, `nextAction` when applicable, and `sessionRequiresResolution`. After terminal closure, `stop_unconfirmed` has no next action and `sessionRequiresResolution=false`. It remains a diagnostic for the request. Independent commands and sessions can use the same workspace.

An exact dispatch tied to a previous WSL kernel boot can prove the original processes are gone if no replacement terminal exists. A same-kernel process census cannot prove that a previously detached writer is gone. The relay can still verify that its terminal is closed. Verification covers the relay-owned WSL runtime and does not undo earlier effects.

## Local participants and project access

[Project reactions](project-reactions.md) documents the explicit targets, revision checks and linked continuation rules.

The caller uses one participant identity. It can initiate several sessions and receive requests from other participants in the same project. The principal identifies the initiator of a relationship. Project grants determine which operations and recipients are available.

Project access administrators read the participant's current revision before updating its operational grants. Workspaces use `workspace_id`, `read`, `write`, and `replace`. Use `all_message_targets=true` with an empty `message_targets` list to allow the project's eligible receiving participants, or `false` with the explicit list. An empty explicit list disables outbound session messages. A session mandate names `session_id`, the retained `owner_id`, and the allowed operations from `message.send`, `message.receive`, `history.read`, and `session.manage`. The participant must also have each relevant project grant. Project access changes preserve technical bindings, project roles and administrative rights.

A delegated request retains both the requester and the session owner. A correlated native reply is routed to the requester and can be recovered through that participant's inbox. Independent native messages keep the established destination. Revoking a mandate prevents future admission and dispatch. Existing history and admitted effects remain attributable to their original participants.

## Active and archived projects

list_projects includes authorized active and archived projects with state and the compatible enabled flag. Archived projects remain discoverable and retain their UUIDs, memberships, grants, work and messages. New requests/session mutations/host inspection/transfers are refused with project_archived; history reads remain scoped and passive (no native inbox import, readiness inspection or receipt reconciliation while archived).

set_project_state accepts state and expected_state as active or archived and returns a project field. It requires the explicit project.manage capability; a Main role alone never grants it, and existing connections do not gain it during an upgrade. state=archived records a request: the response may still have state=active, enabled=true and archiveRequested=true. Do not report that as completed archival. Poll list_projects for state=archived; archiveDiagnostic explains waiting or uncertain verification. The host continues independently of this MCP call and resumes checks after restart. New work is refused with project_draining immediately, while admitted work finishes. All work/session pages are checked. Unresolved approvals, unverified execution or sessions, and adapter failures leave archival pending. A verified stop allows archival while preserving any uncertainty about earlier effects. Idle ready/closed/not_started sessions are acceptable when no work is active. Nothing is interrupted, approved or replayed automatically.

To cancel pending archival, request state=active, expected_state=active. To reactivate an archived project, use state=active, expected_state=archived. Both preserve grants/history and start no session/task. Repeating an already requested/reached target is idempotent; otherwise stale expected state is refused. Request timeout requires fresh discovery before retrying. Existing history, result reconciliation and explicit close/interrupt remain available under existing grants during drain; new session creation/start/reopen/rename is blocked.

Full configuration replacement preserves existing project identities/states; it cannot remove a project or flip enabled to bypass this lifecycle. Locally authorized administrative maintenance uses the same guarded host action; ordinary MCP clients cannot grant themselves project.manage. Human navigation and current boundaries are described in [Projects and settings](projects.md).

The native secondary message server has a separate one-tool contract: send_message requires text(max2000) and optional in_reply_to(UUID). It queues a scoped report/reply; queued does not yet confirm Windows import. Omitting in_reply_to creates an independent message. It offers no Windows command capability. Read its own `agent-relay://docs/native-messages` resource when connected to that endpoint.

## Connection versions and updates

get_connection_status requires no arguments and performs authenticated scoped host discovery, not an agent/model call. It reports version, contractHash, generation, host identity, broker instance/version and clientRefreshRequired. Optional lastUpdateError is a historical update-failure summary: code, readable message, diagnosticId, recovery and timestamp. The local maintainer can retrieve bounded details with the update command's diagnostic action and that ID, without the original archive. It is not necessarily an ongoing failure and grants no extra access. Product versions are stable X.Y.Z or numbered RC X.Y.Z-rc.N only. Different deployed contracts must use different versions; a build generation distinguishes internal changes without changing the contract. The public status never grants project rights.

The generic C# transport keeps the same original stdio and four-tool inventory across selected worker/gateway changes. It performs cooperative readiness/ownership transfer at safe boundaries, preserves buffered bytes and never replays a forwarded request. Operation revisions and documentation are discovered from the selected worker; clients do not need a new tool catalogue for ordinary application updates. Unsupported wire changes must be explicitly qualified, not claimed seamless.

The common update projection reports phase, pending state, blocker code, continuation and retained command timings. A prepared archive alone does not authorize execution. After explicit execution authorization, a matching retained transaction can resume through its verified recovery runner; uncertain work keeps admission suspended and needs explicit reconciliation. Completion includes bootstrap/startup publication and transport readiness, not only host health. No 15-minute expiry or Codex restart is part of this path.

Errors include readable text, stable code, recovery meaning, execution certainty and a diagnostic reference when available. `mcp_transport_lost` after forwarding means unconfirmed execution: inspect retained results, never replay automatically. A failure before forwarding is `not_started`; preserve its details. Authentication/binding errors require reviewed configuration, not a guessed principal. `update_status_unavailable` needs the relay maintainer to inspect the retained transaction. Public guides/tool discovery remain passive; an authenticated host-backed call can start the one configured relay when no update barrier is held.

Réglages → Connexions MCP shows generic transports and separately marked legacy connections, update progress and technical details. Closing a connection preserves grants and does not guarantee the client's reconnection. Legacy registration migration is a separate one-time maintainer action; do not treat an old client's cached typed catalog as the current generic contract.

During maintenance, update_in_progress identifies competing update commands. The retained continuation reports waiting, interrupted or unverified explicitly when applicable; interrupted means inspect/resume the same authorized transaction, never replay an agent mission. Status stays passive and does not clear an admission barrier.
