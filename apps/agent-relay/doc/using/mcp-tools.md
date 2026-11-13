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

Replace the revision placeholder with a JSON integer. All rows below are **operation** arguments passed inside this envelope; `get_connection_status` and `get_result` are also directly available as above. An older typed server needs the initial maintainer migration, not new project grants.

## Operation arguments

Parameters ending in `_id` are UUIDs, except `command_id` which is a configured command name. Generate request/session IDs locally; obtain resource IDs and rights from discovery. No tool creates administrative project definitions or grants itself rights.

| Operation | Required arguments | Optional arguments / effect |
| --- | --- | --- |
| get_connection_status | none | Version, contract hash, build generation, current scoped host and client-refresh requirement; no model |
| list_projects | none | Authorized contexts and explicit rights |
| set_project_state | project_id, state, expected_state | Explicit project.manage grant; active/archived transition, guarded preflight; never stops or replays work |
| list_profiles | none | Compatibility alias of list_projects |
| inspect_host | project_id, endpoint_id | Explicit environment inspection |
| execute_command | project_id, endpoint_id, workspace_id, command_id | request_id; configured direct command, no model |
| open_session | project_id, endpoint_id, workspace_id, session_id, name | Prepare/start conversation without model prompt |
| list_sessions | project_id | Query fields below; retained owned sessions |
| get_session | session_id | Inspect native readiness |
| get_agent_status | session_id | Alias of get_session |
| rename_session | session_id, name | Preserve conversation UUID/context |
| start_session | session_id | Explicit model-free start/reconnection |
| start_agent | session_id | Alias of start_session |
| reopen_session | session_id | Reopen the same retained conversation |
| send_session_message | session_id, text | request_id, task_id; starts an authorized model turn |
| send_agent_message | session_id, text | request_id, task_id; compatibility alias |
| interrupt_session | session_id | Request interruption, not rollback |
| close_session | session_id | Close idle terminal, retain identity/history |
| get_result | request_id | Retained result; uncertain receipt recovery never replays execution |
| cancel_operation | request_id | Targeted cancellation request, not rollback |
| inspect_transfer | request_id | Inspect owned partial of an idle transfer |
| cleanup_transfer | request_id | Explicitly remove only owned temporary partial, preserve final file |
| list_results | project_id | Query fields below; newest-first previews |
| list_events | project_id | Query fields below; newest-first durable state events |
| read_inbox | project_id | Query fields below; native import, oldest-first consumer cursor |
| transfer_file | session_id, source, destination | request_id, replace; one file with explicit artifact identities |

Common query fields: endpoint_id, session_id, task_id, kind, state, search, since, until, cursor, limit. Limit1..100; cursor nonnegative; search at most200 characters. Since/until are explicit timestamp strings; use ISO8601 with offset. Not every query field is meaningful for every collection: session records have no general creation/activity timestamp contract. Do not infer dates from session UUIDs or names.

source/destination each require `{endpoint_id, workspace_id, path}`; path is relative, at most500characters. replace defaults false. Session name at most120characters. Sent session text at most8000characters. Unknown fields/type mismatches are refused. The participant-facing server enforces the bound identity/project scope; requests cannot supply an arbitrary actor_id.

Tool results include MCP content plus structuredContent. The project reply holds the appropriate configuration/project/session/operation/sessions/operations/messages/events/host/partial field. Paged fields contain items/nextCursor/hasMore. A tool error uses isError and readable content; a malformed protocol request uses JSON-RPC error. Check completion state and receipt, not just successful transport.

## Active and archived projects

list_projects includes authorized active and archived projects with state and the compatible enabled flag. Archived projects remain discoverable and retain their UUIDs, memberships, grants, work and messages. New requests/session mutations/host inspection/transfers are refused with project_archived; history reads remain scoped and passive (no native inbox import, readiness inspection or receipt reconciliation while archived).

