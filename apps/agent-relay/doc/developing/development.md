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
Deployed runtime output is copied once per content fingerprint into ignored `var/apps/agent-relay/run/`; explicit test-runtime copies may stay in developer local/; running binaries do not lock the compiler's output directory.
Old runtime versions are retained until explicitly cleaned after confirming no process uses them. Do not replace a running instance silently.

## Tests

The Codex terminal adapter submits prompts as a bracketed paste followed by one Enter. A long burst of literal key events can leave text in the composer without creating a native turn. The durable intent marker still precedes terminal input and remains authoritative for no replay. A missing correlated reply keeps an uncertain outcome with code reply_unconfirmed. Inspect one retained request with tools/inspect-native-reply.py using explicit data, session and request IDs, and a private local output path. This reads the owned terminal and matching native transcript and receipt. It sends no terminal input.

Design-foundation checks are model-free: tools/test-design-contracts.py exercises the shared layout corpus/package roundtrip, typed native errors and exact host-identity selection; tools/test-native-lifecycle.py checks admission before tmux. After the normal build, run AgentRelay.Tests.dll design-contracts with the absolute tests/AgentRelay.Tests/Fixtures/native-layout.json path for the C# side of the same corpus and MCP name-schema parity. No native agent or WSL is started by these checks.

For actual stdio encoding qualification, seed a fresh explicit developer data directory using AgentRelay.Tests.dll design-fixture, then run tools/test-design-mcp.py with --runtime pointing to a prepared payload and --data to that fixture. It starts only its own relay host and MCP child, renames a retained closed fictitious session with Unicode through broker/worker/pipe, verifies exact retained text and shuts its children down. This is distinct from a real native lifecycle check and requires no model.

With explicit native-test authorization, tools/test-design-native.py uses a separately seeded fresh design-fixture, --runtime, --data, --distribution and --user. It prepares a uniquely marked private qualification workspace under the native layout, performs open/ready/close through MCP without a prompt, retains the result and never enters user projects. Use a new fixture after a failed attempt; inspect uncertainty before any native retry.

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
`sync-session-adapters.py --data <explicit-owned-data> --session-id <UUID>` refreshes adapters only in selected named-session roots verified against their UUID/user owner marker. Repeat `--session-id` for an explicit set; omission selects all retained sessions and requires authorization for that wider scope. It does not restart agents or replay prompts. Packaged updates apply to newly provisioned roots; already provisioned roots retain their copied adapter until explicitly refreshed.

`python -B apps/agent-relay/tools/test-native-lifecycle.py` checks lifecycle admission without WSL or models: current and legacy session layouts, matching provisioning identity/user and rejection before tmux access for profile roots, project workspaces, mismatched identities and unsafe markers. Native lifecycle uses the private session runtime root, independently of the assigned project workspace.

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

## Isolated delivery

Use `python -B apps/agent-relay/tools/isolated-delivery.py <action>` from the checkout.

| Action | Effect |
| --- | --- |
| `build`, `test` | Offline development build or repeatable integration suite using the local qualified toolchain. |
| `run` | Build and launch a fresh development instance with a synthetic project and a scoped `agent_relay_dev` MCP fragment. The phase log records both paths. Quit it from the application. Rerun for a fresh scope. The fragment is an export and never changes client settings. |
| `qualify` | Package the current clean RC commit offline and run the portable verifier on the exact ZIP. |
| `rehearse --receipt <absolute-result.json>` | Recheck commit and ZIP checksum. Exercise the actual deployment transaction and injected rollback on synthetic data from the verified archive. Use only the installed baseline binary, never its data or credentials. |

Every invocation reports its result under `var/apps/agent-relay/isolated-delivery/`, with phase logs, durations, attempts, Git identity, disk space before/after and candidate size and checksum when applicable. Automated checks use zero model calls. Manual activity during a development run is not measured by the wrapper. Current account consumption and optional per-process CPU/memory are separate measurements and are not inferred from elapsed time. A retained development scope can be examined independently of subsequent runs.

Pilotage AP currently uses the protected `var/apps/agent-relay/v1-qualification` scope. Development and candidate commands observe its identity and selected configuration hashes without writing to it. The local deployment transaction refuses that scope and overlapping paths. A stable deployment needs an explicit authorization and a separate promotion entry point. The portable verifier covers packaged startup and scoped MCP without model or WSL calls. Use the coverage matrix to plan separate native, GUI and fault checks before accepting a release.

## Current limits

The local echo is a deterministic regression adapter. The application uses the [MVP command/agent workflow](mvp-architecture.md), with native clients and a separate inbound message capability.
There is no agent scheduler, automatic prompt replay or deployment installer.
Named pipes enforce the current Windows account/elevation boundary; no different-account negative test has been run.
The development host requires a Windows desktop and the configured isolated runtime. A standalone apphost double-click without that runtime environment is not a qualified distribution path.
