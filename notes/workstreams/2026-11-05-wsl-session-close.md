# WSL session close correction

**Utility:** retains the authorized correction and recovery checkpoint.
**Usage:** read before resuming; no model calls or project-file changes are authorized.

Florent gave GO to correct lifecycle refusal, verify open → ready → close without a model, deliver and report to Pilotage AP. Dedicated branch: codex/wsl-session-close.

Objective: restore closure of owned sessions in current runtime storage without weakening ownership checks or changing AP permissions/files. Objective API creation refused by an unrelated historic blocked goal; this record tracks the scoped objective, with no attributable token counter.

Cause: native_clients.control still accepts only the legacy agent-relay-session- basename. Provisioning now uses /srv/agent-relay/var/apps/agent-relay/sessions/<UUID hex>. It rejects the private runtime root, not the assigned project workspace.

Correction: retain root_path canonical-path/Linux-owner checks, additionally verify a non-symlink current-user-owned provisioning marker, matching session UUID/root and named Linux account. Accept exactly current and legacy owned layouts; profiles/project folders remain refused. Existing copied adapters require an explicit scoped refresh; deployment alone does not replace them.

Next: targeted guard tests, deploy new packaged adapter, refresh only newly created qualification session if needed, verify a fresh AP workspace session model-free, preserve configuration/permission bytes, commit/push/merge and report. Existing AP sessions will not be interrupted.