set_project_state accepts state and expected_state as active or archived and returns a project field. It requires the explicit project.manage capability; a Main role alone never grants it, and existing connections do not gain it during an upgrade. state=archived records a request: the response may still have state=active, enabled=true and archiveRequested=true. Do not report that as completed archival. Poll list_projects for state=archived; archiveDiagnostic explains waiting or uncertain verification. The host continues independently of this MCP call and resumes checks after restart. New work is refused with project_draining immediately, while admitted work finishes. All work/session pages are checked; approval/unknown work, unverifiable sessions or adapter failures retain the request rather than declaring success. Idle ready/closed/not_started sessions are acceptable when no work is active. Nothing is interrupted, approved or replayed automatically.

To cancel pending archival, request state=active, expected_state=active. To reactivate an archived project, use state=active, expected_state=archived. Both preserve grants/history and start no session/task. Repeating an already requested/reached target is idempotent; otherwise stale expected state is refused. Request timeout requires fresh discovery before retrying. Existing history, result reconciliation and explicit close/interrupt remain available under existing grants during drain; new session creation/start/reopen/rename is blocked.

Full configuration replacement preserves existing project identities/states; it cannot remove a project or flip enabled to bypass this lifecycle. Locally authorized administrative maintenance uses the same guarded host action; ordinary MCP clients cannot grant themselves project.manage. Human navigation and current boundaries are described in [Projects and settings](projects.md).

The native secondary message server has a separate one-tool contract: send_message requires text(max2000) and optional in_reply_to(UUID). It queues a scoped report/reply; queued does not yet confirm Windows import. Omitting in_reply_to creates an independent message. It offers no Windows command capability. Read its own `agent-relay://docs/native-messages` resource when connected to that endpoint.

## Connection versions and updates

get_connection_status requires no arguments and performs authenticated scoped host discovery, not an agent/model call. It reports version, contractHash, generation, host identity, broker instance/version and clientRefreshRequired. Optional lastUpdateError is a historical update-failure summary: code, readable message, diagnosticId, recovery and timestamp. The local maintainer can retrieve bounded details with the update command's diagnostic action and that ID, without the original archive. It is not necessarily an ongoing failure and grants no extra access. Product versions are stable X.Y.Z or numbered RC X.Y.Z-rc.N only. Different deployed contracts must use different versions; a build generation distinguishes internal changes without changing the contract. The public status never grants project rights.

The generic C# transport keeps the same original stdio and four-tool inventory across selected worker/gateway changes. It performs cooperative readiness/ownership transfer at safe boundaries, preserves buffered bytes and never replays a forwarded request. Operation revisions and documentation are discovered from the selected worker; clients do not need a new tool catalogue for ordinary application updates. Unsupported wire changes must be explicitly qualified, not claimed seamless.

The common update projection reports phase, pending state, blocker code, continuation and retained command timings. A prepared archive alone does not authorize execution. After explicit execution authorization, a matching retained transaction can resume through its verified recovery runner; uncertain work keeps admission suspended and needs explicit reconciliation. Completion includes bootstrap/startup publication and transport readiness, not only host health. No 15-minute expiry or Codex restart is part of this path.

Errors include readable text, stable code, recovery meaning, execution certainty and a diagnostic reference when available. `mcp_transport_lost` after forwarding means unconfirmed execution: inspect retained results, never replay automatically. A failure before forwarding is `not_started`; preserve its details. Authentication/binding errors require reviewed configuration, not a guessed principal. `update_status_unavailable` needs the relay maintainer to inspect the retained transaction. Public guides/tool discovery remain passive; an authenticated host-backed call can start the one configured relay when no update barrier is held.

Réglages → Connexions MCP shows generic transports and separately marked legacy connections, update progress and technical details. Closing a connection preserves grants and does not guarantee the client's reconnection. Legacy registration migration is a separate one-time maintainer action; do not treat an old client's cached typed catalog as the current generic contract.
