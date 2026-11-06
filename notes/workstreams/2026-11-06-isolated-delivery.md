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

Two successive dev launches created distinct scopes. A further seeded launch exported `agent_relay_dev`. A fresh stdio client discovered exactly the synthetic project using that fragment. None of these launches changes a global client registration.

The first full rehearsal with scoped checks exercised the installed RC4 binary on synthetic data, an injected post-migration failure with RC5, then a successful RC5 update. A fresh scoped connection discovered the same fixture project after rollback and after successful update. The protected project and credential files matched. Failure plus recovery took 5.187 seconds and the successful upgrade with MCP probe took 3.656 seconds in that run. Pilotage AP answered before and after with the same process identity and hashes. The final candidate is to be qualified again after the dev fixture change, from its exact clean commit.

The archive and rehearsal gates do not cover all rows of the [coverage matrix](2026-11-06-application-coverage.md). Native WSL/model exchanges and exact-archive visual acceptance remain separate checks. Stable deployment is deliberately refused by the current local deployment route. A separately approved promotion route must consume the final qualification and operational checks before it can apply anything to Pilotage AP.

## Final result

RC `1.1.0-rc.5` from clean commit `4ec2304` passed exact archive qualification in 24.125 seconds. SHA-256 `73beca6016d360c085232af53e45455880f64d5eceed5e72bb53e885ca4afee1`. The same archive passed rehearsal in 22.060 seconds, including failed update and recovery in 4.843 seconds and successful upgrade with fresh MCP in 3.468 seconds. Three protected synthetic project/credential files were preserved. The stable process answered and matched its before/after identity and configuration in both operations. No model or WSL call, stable deployment, tag or publication occurred.

Machine-local receipts are `var/apps/agent-relay/isolated-delivery/qualify/69453bc1dd8a4c6183d0b9979693c1a1/result.json` and `var/apps/agent-relay/isolated-delivery/rehearse/a5d9ecc27d514a22a221a66906f140ea/result.json`. The rehearsal log points to the detailed transaction report. The qualification receipt names the exact archive. A new checkout can reproduce those reports using the [isolated delivery commands](../../apps/agent-relay/doc/developing/development.md#isolated-delivery) and a qualified local toolchain. Rehearsing an old receipt requires its exact checkout; otherwise qualify the current commit first.

Next authorized-scope boundary is a new GO for the remaining candidate checks and the stable promotion gate. Deployment to Pilotage AP remains a separate decision. The objective counter is unavailable; timings above are measured command timings.
