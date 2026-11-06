# MCP refresh, design review and RC promotion rehearsal

**Utility:** preserves the authorized scope, checkpoints and qualification gates.  
**Usage:** read before resuming the four approved lots.

GO covers MCP catalog fix, design review, final RC qualification and promotion rehearsal on a copy. Pilotage AP deployment is separate. Branch `codex/mcp-catalog-review` starts at `f585edb`. Goal service rejected a new objective because an unrelated unfinished objective occupies the slot.

The read-only incident diagnosis found the AP host and worker on RC4 with identical contract hashes while the retained connection still carried the older catalog. The previous rollout verified fresh bindings and marked the existing client unconfirmed. A notification race is present in source. Historical traces do not establish whether the affected client received a notification.

## Checkpoint

RC6 differentiates client catalog and relay runtime mismatch failures. One broker method owns notification emission for idle and request-triggered transitions. A successful tools/list remains the confirmation gate. Host verification and existing chat verification are reported separately.

Next run a candidate build and retained connection fixture on a copy. Review the remaining application zones against the design guide and the existing coverage matrix before freezing the final archive.

## Design review by zone

| Zone | Owner and review outcome |
| --- | --- |
| Core admission and permissions | `RelayApplication` owns lifecycle admission and `ProjectPolicy` owns grants. Removed the duplicated native destination check from the infrastructure boundary. Retained session checks preserve grant revocation while allowing inspection or interruption during a drain. |
| MCP and pipe | Broker owns catalog notification and acknowledgement. Facade owns request validation and worker/host negotiation. Separate recovery codes and fixture results distinguish these concerns. Authentication remains at the pipe boundary. |
| Sessions, inbox and WSL adapters | Composition remains in `NativeAdapterComposition`. Session identity and scoped inbox import are separate. Scope retention is revalidated without accidentally repeating new-work lifecycle admission during session control. |
| Commands and transfer | `ScopedCommands` owns command preparation and matching receipts. `FileTransferService` owns staging and publication. Command and transfer preparation now read grants and adapter context from the same configuration snapshot. |
| Durability and recovery | `WorkService` retains pending intent before dispatch and requires matching receipts for recovery. The isolated deployment snapshot and rollback now use the same retained file list, including project and relay configuration. |
| Windows monitoring | `PipeMonitorSource` keeps observation passive and independent of native inbox cursors. WebView remains a projection of monitoring data. The exact archive render remains an explicit acceptance check. |
| Scripts and tests | Reuse exact ZIP verification, update/rollback and test fixtures. Rehearsal explicitly checks the target, refuses active work and records existing-chat confirmation separately. |

This is a responsibility and boundary review supported by selected regressions, not a line-by-line certification. Before real promotion, the host must prevent new admissions across the idle-check/shutdown interval. The existing idle checks are useful but do not provide that cross-process guarantee. Pilotage AP remains outside this rehearsal.

## First candidate observation

The retained catalog fixture passed on an RC1-to-RC6 contract switch. The first exact archive passed portable checks and promotion rehearsal. WSL preparation, Codex session readiness, the direct command and upload reached their expected outcomes, but download rejected the private Windows partial because it was validated as if it belonged to the project workspace. The correction validates private staging with its own path policy. The fixture session was closed. Rebuild and repeat the exact archive and native transfer checks. The selected RC6 qualification window was found, but Computer Use app approval timed out before capture.

## Final handoff

- Lot 1 passed the real retained connection fixture and the named-client fixture. One tools notification and one resources notification were observed across the switch. Worker/host mismatch and stale catalog returned separate pre-execution errors. The same connection succeeded after listing the new catalog. An unsupported existing client remains explicitly unconfirmed.
- Lot 2 completed the zone review above. The integration suite passed in 41.171 seconds. No application scope is claimed to be free of all defects.
- Lot 3 passed portable startup and scoped MCP on RC6 commit `3cc8bcb` with SHA-256 `3bd4ef204bfa9fb7cfed7e30b05aacb0379b44e96661fdb8be57ac0c9543ab3e` in 29.688 seconds. The same archive passed WSL Codex session readiness and close, direct command in the declared workspace, foreign-project refusal and 1,167,360-byte transfer in both directions with matching SHA-256. No model prompt. The window was started in a unique test scope. Actual visual capture is pending after the authorization timeout. Florent instructed that the Windows Computer Use authorization prompt must never be requested again.
- Lot 4 passed the exact-archive rehearsal in 24.907 seconds. The copy refused the wrong target and active work. Rollback restored the previous runtime, configuration, project and credentials, and fresh scoped MCP worked after rollback and after the successful update. The existing chat verdict remains unconfirmed by design. Do not apply the rehearsal to Pilotage AP. Real promotion requires an admission freeze or an independently verified maintenance window as well as separate deployment authorization.

Machine-local evidence lives in `var/apps/agent-relay/isolated-delivery/qualify/ab07b019e1ef4027acd96ba51c9036c8/result.json`, `var/apps/agent-relay/isolated-delivery/rehearse/d8455430242e436ab24f7547b13626a2/result.json`, `local/apps/agent-relay/qualification/mcp-update-8200703a339f476e9e8d6cfda6feb66d/result.json` and `local/apps/agent-relay/qualification/rc6-native-c18c8465026947ce8eec528590f1473f/v1-acceptance.json`. Pilotage AP was outside all test scopes. No stable deployment or publication occurred. The objective service counters remain unavailable.
