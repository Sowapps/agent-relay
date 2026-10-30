# Linux storage checks

**Date:** 2026-10-28, Europe/Paris morning.  
**Source:** dedicated filesystem probes across WSL process, tool-call and distribution-restart boundaries.  
**Validation:** observations awaiting Florent's validation; not evidence.

## Scope

The explicit GO applies only to model-free storage verification (evidence `2026-10-28-go-is-limited-to-model-free-storage-checks`). No Claude, Codex CLI, worker, MCP pilot or auxiliary model was
launched. No product code, client settings or WSL settings were changed. A disposable local Python harness drove the filesystem probes.

The probe identifier was `01b02b4a-15f5-4b3d-9183-0f5bce9cd7ba`. Each location contained the same unique payload, exclusively created and flushed with its directory and parent. Checks compared its
SHA-256 rather than relying only on process exit codes. Each later exchange carried a fresh nonce; machine identity and process-start information were checked across restart.

## Results

| Location | Next WSL process | Next tool call | After WSL restart | Later tool call |
|---|---|---|---|---|
| `/tmp` | Intact | Missing | Missing | Missing |
| `/var/tmp` | Intact | Intact | Intact | Intact |
| `/home/sowapps` | Intact | Intact | Intact | Intact |

Each intact result had the original hash. The user-location probe was a dedicated hidden directory, separate from existing project workspaces. No application data location was installed.

The running-distribution list confirmed that Ubuntu-24.04 stopped. Its machine identity remained unchanged after restart. The kernel boot identifier did not change, but the distribution's PID 1
start time did, confirming a fresh distribution instance. A kernel boot identifier alone would have been an insufficient restart test here.

## Interpretation

The temporary-location probe disappeared before the explicitly requested distribution restart, while the kernel boot identifier remained unchanged. Therefore the disappearance cannot be attributed
solely to that later restart. The same observation was not reproduced in the other two locations.

The checked WSL boot configuration enabled systemd. The installed tmpfiles configuration contained `D /tmp 1777 root root 30d` and a commented `/var/tmp` rule. Those lines were read but do not
establish what removed the fresh probe. No deletion mechanism has been identified, and no system setting was changed to investigate it further.

The earlier [one-response POC](2026-10-28-one-response-poc.md) therefore used a location that is unreliable across the observed tool boundaries. This is a concrete defect in that test setup, not
proof that the rest of the relay or real-agent exchange works.

## Workspace and data-directory distinction

The user-location probe passed the tested persistence boundaries, making a dedicated user-owned directory a reasonable candidate for a durable data directory. This is a recommendation, not a
selected product location or a guarantee against every failure mode.

The temporary persistent-location probe also passed. A dedicated directory there is a candidate for a disposable test workspace, not a replacement for application data backup and retention.
Existence checks found no project instruction files in that location's checked ancestors. A user-level Claude instruction file exists in the account; any future client test still needs the
approved isolation settings. No model was launched to test instruction loading.

## Cleanup and retained artifacts

Cleanup verified the payload hash before deleting only each probe file and its dedicated empty directory. All three directories were confirmed absent. Ubuntu-24.04 was left restarted. Existing
files, sessions and settings were untouched apart from the authorized distribution stop.

The ignored local diagnostics directory retains the probe configuration, stage snapshots and restart confirmation. The helper remains local and is not a product component. The observation report
is the versioned review surface. No new evidence record was created from these measurements.

## Tracking limitation

The goal tool refused a new objective because the original POC objective remains unfinished and blocked. The original objective was not falsely marked complete. Its frozen 65,361-token and
800-second counters belong to the earlier POC, not this storage check. A separate token measurement for this step is unavailable through the exposed goal tools.

The storage-check work started with a Windows clock reading of 09:40:59 local time. The result checkpoint was 09:48:16, 7 minutes 17 seconds later, before final documentation checks. This is a wall
clock interval, not goal-tool timing. No auxiliary model or inference subprocess was used.

## Next boundary

This GO does not authorize another Claude request or an expansion into MVP implementation. A subsequent limited POC can propose a durable user-owned data directory and a separate disposable
workspace, and must receive its own explicit GO before execution.
