# First application slice

**Date:** 2026-10-28  
**State:** completed; tracked objective closed.

## Authorized scope

Authorization is retained as evidence `2026-10-28-first-application-slice-and-reusable-tools`.

Florent approved the proposed 30–60 minute first C# application slice with an explicit GO after adding DRY/SOLID and reusable-tool requirements.
Deliver a neutral C# core, minimal Windows tray application, a correlated local message round trip and durable storage.
Qualify required communication/storage first, reuse the existing isolated toolchain/environment and build/test/run/diagnosis entry points.
No external model, WSL access, global configuration, service, automatic startup or full MVP delivery is authorized.

## Design and acceptance

Use Windows named pipes restricted to the current user, a SQLite adapter and a deterministic local demo endpoint explicitly identified as non-AI.
The neutral core owns contracts, validation, correlation and the local exchange use case; persistence and transport remain adapters.
Prefer one infrastructure project over speculative packages. Windows owns composition, tray and CLI client modes; integration checks reuse these real components.
Nominal acceptance: actual tray host receives a local client message, replies with the same request identity, persists it and returns it after a host restart without duplication.
Important failures: invalid message, same identifier with different content, unavailable host and malformed transport data must produce explicit failure rather than false delivery.
Keep response serialization, pipe naming, validation and process/tool setup in one owner each. Do not introduce generic frameworks without concrete reuse.

## Incident policy and checkpoint

Toolchain was qualified previously, including real debugging and desktop shell operations. Main tools are configured in `local/toolchains/dotnet.psd1`.
SDK 10.0.403/runtime10.0.14, netcoredbg3.2.0-1092, PowerShell7.6.6 and Python3.11.9. Reuse `tools/ToolchainEnvironment.psm1` for private caches and process-only environment.
First qualify a pinned Microsoft.Data.Sqlite package and current-user pipe round trip in an isolated fixture. Use official NuGet sources and local package caches.
If a gate fails, isolate and resolve it before adapter implementation; preserve diagnostics. No paid agents or WSL retries.
Dependency gate passed: Microsoft.Data.Sqlite10.0.14 (SQLite3.53.3), reopened durable SQLite DB and CurrentUserOnly Windows pipe.
The fixture initially awaited pipe write before read, hitting the zero-buffer pipe backpressure timeout; concurrent write/read fixed the fixture before integration.
Core, Infrastructure and Windows projects exist, plus a small executable integration suite. Package versions are central and per-project locks saved.
App builds without warnings. Core/store/pipe integration passes correlation, idempotence, conflict, invalid input, denied route, absent record, restart recovery and second-host refusal.
`dev.ps1 build/test/run -App agent-relay [-Offline]` now routes through reusable AppCommands; `tools/relay.ps1` provides explicit send/read clients.
Actual Windows tray/client process smoke passes send, persisted read after host restart and unavailable-host refusal.
Latest artifacts: `local/apps/agent-relay/tests/c1275008f562465799c86ff1e9d6973e/`.
WinForms designer warning for an unnecessary custom property was resolved by removing the property and using CloseReason.UserClosing; do not suppress that analyzer globally.
Architecture ownership is saved in application doc/developing/application-architecture.md. No WSL/model call or global config change.
Next: visual UI verification using computer-use skill, malformed-frame/busy checks, final reusable tooling checks, documentation and objective metrics/closure.

Visual control attempt: computer-use helper selected one Agent Relay window but state capture returned "Computer Use app approval timed out".
No visual layout or button interaction is counted as verified. Do not bypass this with a different UI automation mechanism.
Restricted process inspection could not see PID1676; permitted inspection later confirmed it was still alive. Do not infer process exit from restricted visibility.
PID1676 locked compilation outputs, so it was stopped only after exact executable/command-line validation; durable data retained.
Launch now waits for exit/checks code. RuntimeStaging creates one verified content-addressed local copy per built output, shared by normal run and process smoke, preventing rebuild locks.
Actual short-lived tray process smoke remains passing. Next: finish malformed-frame/busy checks, rerun test, document and measure; no WSL or model work.

Latest integration adds oversized-frame rejection/listener recovery and a controlled busy responder; all 13 checks pass.
Full offline test with staged runtime passed; artifacts `local/apps/agent-relay/tests/0a2ca7a1476d4e15bfec9a9a944a57a3/`.
Added reusable staging regression for unchanged-content reuse and rebuilt output while an old staged file is locked; integrate into next test run.
Application using/development documentation is saved and linked; shared documentation/syntax/routing checks reached PASS, remaining shared fixture results pending cell87985.
Remaining: staging regression and last full tests, user-facing docs/current status, measured observation and goal closure. UI visual/accessibility inspection remains unverified due tool approval timeout.

Final C# boundaries are stable: core contracts/orchestration; infrastructure SQLite/pipes/local demo adapter; Windows UI/composition/client; one integration executable.
Windows output is framework-dependent win-x64, reducing staged payload from34515612 to~2537153 bytes; source compilation stays separate from running copies.
Final functional test at18:31 passed all13 integration cases, staging regression and actual tray/client/restart/unavailable-host smoke.
Artifacts `local/apps/agent-relay/tests/b67810d598e24cc58c30fc7775058f55/`; logs are now captured automatically by Test-App for future repeated verification.
Current doctor returns Ready=true, ToolchainReady=true, source-present and qualified. Package locks exist for all four projects and the dependency gate.
Remaining: one final logged test for the logging change, shared checks, measurements/cleanup and historical report; no more application feature scope.

Final logged functional run passed at18:38: artifacts `local/apps/agent-relay/tests/64091d93d3764573acc2323c61038dc5/`.
All13 integration checks, staging regression, actual tray/client/restart and precise unavailable_or_timeout checks pass. No more feature changes.
Final runtime fingerprint4F4BEC9AB3BF64BAE71583699FA5EA70FE1421343F7962E048070529F8F632CC; old copies were cleaned at18:36, latest superseded small copy may still need cleanup.
Historical report is application notes/observations/2026-10-28-first-local-application.md; current docs describe using, architecture, build/test/run and known boundaries.
Remaining only final cleanup/process verification, link check, resource/report update and goal closure with actual metrics. Visual/accessibility inspection remains unverified due approval timeout.

## Completion checkpoint

Implementation, full logged tests, shared checks and cleanup are complete. Current artifacts: tests/64091d93d3764573acc2323c61038dc5 under the application's ignored local data.
Current runtime fingerprint4F4BEC9AB3BF64BAE71583699FA5EA70FE1421343F7962E048070529F8F632CC, 2537225 bytes including marker; obsolete runtime cleanup39589982 bytes.
Permitted final process inspection found no owned application running. SQLite data and all useful diagnostics retained.
User entry: `./dev.ps1 run -App agent-relay -Offline`. SDK/dependency gate and current application tests pass; full MVP/WSL integration is not delivered.
Visual layout/button inspection is explicitly unverified because the computer-use app approval expired. Do not claim a screenshot or UI action succeeded.
Final documentation/shared checks passed. The tracked objective is complete:104342 tokens,3313 seconds (55 min13 s).
No work remains in this lot. The outcome and metrics are retained in the application observation. No new implementation scope is authorized without another explicit GO.
