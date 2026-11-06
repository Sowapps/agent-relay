# Isolated Agent Relay delivery

**Utility:** records authorization, isolation constraints and the next safe delivery action.  
**Usage:** read before resuming the dev/RC delivery workstream.

## Scope and starting checkpoint

GO of 2026-11-06 covers local tooling, isolated qualification and deployment rehearsal on a test copy. Pilotage AP is the protected stable instance and requires a separate deployment GO. The goal service refused a new objective because an unrelated unfinished objective already occupies its slot.

Branch `codex/isolated-delivery` starts at `fe6aef2`. Earlier delivery records identify `var/apps/agent-relay/v1-qualification` as the shared Pilotage AP scope, despite its historical directory and RC version names. This workstream must preserve its data, startup and MCP configuration and leave its host running if already running. No existing agent sessions or user workspaces are fixtures.

## Design checkpoint

Use fresh, owned development scopes instead of deleting state in place. Reuse the portable verifier and deployment transaction rather than creating another migration algorithm. Keep exact candidate receipts separate from duration/resource measurements. Treat an isolated scope and a simulated rollback as distinct from a live deployment approval.

## Checkpoint

The guarded entry point now supports offline build/test, a fresh dev launch, exact ZIP qualification and rollback rehearsal with a synthetic fixture. The deployment transaction refuses the Pilotage scope before reading the candidate or stopping a host. The guard refusal and five shared design checks passed. The ordinary integration and Windows process smoke suite passed in 36.94 seconds. The before/after Pilotage snapshot matched. The first restricted process lookup was refused by Windows; the qualified run used the authorized host execution context.

Next action is to qualify the exact clean RC commit, then pass its receipt to rehearsal. Compare the stable snapshot and record whether a live Pilotage process actually answered. The clean archive and rollback check do not by themselves cover the native WSL/model and visual gates in the [coverage matrix](2026-11-06-application-coverage.md).
