# WSL launch diagnostics

**Utility:** records AR-WSL-07 investigation, corrections and isolated verification.  
**Usage:** resume from the checkpoint before operating on any native test scope.

## Scope and findings

The operator authorized WSL timeout diagnosis and concrete corrections on isolated copies. Branch `codex/wsl-launch-diagnostics` starts at `06f4239`. Stable changes, shared WSL restarts and model calls are outside scope. The scoped objective API remains refused under WS-GOAL-01. Start time was 11/11/2026 at 10:03 Europe/Paris.

The historical Python timeout retained neither launcher output nor a Linux-start acknowledgement. Its trigger cannot be reconstructed. Two read-only probes now succeeded in 0.156 and 0.141 seconds; their raw output is retained at `local/apps/agent-relay/tests/wsl-launch-diagnosis-1431c013666d41a78c900147a854e5a0/result.json`.

Concrete defects in the existing C# process owner were an input write without cancellation/deadline and timeout handling that discarded process outputs. Reads now capture bytes concurrently, preserve partial diagnostic data after a deadline, and decode native UTF-8 and Windows UTF-16 launcher text. Input uses the same deadline as waiting. The typed timeout remains a TimeoutException so existing uncertain-outcome handling continues to apply.

The trusted local `wsl-maintenance` CLI uses this owner. It adds a unique flushed Linux-start marker before the requested script. A marker confirms Python started; its absence means native start is unconfirmed, not that effects are impossible. Python session synchronization and its native fixture use a facade to this C# command rather than another timeout implementation. No automatic replay is introduced. Other established subprocess consumers remain outside this focused migration.

## Qualification

The offline application suite passed with the new process owner, including the existing uncertain-outcome tests and process smoke. Its artifacts are `local/apps/agent-relay/tests/7cd876620b9042f19ac307dcf412fbc8`. After the final timing/tail adjustments, a targeted build and process check passed with zero warnings/errors. `local/apps/agent-relay/tests/process-diagnostics-60bfdc98cb9c4121b9604a54dd7bb068/result.log` (SHA-256 `FEBAA10DE89D81C48E351F901302B4979D8D1720E53F150B20FC5E46E511DA2C`) covers Unicode echo, blocked stdin, preserved partial timeout output and caller cancellation.

The final C# runtime is selected by `var/apps/agent-relay/preparing/wsl-diagnostics-final-2fb74ee39974491593a9e8370e86a252/mcp-runtime.json`. Native diagnostic tests passed in 3.422 seconds. The controlled one-second timeout returned partial stdout/stderr in 1.093 seconds and retained possible effects. The absent fictional distribution returned the correctly decoded Windows diagnostic `Wsl/Service/WSL_E_DISTRO_NOT_FOUND`, with no Linux-start acknowledgement. Receipt `local/apps/agent-relay/tests/wsl-diagnostics-6000f875c18e43c1a0c179ada200f79c/result.json` (SHA-256 `31371FB5729C522546520EE6A5E56850F156EC1DBB9E25D85E797245284F8339`) retains both native duration and total runtime wait per call.

The initial diagnostic suite took 33.187 seconds although its measured native calls summed to under two seconds. Its record `local/apps/agent-relay/tests/wsl-diagnostics-79eae74785184a8e8447898924043c4a/result.json` cannot locate that extra wait retrospectively. This motivated the separate `runtimeWaitSeconds` metric. No cause such as antivirus, Windows scheduling or WSL-service behavior has been established. There were no new natural launch timeouts during this lot.

The final session synchronization qualified current and legacy marked WSL copies in 6.656 seconds. It preserved database/runtime bytes, refused active work and competing host ownership, updated only the selected roots, and cleaned its two native copies. Receipt `local/apps/agent-relay/tests/adapter-sync-6be68f6e9961430dbad3834f883cc486/result.json` (SHA-256 `80534EE2F4CD8F77D4C540E62C568AABCC14A3DA7501A5B228A7AC2822B0DD64`). Every native check used fictional or no-effect inputs, with zero model calls.

Workspace verification and six design-contract tests passed. A deterministic runtime-timeout fixture verifies that the Python facade retains partial runtime output/error and invokes the operation once.

## Self-review and checkpoint

Concrete process defects are corrected and the historical cause remains open under AR-WSL-07. The next natural occurrence can now be distinguished from an operation failure without replay. The follow-up cleanup proposal is moving the two remaining direct maintenance callers (`sync-wsl.py` and `migrate-storage.native_call`) onto this owner, with their own scoped regression checks, rather than copying the diagnostic implementation.

Implementation commit `a731725` is pushed on `codex/wsl-launch-diagnostics` and integrated by the merge carrying this final checkpoint. Concrete corrections and isolated qualification are complete. The historical external trigger remains open under AR-WSL-07. Stable deployment remains separately gated. Scoped goal consumption is unavailable because the historical objective still blocks creation; its usage is not attributed to this work.
