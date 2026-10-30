# Building and verifying the application

**Utility:** documents the reusable commands and checks for the current C# application slice.  
**Usage:** use from the repository root after the isolated toolchain has been installed and qualified.

## Entry points

```powershell
./dev.ps1 doctor -App agent-relay -Json
./dev.ps1 build -App agent-relay -Offline
./dev.ps1 test -App agent-relay -Offline
./dev.ps1 run -App agent-relay -Offline
```

Omit Offline only when restoring locked dependencies from the official NuGet source is intended. Offline restores use the existing private package cache and clear network sources.
Normal restores retain NuGet auditing; offline mode disables that network-dependent audit and is not a vulnerability scan.
Package versions are central in Directory.Packages.props; source-adjacent packages.lock.json files retain transitive versions and content hashes.
Normal builds restore in locked mode. A deliberate dependency change requires updating and reviewing the lock files first.
The current Windows executable targets win-x64 and uses the qualified shared runtime; it does not copy unused native binaries for other operating systems into its execution directory.

Run opens the French project-monitoring window and notification icon. The command waits until Quitter or an application failure, propagating its exit code.
Runtime output is copied once per content fingerprint into ignored `local/apps/agent-relay/run/`; running binaries do not lock the compiler's output directory.
Old runtime versions are retained until explicitly cleaned after confirming no process uses them. Do not replace a running instance silently.

## Tests

The integration executable reuses the actual core, SQLite store and Windows named pipes. It covers the nominal exchange, duplicate/conflicting identity,
invalid input, denied route, absent record, malformed frame and listener recovery, restart persistence, second-host refusal and a busy use case.
The small paused responder only controls concurrency in that last case; it does not replace the persistence or transport tests.

The process smoke runs the actual Windows executable as a tray host, sends through a separate client process, restarts and reads the persisted exchange,
then verifies an absent host produces failure. Its bounded lifetime is test instrumentation; no Windows service or automatic startup is created.
Test artifacts use unique persistent folders under `local/apps/agent-relay/tests/`. A process exit or successful write alone is not counted as correlated delivery.
The durable-work suite also checks intent, pending duplicate, conflict, busy/capability denial, interrupted unknown state without replay and completed-result persistence.
Real acceptance phases live in tools/mvp-acceptance.py. Their paid-agent phase is guarded by exclusive markers and requires its own bounded authorization; ordinary test never invokes WSL agents.
SessionScenarios covers independent-session admission, targeted interruption, durable metadata/permission guards, cursor retrieval and transfer-path refusal. `session-acceptance.py` qualifies dedicated native conversations through real MCP; its `agents` and `resume` phases have exclusive paid-intent markers. `transfer-acceptance.py` qualifies a model-free file round trip and collision/path guards.

`publish-local.ps1 -Show` stages and preflights a copied database/configuration before replacing only the exact recorded idle Agent Relay process. It retains offline rollback data and failed migrated files, confirms authenticated local health, then publishes the runtime pointer. DataDirectory/ReportDirectory allow an isolated qualification. The retired -CodexGlobal switch refuses before any runtime change: use register-codex.py separately with the reviewed final fragment, backup and semantic comparison. Neither command grants authorization by itself.
`sync-session-adapters.py --data <explicit-owned-data>` refreshes adapters only in named-session roots verified against their UUID/user owner marker. It does not restart agents or replay prompts. Use it for deliberately selected owned qualification sessions, not unrelated native sessions.

## Dependency gate and resolved issues

The tools/qualification fixture exercises the pinned SQLite native provider and a CurrentUserOnly pipe before application integration.
SQLite connections use WAL and synchronous FULL. Package restore and all CLI caches remain under the existing isolated toolchain environment.

Pipe writes and reads need concurrent consumers: awaiting a write before starting its read can block on zero-buffer Windows pipes.
The gate uses concurrent operations; the application has independent host/client readers and writers with cancellation deadlines.
Custom WinForms control properties must declare their designer serialization behavior when needed; remove unnecessary state instead of suppressing the designer analyzer.

The compiler cannot overwrite a DLL loaded by an open application. Stage runtime output before launch; do not solve this by routinely killing user processes.
Process inspection must use the execution context that owns or can inspect the launched process. A restricted lookup returning no result does not establish process exit.
Python's SQLite connection context commits/rolls back a transaction but does not close the connection. Publication uses contextlib.closing explicitly: a lingering read connection can prevent Windows from archiving a failed database during rollback. Snapshot the protected credential files together with their hashed database bindings so a copied preflight or restored database can authenticate correctly.
Native startup can stop at a login-expired or update-selection screen even after the message MCP initialized. Classify it as attention-required. Disable Codex startup update checks only in the relay-managed invocation; do not install/update global clients while trying to reconnect a conversation.

## Current limits

The local echo is a deterministic regression adapter. The application uses the [MVP command/agent workflow](mvp-architecture.md), with native clients and a separate inbound message capability.
There is no agent scheduler, automatic prompt replay or deployment installer.
Named pipes enforce the current Windows account/elevation boundary; no different-account negative test has been run.
The development host requires a Windows desktop and the configured isolated runtime. A standalone apphost double-click without that runtime environment is not a qualified distribution path.
