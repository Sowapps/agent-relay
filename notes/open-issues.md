# Open issues

**Utility:** records unresolved product, delivery and agent-process problems and their recurrences.  
**Usage:** look for the same symptom before starting an investigation. Update the existing ID and link retained evidence when it recurs. See the [development workflow](../doc/developing/workflow.md).

Count independently observed occurrences. Use “unknown” if the historical count is uncertain. Close an item only when its stated check passes, then preserve the outcome under its ID in the relevant workstream or observation.

| ID | First / last observed | Occurrences | Impact and proof | Next action / closure |
| --- | --- | --- | --- | --- |
| AR-STARTUP-01 | 08/11/2026 | 1 | Pilotage AP lost its stable host after reboot. The startup entry still points at the earlier installation. [Live diagnosis](../apps/agent-relay/notes/observations/2026-11-08-pilotage-ap-live-reply.md). | Qualify on-demand recovery and same-scope updates, then verify the authorized stable deployment and the legacy entry migration. |
| AR-MCP-02 | 07/11/2026 | 1 for the stable cutover | A loaded Codex chat retained the old MCP route after a scope move. [Deployment observation](../apps/agent-relay/notes/observations/2026-11-07-stable-deployment.md). | Confirm that a loaded broker follows a stable upgrade with the same registration, and document the remaining client-side catalog refresh when the contract changes. |
| AR-SIGN-03 | 05/11/2026 | unknown | Avast CyberCapture interrupted qualification of a local unsigned build. [Session close workstream](workstreams/2026-11-05-wsl-session-close.md). | Reopen signing and antivirus reputation work when authorized. Record evidence from the exact reviewed build. |
| WS-GOAL-01 | 05/11/2026 / 09/11/2026 | at least 4 | An unrelated unfinished objective prevents creating a scoped tracked goal. The current goal tool still refuses. [Current workstream](workstreams/2026-11-09-stable-startup.md). | Use dated receipts for timing. Close when the tool allows a new objective without relabeling historical usage. |
