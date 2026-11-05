# WSL session close correction

**Utility:** retains the authorized correction and recovery checkpoint.
**Usage:** read before resuming; no model calls or project-file changes are authorized.

Florent gave GO to correct lifecycle refusal, verify open → ready → close without a model, deliver and report to Pilotage AP. Dedicated branch: codex/wsl-session-close.

Objective: restore closure of owned sessions in current runtime storage without weakening ownership checks or changing AP permissions/files. Objective API creation refused by an unrelated historic blocked goal; this record tracks the scoped objective, with no attributable token counter.

Cause: native_clients.control still accepts only the legacy agent-relay-session- basename. Provisioning now uses /srv/agent-relay/var/apps/agent-relay/sessions/<UUID hex>. It rejects the private runtime root, not the assigned project workspace.

Correction: retain root_path canonical-path/Linux-owner checks, additionally verify a non-symlink current-user-owned provisioning marker, matching session UUID/root and named Linux account. Accept exactly current and legacy owned layouts; profiles/project folders remain refused. Existing copied adapters require an explicit scoped refresh; deployment alone does not replace them.

Next: targeted guard tests, deploy new packaged adapter, refresh only newly created qualification session if needed, verify a fresh AP workspace session model-free, preserve configuration/permission bytes, commit/push/merge and report. Existing AP sessions will not be interrupted.

## Completed qualification and delivery

Four model-free guard tests passed (both layouts and stop/interrupt, invalid roots/identity/user/symlink/marker ownership). Build succeeded. Source d40c8c3, integrated main 01d7e60 pushed; deployed generation 320367844223cc1663f1f6e9c0794c4d7234719b7c078057cf9d8b028d6a1f3d, version 1.1.0-rc.1 and public contract unchanged. No tag or release.

Real MCP AP session 6e4b15f0-4091-4c6d-97a2-493e1e321c04 used the existing /home/sowapps/projects/personal-assistant workspace: open returned starting, get returned ready/workActive=false, close returned closed/workActive=false. No model message or project command was submitted. Raw result: local/apps/agent-relay/wsl-session-close/mcp-lifecycle.json. Selected private adapters for the two reported original sessions were refreshed after inactivity checks; their processes were not restarted/interrupted. Seven protected configuration/review/credential files remain byte-identical; three fresh scoped MCP connections verified, zero registration edits. Reports: local/apps/agent-relay/wsl-session-close/preservation.json and var/apps/agent-relay/deployment/wsl-session-close/.

Deployment initially refused because ui-host.json recorded obsolete PID28616 while the exact executable/scope had one live tray owner PID21700. Reconciled only that metadata after exact executable/command-line uniqueness checks and retained its prior copy. Reused the already prepared runtime (no rebuild), then normal deployment succeeded as PID16936. Broader automatic recovery of stale host metadata remains a separate issue. Avast CyberCapture also delayed analysis of the unsigned new executable; signing and Avast publisher validation are proposals only, with no security-setting change or submission. The test session name exposes an existing Unicode-encoding defect (em dash becomes mojibake); not expanded into this lifecycle fix.

Completed 05/11/2026 around08:02 Europe/Paris, about7minutes from branch creation (07:54:53), excluding initial investigation. Task-specific token measurement unavailable due unrelated blocked goal; its historic counters are not reused. Objective recorded here complete, pending delivery notification to Pilotage AP.
