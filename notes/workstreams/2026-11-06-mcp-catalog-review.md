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
