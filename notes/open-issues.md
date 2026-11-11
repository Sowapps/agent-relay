# Open issues

**Utility:** records unresolved product, delivery and agent-process problems and their recurrences.  
**Usage:** look for the same symptom before starting an investigation. Update the existing ID and link retained evidence when it recurs. See the [development workflow](../doc/developing/workflow.md).

Count independently observed occurrences. Use “unknown” if the historical count is uncertain. Close an item only when its stated check passes, then preserve the outcome under its ID in the relevant workstream or observation.

| ID | First / last observed | Occurrences | Impact and proof | Next action / closure |
| --- | --- | --- | --- | --- |
| AR-STARTUP-01 | 08/11/2026 | 1 | Pilotage AP lost its stable host after reboot. The startup entry still points at the earlier installation. [Live diagnosis](../apps/agent-relay/notes/observations/2026-11-08-pilotage-ap-live-reply.md). | Isolated cold startup and upgrade passed. Close after an authorized stable rollout, startup preference verification and legacy transfer. |
| AR-MCP-02 | 07/11/2026 | 1 for the stable cutover | A loaded Codex chat retained the old MCP route after a scope move. [Deployment observation](../apps/agent-relay/notes/observations/2026-11-07-stable-deployment.md). | A loaded fixture followed an upgrade at the same address and refreshed its catalog. Confirm the live client after the authorized stable upgrade. |
| AR-SIGN-03 | 05/11/2026 | unknown | Avast CyberCapture interrupted qualification of a local unsigned build. [Session close workstream](workstreams/2026-11-05-wsl-session-close.md). | Reopen signing and antivirus reputation work when authorized. Record evidence from the exact reviewed build. |
| WS-GOAL-01 | 05/11/2026 / 11/11/2026 | at least 7 | An unrelated unfinished objective prevents creating a scoped tracked goal. [Latest workstream](workstreams/2026-11-11-wsl-launch-diagnostics.md). | Use dated receipts for timing. Close when the tool allows a new objective without relabeling historical usage. |
| AR-QUAL-04 | 09/11/2026 | 1 | The archive qualification and in-place rehearsal require separate invocations. [Current workstream](workstreams/2026-11-09-stable-startup.md). | Add the approved baseline to the existing qualification entry point so one report covers both checks. |
| AR-WSL-07 | 11/11/2026 | 2 historical expirations | Historical launcher stalls remain unexplained because the original diagnostics were discarded. Shared C# input deadlines, bounded partial outputs, Linux-start acknowledgement and separate runtime-wait timing are corrected and qualified. [Investigation and proof](workstreams/2026-11-11-wsl-launch-diagnostics.md). | Inspect a future natural occurrence using retained phase, input completion, durations and stream tails. Do not infer antivirus or WSL-service causality from timing alone, restart shared WSL, or replay uncertain operations automatically. |
