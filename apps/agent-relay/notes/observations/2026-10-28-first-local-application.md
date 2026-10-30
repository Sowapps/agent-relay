# First local C# application slice

**Date:** 2026-10-28  
**Status:** measured observations awaiting operator validation.

## Scope and result

The first application GO and DRY/SOLID requirements are retained as evidence `2026-10-28-first-application-slice-and-reusable-tools`.
Implemented the first local application slice: Windows tray/window, a correlated message client, current-user pipe host and durable SQLite exchange.
No Codex/Claude request, WSL operation, global configuration, service or automatic startup was performed.
The local endpoint is deliberately labelled a demonstration; its echo is never reported as an AI-agent response.

## Prerequisites qualified before integration

Selected Microsoft.Data.Sqlite 10.0.14 from the official NuGet catalog. Central package versions and five project/gate lock files retain exact dependencies and integrity hashes.
The isolated gate reopened a durable SQLite database, reported native SQLite 3.53.3, and transferred a message over a CurrentUserOnly/FirstPipeInstance Windows pipe.
The first gate incorrectly waited for a pipe write before its reader and reached the cancellation deadline; concurrent consumption fixed that Windows backpressure case before integration.
One diagnostic property initially used an instance member statically and was corrected before the gate passed.
Gate result is retained at `local/apps/agent-relay/dependency-gate/result.json`; no gate output substitutes for application tests.

## Code organization and reusable tools

Core owns immutable contracts, route/input validation, correlation, idempotence and a single-active-exchange use case behind store/responder ports.
Infrastructure owns SQLite, JSON/framing, pipe naming/client/server, host ownership and the local demonstration adapter.
Windows owns composition, the French window/tray and send/read executable modes. No ORM, dependency-injection framework, generic repository or event bus was added.

One JSON codec, one frame codec, one pipe-name owner and one local-route factory are reused by host, UI and client.
AppCommands owns qualified-tool selection, isolated environment use, locked restore/build and test/run composition.
RuntimeStaging owns verified content-addressed runtime copies, reused by both the normal launcher and process tests.
The CLI wrapper delegates to the same application client as the UI rather than implementing another messaging path.

Commands are `dev.ps1 build/test/run -App agent-relay [-Offline]`, plus the application-specific `tools/relay.ps1 send/read` client.
The run command opens the window/icon and waits for termination. Compiler outputs remain separate from active runtime binaries.

## Verification

Final offline build completes without warnings or errors. Thirteen integration checks pass against actual production core/store/pipe components:
correlated exchange, original result for a duplicate, conflicting identifier, invalid message, denied route, absent record, oversized-frame refusal,
listener survival, persisted read after restart, duplicate after restart, second-host refusal, no hidden host errors and busy refusal.
Only the busy case uses a paused responder to control concurrency; it still uses the real SQLite store.

The actual Windows executable also passes separate-process tests: tray host startup, client message, host restart, exact persisted reply and the specific unavailable-or-timeout error after shutdown.
The staging regression proves identical content is reused and rebuilt output can be staged without replacing a locked running version.
Final artifacts are `local/apps/agent-relay/tests/64091d93d3764573acc2323c61038dc5/`, including integration.log, process-smoke.log and processes/process-smoke.json.
The final runtime fingerprint is `4F4BEC9AB3BF64BAE71583699FA5EA70FE1421343F7962E048070529F8F632CC`.

Shared verification passes documentation links/anchors/index reachability, evidence references, PowerShell syntax, nine routing cases, seven installation cases and environment restoration.
Historical POC layout inspection still passes without executing WSL or a model. Doctor reports qualified tools, application source-present and Ready=true.
Ready refers to development prerequisites/source, not to a live listener or full MVP completion.

## Windows lifecycle and visual-inspection limits

A custom WinForms property triggered the designer serialization analyzer; removing unnecessary state resolved it without disabling the analyzer.
An old application reply is cleared when a new UI send begins, so an earlier response cannot appear to confirm a failed new send.

The computer-use skill selected one Agent Relay window, but state capture returned "Computer Use app approval timed out".
No screenshot, layout judgment or human-equivalent button interaction is counted as verified. There was no bypass through another UI automation API.
The functional tray process, client exchange and restart checks are independent of that unsuccessful visual inspection.

Restricted process inspection did not see the launched PID1676. Later permitted inspection proved it was still alive and locking compiler DLLs.
The original PID was stopped only after exact executable and command-line validation; the development data directory was retained.
Runtime staging resolves the rebuild problem rather than routinely killing an open app. Current run checks the process exit code.

## Size, cleanup and remaining boundary

Targeting framework-dependent win-x64 reduced a runtime copy from about 34.5 MB to about 2.5 MB by excluding unused platform-native binaries.
The previously qualified .NET runtime remains an external development prerequisite; this is not a self-contained distribution or installer.
Obsolete test runtime copies were removed only after normalized-path and permitted process checks. Current tools retain cached packages and test diagnostics for reproducibility.
Resource and cleanup records are under `local/apps/agent-relay/reports/first-slice/`.
Final retained runtime copy is 2,537,225 bytes including its integrity marker. Obsolete runtime copies totaling 39,589,982 bytes were removed.
Generated source-project build directories total about 38.1 MB, test builds 34.9 MB, qualification builds 34.7 MB and the reusable NuGet package cache 58.3 MB.
These measurements include generated binaries and caches, not just handwritten source. Final permitted process inspection found no owned application still running.

The first UI has no history browser, project editor, MCP shim or WSL adapter. Only the configured local demo route is available.
Current-user/elevation pipe restrictions are enforced by the Windows API; different-account negative testing was not performed.
Only normal completed local exchange persistence is qualified; external command ambiguity, agent permissions and crash recovery remain later work.
The next workstream needs a concrete scope and separate GO. Earlier WSL qualification observations remain historical and were not rerun.

## Objective consumption

The tracked objective is complete. Final returned usage is 104,342 tokens and 3,313 seconds (55 minutes 13 seconds).
These are the objective's measured counters, not a financial cost estimate. No external AI agent was invoked.
