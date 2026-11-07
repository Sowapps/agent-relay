# RC6 native WSL reply

**Utility:** records a real reply from Codex WSL and its correlation with the original MCP request.  
**Usage:** compare future round-trip results against this isolated case.

On 07/11/2026, the exact Agent Relay `1.1.0-rc.6` archive (SHA-256 `2bab25acdf7f0777f55b08a252cd7cc516083c8bc5d7bf3a1256c78adad10184`) ran with a fictional project and a dedicated workspace. An MCP client submitted one new read-only request in this successful fixture. The native transcript records `pwd`, `ls -1`, and the reply tool call with the original request UUID.

The operation reached `completed` in 14.829 s. The inbox returned exactly one reply for this request in the expected session. Its text matched the operation result, and the session subsequently reached `closed`.

| Identifier | Value |
| --- | --- |
| Session | `91e83cea-26a6-4ec1-8552-5114d195628f` |
| Request and `inReplyTo` | `9bff1a26-ee20-4bc1-8041-77a493255f16` |
| Inbox message | `736ab077-2a46-4a1f-b65c-ad7c12e8229d` |

```text
pwd:
/srv/agent-relay/var/apps/agent-relay/qualification/91e83cea26a64ec185525114d195628f/workspace

ls -1:
relay-fixture.txt
```

The retained raw files are `rc6-round-trip.json` and `rc6-native-trace.json` under `local/apps/agent-relay/evidence/2026-11-07-stable-qualification/`. Their SHA-256 values are `aefdfbc3be71485debc65d2986b3aa31d6e2add1f192c66853ccd75e2ca33303` and `cda253023c7e998fa1b98787ac0bfe1b3fb57e9ea069fa6da6018e270945a43c`, respectively. See the [stable deployment observation](2026-11-07-stable-deployment.md) for the separate Pilotage AP check.
