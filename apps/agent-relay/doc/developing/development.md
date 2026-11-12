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
`sync-session-adapters.py --data <explicit-owned-data> --session-id <UUID>` refreshes adapters only in selected named-session roots verified against their UUID/user owner marker. Repeat `--session-id` for an explicit set; omission selects all retained sessions and requires authorization for that wider scope. Stop the selected relay through its approved procedure first. Synchronization holds the common exclusive host lock while the C# `maintenance-idle` command checks durable work and the selected files are copied. It refuses an active host or active work. The registered runtime supplies this command by default. For older installations, `--runtime-pointer <prepared-current-pointer>` explicitly selects a validated guard without changing the registered runtime. A missing or damaged database and a migrated scope are refused. It does not restart agents or replay prompts. Packaged updates apply to newly provisioned roots; already provisioned roots retain their copied adapter until explicitly refreshed.

`python -B apps/agent-relay/tools/test-native-lifecycle.py` checks lifecycle admission without WSL or models: current and legacy session layouts, matching provisioning identity/user and rejection before tmux access for profile roots, project workspaces, mismatched identities and unsafe markers. Native lifecycle uses the private session runtime root, independently of the assigned project workspace.

With authorization for isolated WSL copies, run `python -B apps/agent-relay/tools/test-session-adapter-sync.py --runtime-pointer <current-pointer> --distribution <distro> --user <user>`. Add `--legacy-runtime-pointer <older-pointer>` to verify the explicit guard override. The current runtime must expose both `maintenance-idle` and `wsl-maintenance`. The check uses the actual C# maintenance commands and Linux file replacement on unique current/legacy session roots, verifies refusal before WSL when work or a host is active, and checks that the selected session alone changes. It retains a JSON report under the developer test directory and removes only its marked native copies.

`wsl_maintenance.run_script` invokes the trusted local `wsl-maintenance --data-dir <absolute-data>` CLI with an explicit distribution, user, script, input and deadline in its JSON input. C# `WslMaintenance` delegates process handling to `LocalProcess`, acknowledges Python startup on stderr, and distinguishes `native_operation` from `native_start_unconfirmed`. The latter never proves that effects are impossible. The Python facade also measures total runtime wait separately from the native invocation. It preserves a structured diagnostic on failure; the synchronization fixture stores it in its result, including cleanup failures. A timeout is uncertain and is never replayed automatically. This CLI is local operator tooling and exposes no additional MCP tool or project permission.

With native-test authorization, `test-wsl-maintenance.py --runtime-pointer <current-pointer> --distribution <distro> --user <user>` checks Unicode round-trip, a native failure, a controlled timeout with partial stdout/stderr, and a fictional absent distribution. It launches no agent. `AgentRelay.Tests.dll process-check` separately exercises process deadlines including blocked stdin and caller cancellation without WSL. Process diagnostics decode UTF-8 and Windows UTF-16; legacy single-byte diagnostic output is retained using a Latin-1 fallback. Nonzero and timeout diagnostics retain at most 1800 characters per stream. Normal successful output remains complete.

## Dependency gate and resolved issues

Legacy technical profile refresh uses `sync-wsl.py --settings <scope/relay-config.json> [--runtime-pointer <current-pointer>]`. It shares named-session maintenance runtime selection, the C# idle guard, exclusive host lock and WSL transport. It rejects helper symlinks and publishes each helper atomically. `test-maintenance-callers.py` qualifies this path on an isolated WSL profile. Native storage qualification now requires `test-native-storage.py --runtime-pointer <current-pointer> --distribution <distro> --user <user> --report <developer-result.json>` and uses the same C# transport for its owned copy/commit/rollback check.

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
| `qualify [--baseline-directory <absolute-directory>]` | Package the clean RC commit offline, verify its exact ZIP and scoped MCP cold startup, then optionally rehearse update/rollback against the selected verified archive in the same report. |
| `rehearse --receipt <absolute-result.json> --baseline-directory <verified-release-directory>` | Recheck the commit and both ZIP identities. Exercise the deployment transaction and injected rollback using two staged archives and synthetic data. The running baseline must support the admission barrier. Add `--legacy-baseline` to test refusal of a live legacy replacement and verify an update after the fixture host has stopped. |
| `status [--operation qualify] [--json]` | Read the newest retained report for each requested operation. Show its result, duration, source commit and report path. Report unreadable records separately. This command reads delivery reports only. |

Every invocation reports its result under `var/apps/agent-relay/isolated-delivery/`, with phase logs, durations, attempts, Git identity, disk space before/after and candidate size and checksum when applicable. Automated checks use zero model calls. Manual activity during a development run is not measured by the wrapper. Current account consumption and optional per-process CPU/memory are separate measurements and are not inferred from elapsed time. A retained development scope can be examined independently of subsequent runs.

