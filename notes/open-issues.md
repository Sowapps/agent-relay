# Open issues

**Utility:** records unresolved product, delivery and agent-process problems and their recurrences.  
**Usage:** look for the same symptom before starting an investigation. Update the existing ID and link retained evidence when it recurs. See the [development workflow](../doc/developing/workflow.md).

Count independently observed occurrences. Use “unknown” if the historical count is uncertain. Close an item only when its stated check passes, then preserve the outcome under its ID in the relevant workstream or observation.

| ID | First / last observed | Occurrences | Impact and proof | Next action / closure |
| --- | --- | --- | --- | --- |
| AR-IO-08 | 12/11/2026 | 1 | One isolated full-suite run returned UnauthorizedAccessException during ProjectSettingsStore's atomic replace; the justified rerun passed. No cause is established. [Update continuation](workstreams/2026-11-12-stable-1.1.1.md). | Retain the failing path/stack and inspect the next natural occurrence. Do not attribute it to antivirus or add broad retries without evidence. |
| AR-MCP-02 | 07/11/2026 / 12/11/2026 | 2 live observations | The AP chat now reads stable 1.1.1 and its project, but still reports clientRefreshRequired=true. The owning Codex control socket is unavailable. [Current deployment](workstreams/2026-11-12-stable-1.1.1.md). | Reload the actual Codex MCP catalog through its supported client lifecycle, recheck the same chat and confirm only when clientRefreshRequired=false. Stable installation and all nine logical tables were verified. |
| AR-SIGN-03 | 05/11/2026 | unknown | Avast CyberCapture interrupted qualification of a local unsigned build. [Session close workstream](workstreams/2026-11-05-wsl-session-close.md). | Reopen signing and antivirus reputation work when authorized. Record evidence from the exact reviewed build. |
| AR-WSL-07 | 11/11/2026 | 2 historical expirations | Historical launcher stalls remain unexplained because the original diagnostics were discarded. Shared C# input deadlines, bounded partial outputs, Linux-start acknowledgement and separate runtime-wait timing are corrected and qualified. [Investigation and proof](workstreams/2026-11-11-wsl-launch-diagnostics.md). | Inspect a future natural occurrence using retained phase, input completion, durations and stream tails. Do not infer antivirus or WSL-service causality from timing alone, restart shared WSL, or replay uncertain operations automatically. |
