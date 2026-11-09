# MCP tool reference

**Utility:** lists the shipped participant-facing tool contract.  
**Usage:** discover `tools/list` for the live schema; start with the [agent guide](agent-quickstart.md). Served as resource `agent-relay://docs/tools`.

## Contract

Parameters ending in `_id` are UUIDs, except `command_id` which is a configured command name. Generate request/session IDs locally; obtain resource IDs and rights from discovery. No tool creates administrative project definitions or grants itself rights.

| Tool | Required arguments | Optional arguments / effect |
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

get_connection_status requires no arguments and performs authenticated scoped host discovery, not an agent/model call. It reports version, contractHash, generation, host identity, broker instance/version and clientRefreshRequired. Product versions are stable X.Y.Z or numbered RC X.Y.Z-rc.N only. Different deployed contracts must use different versions; a build generation distinguishes internal changes without changing the contract. The public status never grants project rights.

The retained native broker follows the selected runtime between calls and forwards raw JSON without dropping fields introduced by a newer worker. An interrupted call keeps its original outcome and is never replayed automatically. A successful tools/list confirms the catalog requested by this connection. The broker emits list-change notifications on a contract switch, whether detected while idle or during a request. Until the client lists the current tools, get_connection_status and list_projects remain available when the worker and host agree. Calls that require the current catalog return client_catalog_outdated before submission. Verify an existing chat from that same chat after an update.

Errors carry a readable message, a code, a recovery action, execution certainty and an optional diagnosticId. Unexpected details go to bounded private diagnostics without request bodies or credentials. For client_catalog_outdated, reload this connection's tools. For runtime_contract_mismatch, inspect the worker and host installation. The older connection_outdated code also requires checking connection status. For relay_migrated, reconnect to the current owned data scope. For authentication_required, review the explicit binding with operator authorization. For relay_unavailable, relay_timeout or mcp_transport_lost, inspect retained results before resubmitting. For storage_unavailable, check installation and data access. A transport error leaves the effect uncertain.

On a prepared portable installation, the first authenticated host-backed call can start the shared relay and wait for its authenticated handshake. Discovery of tools and public guides stays passive. Concurrent clients share the cold start. Maintenance blocks new startup and admission. A `relay_start_failed` response with `execution=not_started` directs agents to the [local recovery procedure](portable-release.md#recover-an-unavailable-relay). An agent without local command access should ask the Agent Relay maintainer or the user. A request sent to the host is never replayed automatically.

The Windows operator can inspect recent MCP broker connections at Réglages → Connexions MCP and request closure after the admitted call. Closing does not revoke permissions or guarantee reconnection. A Codex control adapter can request reload only through an existing reachable owning-backend socket; otherwise the update report explicitly requests client-side reload/restart. It never creates a daemon, starts a replacement backend or claims fresh-client verification proves the old conversation.
