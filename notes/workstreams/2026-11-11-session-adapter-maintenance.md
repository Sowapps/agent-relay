# Session adapter maintenance

**Utility:** records the authorized AR-MAINT-06 correction and isolated qualification.  
**Usage:** consult before resuming or modifying session helper synchronization.

## Scope and design

The operator approved centralizing the synchronization check, selecting a current guard for older installations, and testing on an isolated WSL copy. Branch `codex/session-adapter-maintenance` starts at `833f7b0`. Stable deployment and model calls are outside this scope.

`deployment.demand_idle` calls the existing C# maintenance operation. Synchronization uses the registered runtime by default. An explicit `--runtime-pointer` can select a validated current runtime for an older installation, without rewriting its pointer. The C# command remains responsible for the durable-work states. Python retains read-only session selection and WSL transport.

The earlier script had a check/copy race. Synchronization now holds the common exclusive host lock from the idle check through all selected copies. A running relay must be stopped through its separately authorized procedure first. Acquiring a lock once is simpler than introducing another live-maintenance protocol. It prevents a host from admitting new work during this offline operation.

## Qualification and closure

AR-MAINT-06 is resolved in source. The production script contains no durable-work SQL predicate. It calls the same C# guard as migration and deployment, retains the host lock throughout copying, and loads sessions read-only. It is importable for an isolated qualification and has an ordinary CLI entry point.

`test-session-adapter-sync.py` passed with the current prepared runtime from the previous lot and the historical startup-owner runtime. It exercised both the default current runtime and refusal of the older unsupported command before WSL, followed by the explicit current-guard override. It verified `pending` and `running` refusal, retained active records, competing host lock refusal, the lock during actual Linux copies, absent selection refusal, unchanged database/runtime pointer, selected-only publication and current/legacy native layouts. Each selected copy received all nine packaged files. Only the two marked native copies were removed. No model was called.

Final receipt `local/apps/agent-relay/tests/adapter-sync-a7df72f8568e4e6697c349477be0384f/result.json` records 5.984 seconds (SHA-256 `2E889DFDCAB5232B9BEC8AAE7A8BD427CFB2D7A994B4F7464AE6848C096A83B4`). The earlier successful receipt is `local/apps/agent-relay/tests/adapter-sync-360290b9fb6249c4b22880e52bc2a247/result.json`. The five shared design-contract tests and workspace verification passed. C# source was unchanged; the actual prepared C# process was exercised by this integration check.

The first attempt used an incomplete fixture (initialize does not create the session tables). The harness now creates its explicit synthetic schema and avoids cleanup calls before native preparation. An initial cleanup launch and a later preparation launch timed out. The latter receipt is `local/apps/agent-relay/tests/adapter-sync-de489fda68fa45d29b422fd9fba43ebe/result.json`; its marked copies were removed by its cleanup. A read-only `/usr/bin/true` availability probe succeeded, then a fresh final qualification passed. The origin of the intermittent launcher timeout remains under AR-WSL-07. Redirected streams now tolerate launcher diagnostics outside UTF-8, and cleanup failure marks a qualification unsuccessful.

## Self-review and continuation

The dedicated check reproduces synchronization without borrowing a live agent session. It avoids inventing another provisioning workflow and uses the packaged identity definition. The next concrete improvement is retaining bounded timeout diagnostics at the WSL invocation boundary, so an availability problem is distinguishable from a failed Linux copy. No automatic replay or shared-distribution restart is warranted by these observations.

Implementation commit `91c315f` is retained on `codex/session-adapter-maintenance` and integrated by the merge carrying this final record. The authorized source cleanup is complete. AR-WSL-07 is the next proposed diagnostic lot. Tests used isolated copies; stable deployment remains separately gated.

The tracked-goal API still refuses a scoped objective because the unrelated historical goal is unfinished (WS-GOAL-01). Start time was 11/11/2026 at 09:00 Europe/Paris. Scoped consumption is unavailable.
