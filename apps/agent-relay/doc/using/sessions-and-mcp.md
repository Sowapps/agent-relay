# Sessions and MCP workflows

**Utility:** describes project-scoped agent use, retained conversations and file operations.  
**Usage:** connect the Windows MCP agent to an explicitly configured participant while Agent Relay is running.

## Configure once

In Réglages → Outils d’administration et de diagnostic → Modifier les projets, declare a Relay project / Projet relayé, Main / Principal, destination and existing Workspace / Espace de travail. Select rights deliberately: native local work, direct command identifiers, host inspection and transfer source/destination rights are distinct. Existing legacy profiles and records grant no project authority.
Console technique → Paramètres can prepare this project's MCP connection. Its protected reference is stored in the runtime pointer; no secret belongs in command arguments or workspace files. export-mcp.ps1 generates the stable Python stdio launcher and final client fragment. The launcher does not start the tray host.
An enabled agent_relay settings entry proves registration only. Confirm that the current conversation discovers and invokes its tools. Updating the pointer does not reload an already connected facade. Use only the installed client's supported reconnect lifecycle; do not invent a restart button or kill an internal client process.

The [agent quickstart](agent-quickstart.md) is also served as MCP resource `agent-relay://docs/quickstart`; [tool arguments](mcp-tools.md) are served as `agent-relay://docs/tools`. For preparation, project administration and human guidance, read [bootstrap](bootstrap.md), resource `agent-relay://docs/bootstrap`; its reviewable request example is resource `agent-relay://docs/new-project-request`. These resources ship with the rebuilt runtime. Existing connected facades may require the client’s normal reconnect to advertise newly added resources. Older native message endpoints still obey their discovered one-tool schema.

## Discover and select context

list_projects returns only this connection's authorized projects, endpoints, workspaces and its explicit grants. list_profiles is a compatibility alias for this discovery, with the same scope. Stable UUIDs identify resources; names can change. A foreign project, forged identity or revoked credential is refused by the server.
execute_command requires project_id, endpoint_id, workspace_id and command_id. It runs that configured program/argument array directly without an AI turn. Results identify the actual working directory and effective Linux user. inspect_host reads authorized user/platform/tool versions and selected service presence, without dumping secrets or asking an agent.

## Named conversations

| Tools | Meaning |
| --- | --- |
| open_session | Supply project_id, endpoint_id, workspace_id, session_id and name; prepare/start without a model prompt |
| list_sessions, get_session | Page retained sessions or inspect actual readiness |
| rename_session | Change the readable name, retaining UUID and context |
| start_session, reopen_session | Start/reopen the retained native conversation explicitly |
| send_session_message | Send exact bounded text to this owned conversation; retain request_id |
| interrupt_session, close_session | Request interruption or close an idle terminal; effects may remain uncertain |
| get_agent_status, start_agent, send_agent_message | Compatibility aliases requiring an explicit session_id |

A session's real workspace is separate from its private runtime/configuration/history. Reopen uses a pinned native conversation UUID; missing identity is context-lost, never an implicit latest conversation. Linux distribution/user/native executable belong to the technical adapter binding.
Separate sessions can run within maximumConcurrency (default4,1..64). Busy means refused, not queued. Shared workspace writes are serialized by the relay unless explicitly allowed. Avoid concurrent manual terminal input during relay work; tmux access remains available for human takeover and native approvals.
Check process, native authentication, message MCP and approval state separately. Authentication required means the native account needs login; a broken credential reference is a different failure. After changing Claude account credentials, reopen the idle relay session to refresh its launch environment. Do not retry a task automatically after an uncertain failure.

## Follow work and messages

get_result reads the durable operation. Pending acknowledgment does not prove delivery or completion. State can be pending, running, awaiting_approval, completed, failed or unknown. cancel_operation records a targeted cancellation request; it does not roll back changes. A matching late receipt can confirm an unknown result without execution replay.
list_results returns bounded previews, newest first; get_result contains full details within frame bounds. list_events follows durable project/task/attempt transitions. Filter by project, endpoint, session, task, kind, state, content and date. Pass nextCursor to read older pages; updates to an existing row require reading its ID again. Search applies to content/labels, not all JSON metadata.
read_inbox imports bounded native pages and returns messages addressed to this participant, oldest first after its cursor. Preserve nextCursor even when caught up. Per-session warnings do not hide messages already retained in Windows. Each consumer owns its cursor.
Independent messages have no inReplyTo; final correlated replies carry the operation UUID. The native policy permits useful progress, clarification and blocker messages during ordinary work. A model is not guaranteed to emit an independent message on every task. WSL's MCP advertises only send_message; it cannot request Windows commands and no received text is executed.

## Transfer files

transfer_file requires session_id and explicit source/destination objects, each with endpoint_id, workspace_id and a relative path. Replacement requires replace=true and its separate permission. Example: source is the Windows participant's docs/input.txt; destination is the selected agent workspace's incoming/input.txt. Neither path identifies an arbitrary disk location.
The operation carries project/participant/artifact identities and checks source read, destination write and optional replacement rights. Bytes travel through the WSL filesystem adapter, not JSON/base64 messages. Read get_result for byte progress, integrity receipt and confirmed destination. See the [transfer reference](../developing/file-transfer.md) for the actual UNC/9P mechanism, limits and interruption behavior.
inspect_transfer reads the owned partial of a retained transfer; cleanup_transfer explicitly removes that temporary only, while preserving the final destination. Both require the authorized owned session to be idle. Cleanup does not retry, resume or confirm an unknown transfer.

## Monitoring and acceptance

The app observes the same project operations as MCP, with readable errors, received-message presentation, filters, retained pages, copy and raw details. Settings expose technical profiles and unassigned legacy history separately. It does not substitute for real MCP availability in the caller.
One-time project configuration, account authentication, current-client connection reload and visual/OS lifecycle checks are separate acceptance gates. The dated V1 acceptance record identifies which are actually demonstrated.
