# Post-audit tool cleanup

**Utility:** preserves the bounded cleanup scope and a safe resume point.  
**Usage:** read before resuming the audit tooling cleanup or repeating its targeted checks.

## Scope and timebox

GO of 2026-11-06 covers confirmed code cleanup, useful local report lookup and targeted tests. Work started around 09:12 Europe/Paris from `3ab7ac3` on `codex/audit-tooling-polish`. Nominal checkpoint is 09:32 and hard stop is 09:34. Pilotage AP is outside this workstream, including its live data, process and client bindings. No deployment, native agent call, WSL or publication is needed. The goal service refused a new objective because the unrelated unfinished objective remains in its slot.

## Checkpoint

The portable verifier and rehearsal now share Windows-safe ZIP extraction and verified identity. The verifier uses the same MCP transport as the other relay checks. The isolated CLI retains the active phase and failures, and the read-only status command discovers older and new reports. The unchanged RC5 operational baseline and its prior receipts remain described in [isolated delivery](2026-11-06-isolated-delivery.md).

## Verified handoff

- Qualification and rehearsal share archive identity, streaming hashes and Windows member validation. Publication compares the current inputs against the manifest inside the exact ZIP and checks the accompanying checksum file. The simulated publication gate refuses changed source even if the separate payload manifest was edited to match it.
- The portable test now uses the shared MCP client. The rollback harness has an explicit baseline, a typed injected failure and cleanup restricted to the child process handles it created. The project file and both binary credential files were checked after rollback and update.
- `status` reads retained reports only, including the previous format. A failed or interrupted operation preserves its phase and error. Development and qualification no longer depend on a stable health probe. Rehearsal with an explicit baseline uses two local archives. The prior installed-baseline invocation remains available.
- Code checkpoint `76eb483`. Local candidate qualification passed in 19.703 seconds, and rehearsal against the earlier RC1 archive passed in 20.047 seconds. Injected failure and rollback took 4.890 seconds. The same-archive baseline was refused and reported. Status, simulated publication, three evidence regressions, five design regressions and document links/syntax passed.
- The two RC5 archives, before and after this cleanup, embed the same MCP contract hash `15aa56934605740376204c98fee797a639f9ad12887ac2a3923378edd4973923`. This is an artifact comparison, separate from checking a live client's negotiated catalog.
- Machine-local receipts are `var/apps/agent-relay/isolated-delivery/qualify/85e30dc248ec459898c90825e9804457/result.json` and `var/apps/agent-relay/isolated-delivery/rehearse/448cb4f5bf3e4a76ab5ab47b5f0ed677/result.json`. Both report `stableObserved=false`. No Pilotage AP configuration, credentials, runtime or session was used, and no model, WSL, deployment, tag or release was initiated.

For the next authorized audit pass, use [the development commands](../../apps/agent-relay/doc/developing/development.md#isolated-delivery) and select the next concrete gap from the [coverage matrix](2026-11-06-application-coverage.md). These tool checks do not claim native WSL or visual qualification. The tracked-objective token total is unavailable.

## Pilotage AP report at handoff

Florent relayed an `open_session` refusal with `connection_outdated`, recovery `reload_client_connection` and no work submitted. The current code uses this same outward error for an old catalog and for a worker/host version or contract mismatch. The broker updates its remembered catalog on a successful `tools/list`, not merely on sending a change notification. The two RC5 archive hashes above match at the contract level. This does not identify the failing live path. A separately authorized read-only investigation should first correlate the original diagnostic ID and the connection status from the affected chat with its host and worker identities. Do not retry the request, reload a client or modify the stable installation during that investigation.