Pilotage AP uses a protected installation under `var/apps/agent-relay/ap-stable/`. Generic deployment also protects the former `var/apps/agent-relay/v1-qualification` scope. Development, qualification and rehearsal use no stable data by default. For an authorized passive comparison of the actual named registration and selected configuration hashes, pass `--observe-stable <absolute-codex-config>`. Rehearsal always takes an explicit verified baseline archive. See [Pilotage AP stable operation](#pilotage-ap-stable-operation) for its separate deployment path.

An online replacement owns `promotion.lock` for the selected data scope. The outgoing host must acknowledge that new work is suspended and that already admitted requests and active work have drained. If the host does not support that acknowledgement, plan a separately authorized maintenance window and upgrade from a confirmed stopped host. The transaction releases its marker after success or verified rollback. A retained marker after an interruption or unconfirmed recovery requires inspecting the owned transaction and host before resuming.

For one combined archive and update qualification, use `python -B apps/agent-relay/tools/isolated-delivery.py qualify --baseline-directory <absolute-verified-baseline-directory>`. The baseline is a local verified archive copy. The command requires a clean identified RC commit, verifies the new package, checks scoped MCP cold startup, then runs the shared update/rollback rehearsal. Candidate/baseline identity, phase logs, durations and the update receipt appear in one qualification result. Add `--legacy-baseline` only for a baseline predating the live admission barrier. The standalone `rehearse --receipt <qualification-result> --baseline-directory <directory>` remains available for a previously qualified candidate.

## Pilotage AP stable operation

Use `python -B apps/agent-relay/tools/pilotage_stable.py <action>`. The workflow uses the existing `agent_relay_personal_assistant` table as a scoped witness for the shared stable relay. All project connections that already point at that installation follow an in-place update with their own grants. The development MCP stays isolated. A stable rollout requires separate authorization and an idle maintenance window.

| Action | Effect |
| --- | --- |
| `status --config <file> [--receipt <result.json>]` | Inspect the named registration. With its receipt, check a fresh MCP connection and distinguish it from an existing-chat confirmation. |
| `prepare --config <file> --release-directory <dir> --version <X.Y.Z> --project <UUID> --endpoint <UUID> --thread <chat-id>` | Check the verified archive and its SHA-256, copy the stopped and idle source using SQLite backup, and qualify a temporary host on the copy. The original registration is preserved. |
| `switch --receipt <result.json> --apply` | Recheck that the source, staged installation and named route still match the receipt. Start the candidate, verify the scoped project and rights, and atomically change the named registration. |
| `upgrade --config <file> --release-directory <dir> --version <X.Y.Z> --project <UUID> --endpoint <UUID> --thread <chat-id> --apply` | Standard update for the retained portable installation. Use the existing maintenance, snapshot and rollback procedure to switch only the executable behind the shared stable launcher. Verify the scoped MCP, the unchanged registration and the existing startup preference. The receipt also reports the known legacy startup entry. |
| `confirm --receipt <result.json> --proof <file>` | Record evidence obtained from the owning chat after the switch and return a targeted resumption message. |

Use the package directory containing the reviewed stable ZIP, `SHA256SUMS.txt` and the portable verifier's matching `verification.json`. Confirm that its checksum matches the published archive. Use IDs from the approved project and existing participant. Use `prepare` and `switch` only for a one-time move from a stopped legacy scope. Use `upgrade` after the portable installation has the retained launcher. Run it with the same Windows account that holds the credential. The workflow preserves the database, project configuration, grants and protected credentials. It never rewrites WSL paths or resumes native sessions.

The successful switch prints `chatCheck`. In that exact Codex chat and using the named connection, request `list_projects` and read `agent-relay://docs/tools`. Compare the observed project UUID and the SHA-256 of the UTF-8 documentation text with LF line endings. When `chatCheck` includes a version, also verify it with `get_connection_status`. On older contracts, the documentation fingerprint can distinguish two connections only when their documents differ. Copy the matching fields of `chatCheck` into the proof JSON and add `"listProjectsSucceeded": true` only after reading the chat's actual reply. The command does not reload Codex or send messages. The owning Codex operator sends the returned resumption message to that chat after confirmation. A fresh process alone leaves the existing chat unconfirmed.

The registration write is the cutover point. If a check or write fails before it, the tool stops only the candidate it started and preserves the previous registration. If interrupted near cutover, inspect the retained receipt and named registration with `status` before any retry. After cutover, preserve the selected scope and any new history while recovering the client. Restoring an older scope requires a new stopped-scope review and must never silently route Pilotage AP to development. Logs and timings stay next to the receipt under the managed installation directory.

Windows sign-in startup is a preference per data scope. An in-place upgrade keeps that scope and verifies its enabled or disabled state before and after. For a one-time move, run the selected installation's `AgentRelay.Windows.exe startup-transfer --data-dir <stable-data-directory> --previous-data-dir <reviewed-legacy-directory>` for a read-only preview. Only with explicit startup-change authorization, repeat it with `--apply`. The command verifies the current entry before retiring the previous one and attempts to restore the prior preference on failure. The [portable recovery procedure](../using/portable-release.md#recover-an-unavailable-relay) also covers agents without an MCP connection.

Rehearse on a fictional installation with `test-pilotage-stable.py --release-directory <verified-stable> --upgrade-release-directory <verified-RC>`. Add `--guard-runtime-pointer <prepared-current-pointer>` when the baseline archive predates the C# `maintenance-idle` command. Use `test-mcp-startup.py --release-directory <verified-RC>` for the portable cold-start, concurrency, credential and maintenance checks.

## Current limits

The local echo is a deterministic regression adapter. The application uses the [MVP command/agent workflow](mvp-architecture.md), with native clients and a separate inbound message capability.
There is no agent scheduler, automatic prompt replay or deployment installer.
Named pipes enforce the current Windows account/elevation boundary; no different-account negative test has been run.
The development host requires a Windows desktop and the configured isolated runtime. A standalone apphost double-click without that runtime environment is not a qualified distribution path.
