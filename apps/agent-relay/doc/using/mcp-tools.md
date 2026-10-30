# MCP tool reference

**Utility:** lists the shipped participant-facing tool contract.  
**Usage:** discover `tools/list` for the live schema; start with the [agent guide](agent-quickstart.md). Served as resource `agent-relay://docs/tools`.

## Contract

Parameters ending in `_id` are UUIDs, except `command_id` which is a configured command name. Generate request/session IDs locally; obtain resource IDs and rights from discovery. No tool creates administrative project definitions or grants itself rights.

| Tool | Required arguments | Optional arguments / effect |
| --- | --- | --- |
| list_projects | none | Authorized contexts and explicit rights |
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

Tool results include MCP content plus structuredContent. The project reply holds the appropriate configuration/session/operation/sessions/operations/messages/events/host/partial field. Paged fields contain items/nextCursor/hasMore. A tool error uses isError and readable content; a malformed protocol request uses JSON-RPC error. Check completion state and receipt, not just successful transport.

The native secondary message server has a separate one-tool contract: send_message requires text(max2000) and optional in_reply_to(UUID). It queues a scoped report/reply; queued does not yet confirm Windows import. Omitting in_reply_to creates an independent message. It offers no Windows command capability. Read its own `agent-relay://docs/native-messages` resource when connected to that endpoint.
