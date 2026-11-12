# Codex MCP refresh qualification

**Utility:** distinguishes server reconnection, catalog refresh and access to the owning Codex client.  
**Usage:** consult before diagnosing an obsolete catalog or claiming that closing a broker refreshes Codex. Use the existing complete-update procedure and `client_refresh.py` adapter; do not start a replacement backend to refresh an existing desktop chat.

## Qualified behavior

| Mechanism | Observed behavior | Qualification boundary |
| --- | --- | --- |
| `config/mcpServer/reload` | Reconnects a closed MCP server with unchanged configuration and refreshes changed tool definitions and resources. Closing Codex is unnecessary when this method is reachable. | Installed Codex app-server, isolated home and ephemeral protocol thread; no model turn. |
| `notifications/tools/list_changed` and resource notification | Tool inventory remained unchanged during the six-second observation; an explicit resource read returned current contents. | This does not prove notification handling during a subsequent model turn, which was not tested. |
| MCP child exit | Subsequent resource read returned `Transport closed`; no replacement fixture process appeared before explicit reload. | Isolated Codex backend. Earlier actual-desktop audit also observed no replacement after identity-checked legacy facade closures. |
| Agent Relay close action | Closes the admitted connection safely; does not itself request a Codex catalog reload. | Broker lifecycle qualification is distinct from client integration. |
| Owning desktop control adapter | Reports `unsupported` when its supported control socket is absent. | An isolated backend accepting reload does not make the existing desktop backend reachable. |

These are measured outcomes, not a generic statement that refresh is “not guaranteed”. Both the original and repeat qualification, exact client versions, outputs and limitations are retained in the dated qualification record. Changes to the Codex version or control interface require a scoped requalification.

## Completion rule

A healthy host, fresh diagnostic connection or changed resource alone cannot confirm an existing chat's catalog. Verify the same consuming chat's inventory, current resource, `get_connection_status` identity/contract and scoped `list_projects`. If the owning client's supported refresh cannot be reached, retain an explicit pending-client state. Never force-close Codex, replay uncertain work or silently substitute a new backend.

The `config/mcpServer/reload` method reloads configuration for loaded threads; the qualified method has no per-server selector. See the [official app-server reference](https://learn.chatgpt.com/docs/app-server) for the interface. Reachability and actual same-client results must still be measured locally.
