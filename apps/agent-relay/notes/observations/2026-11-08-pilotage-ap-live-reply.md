# Pilotage AP live reply on stable 1.1.0

**Utility:** records the live proof from Pilotage AP's own Codex chat after restoring its stable relay.  
**Usage:** use this observation to verify the deployed MCP-to-WSL path and distinguish restart recovery from the earlier isolated tests.

After the workstation restarted at 15:59 Europe/Paris on 08/11/2026, the dedicated stable host was absent and MCP returned `relay_timeout`. The stable named registration and its data were intact. The sign-in startup preference was disabled for the 1.1.0 installation. One Agent Relay sign-in entry still pointed to the older `v1-qualification` scope. The recovery started the exact stable host with its existing data and scoped credentials. The startup configuration was read for diagnosis and preserved.

Pilotage AP used `agent_relay_personal_assistant` from its own chat. It observed version and broker `1.1.0`, `clientRefreshRequired=false`, the active project and the matching MCP guide SHA-256. The deployment receipt advanced to `confirmed`.

One new read-only Codex WSL request ran `pwd` and `ls -1`. Both returned exit code 0, the working directory matched the configured workspace, and `app-symfony` appeared in the listing. The operation reached `completed` in 13.725 s. The native trace, stored operation and inbox agree on the same reply and session. Pilotage AP closed the idle qualification session and received the notification that it could resume authorized work.

| Identifier | Value |
| --- | --- |
| Session | `4a353516-003b-42ca-8841-8e22d8f8659a` |
| Request | `19c12f7b-8313-4b94-ace9-e202db57565f` |
| Correlated inbox message | `c451dac4-ac49-480c-afbc-432f3c5afae5` |

The private recovery, chat confirmation, native trace and stored reply are retained under `local/apps/agent-relay/evidence/2026-11-08-pilotage-ap/`. The SHA-256 of its checksum manifest is `6c00341074243cd55425ab13da8cef7742cf92436d794b2582d3085034079164`. The [deployment observation](2026-11-07-stable-deployment.md) records the earlier cutover and initial stale client.
