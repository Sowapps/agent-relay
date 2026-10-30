# Isolated .NET qualification

**Date:** 2026-10-28  
**Status:** measured results awaiting operator validation; not promoted to operator evidence.

## Scope

The explicit installation GO is retained as evidence `2026-10-28-go-authorizes-isolated-toolchain-installation`.
The tracked objective covers isolated installation, reproducibility, real development-tool qualification, documentation and resource reporting.
No global installation, service, external model request, WSL operation or Agent Relay application implementation occurred.

## Locked inputs and installation

`tools/toolchains/windows-x64.json` locks SDK 10.0.403 Windows x64 and netcoredbg release 3.2.0-1092.
Checksums came from [Microsoft release metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) and the
[upstream debugger release](https://github.com/Samsung/netcoredbg/releases/tag/3.2.0-1092).
Publisher URLs and full hashes are versioned with the installer. The lock SHA-256 is `763998D519B24E753BCFC368BC5DEA48FDD9B6471571C42E5490CC2F74E03BE6`.

The initial restricted-shell DNS failure did not establish a network outage. Authorized execution accessed official metadata and downloads successfully.
The first primary installation, including transfer, checksum verification, extraction and per-file hashing, took 40.50 seconds.
An installation report and full payload file hashes are retained under ignored `local/toolchains/`.

| Package | Download bytes | Extracted payload bytes |
| --- | ---: | ---: |
| SDK | 300,745,255 | 807,619,073 |
| Debugger | 3,524,161 | 8,822,880 |
| Total | 304,269,416 | 816,441,953 |

The download is about 290.2 MiB and the primary payload about 778.6 MiB. Reports, markers, probes and caches add overhead measured separately.
Host prerequisites already present are PowerShell 7.6.6 and Python 3.11.9; they were not installed by this lot.
The installed SDK reports runtime 10.0.14. The debugger binary identifies itself as `NET Core debugger 3.2.0-1 (9744e1f, Release)` within release 3.2.0-1092.

## Real qualification

The SDK compiles the isolated console fixture without warnings/errors. The JSON message round trip and nominal self-test pass.
The DAP driver binds the source breakpoint on line 27 of the copied Program.cs, stops there, reads `input.Id = "probe-001"` and `input.Body = "ping"`,
steps, continues and receives exactly the expected `ack:ping` reply with target exit 0. The debugger exits 0.
Module events identify System.Private.CoreLib under the explicitly selected SDK installation, ruling out silent reuse of the primary runtime during reproduction.

A separate Windows Forms fixture compiles with the included Windows Desktop SDK/runtime. A real message loop runs, and Shell_NotifyIcon reports successful add and remove operations.
This verifies the desktop primitive and cleanup, not the layout, interactions or reliability of a future Agent Relay application.
Each fixture executes locally without external packages, AI models or WSL.

## Reproduction

A second installation was created from an absent directory at `local/toolchains/reproduction/clean-20261028/`, using only verified cached archives and `-Offline -SkipConfigure`.
Its first extraction/file-hash pass took 17.90 seconds. The later idempotent verification pass took 8.39 seconds.
The full console/debugger/desktop qualification also passed using that copy's own tools and own report directory.
The active main-tool configuration was not replaced. The reproduction uses the existing host PowerShell/Python prerequisites; it is not a clean-machine OS installation test.

## Incidents and fixes

The first debugger attempt under restricted execution ended while starting the target. The same driver passed with execution that permits debugger control.
This difference supports an execution-context issue; the precise native failure cause was not independently isolated.
The failed attempt's cleanup also hit a closing-pipe race; cleanup now tolerates that race without hiding the original diagnostic.

The SDK's initial first-use step attempted global-tools PATH integration and printed that it failed. Explicit `DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false` prevents that behavior thereafter.
All subsequent SDK processes use a private CLI home, private caches/temporary files, disabled telemetry, and disabled persistent build-server reuse.
Process environment values are restored after normal and failing callbacks. Originally absent variables need PowerShell NullString when passed to .NET setters; a plain null produced empty variables.
The same explicit null handling is used for atomic report replacement.

## Targeted verification

- Nine routing cases pass across two isolated fixture applications, including required application selection for setup.
- Seven installer cases pass: offline cold cache, nominal install, idempotence, incomplete payload recovery, bad checksum, ZIP traversal and concurrent installer refusal.
- Environment tests pass for normal callback, failing callback, complete process restoration and unchanged user/machine PATH during the helper calls.
- Four report cases pass: matching inputs, stale source, explicit failure and interrupted JSON.
- Documentation links, anchors, headers, index reachability, evidence references and PowerShell syntax pass shared verification.

An additional real online-cache recovery check preserved a deliberately damaged debugger cache, downloaded the small official archive again, verified it and installed it in an isolated test directory.
The active main configuration hash remained unchanged. The duplicate payload/archive were removed after retaining the result and installation report.

## Final state and cleanup

Final main qualification started at 15:42:44 UTC and finished at 15:42:50 UTC (6.47 seconds).
Final reproduction qualification started at 15:42:51 UTC and finished at 15:42:57 UTC (5.86 seconds).
Both report schema 1, all qualification flags true, actual host/tool versions, the inspected values and the correct own-runtime module paths.

The active report is `local/toolchains/agent-relay/qualification.json`; its final probe is `probe-cb5fe08231fe4abda5522609011fdb26`.
The reproduced report is under `local/toolchains/reproduction/clean-20261028/qualification/`; its final probe is `probe-335328942acf4dbe82735a1c594f4e7f`.
Copies of both reports and the initial primary installation report are retained in `local/verification/toolchain-20261028/`.
Doctor reports `QualificationStatus=qualified`, `ToolchainReady=true` and `ApplicationStatus=not-implemented`. Its product readiness remains false.

After checking resolved cleanup targets and confirming that no process used them, the second tool copy and online-recovery duplicate were removed.
Cleanup reclaimed 830,055,823 bytes. Historical probe files, debugger traces and reports remain; the reproduction tool paths in those historical reports are intentionally no longer installed.
The primary installation and verified archive cache remain ready for reuse and offline reinstallation.

Measured retained toolchain/archive/probe directories total 1,126,379,637 bytes (about 1.13 GB / 1.05 GiB), excluding small independent test fixtures and source files.
This includes 817,706,681 bytes of primary tools plus installation markers, 304,269,416 archive bytes and about 4.4 MB of probe/report artifacts.
Available space after cleanup was 45,901,320,192 bytes (about 42.7 GiB). Disk-space changes from other activities are not attributed to this lot.
Exact directory counts, cleanup targets and byte measurements are retained in `local/verification/toolchain-20261028/{resources,cleanup}.json`.
Final native command checks confirm doctor exits 2 for the absent product while reporting its toolchain qualified; build/test/run exit 3 for missing application sources.
No process using the experiment's installed dotnet/netcoredbg paths remained after qualification and cleanup.

Fixtures do not fabricate production build/test success. Application build/test/run remain unavailable because the C# application does not exist.
The qualification report is accepted only when schema, required checks and input fingerprint match; a source/tool change requires a fresh qualification run.

## Objective consumption

The tracked objective is complete. Its returned final usage is 82,281 tokens over 1,820 seconds (30 minutes 20 seconds).
These are the objective's reported metrics, not a monetary cost estimate or a count of external-model calls; no external AI agent was invoked.

## Next workstream boundary

This lot qualifies the selected development tools and desktop primitive. It does not qualify application SQLite bindings, MCP implementation, named-pipe transport or WSL integration in C#.
Those must receive focused checks before application integration. Earlier Python/agent qualifications remain historical measurements and were not repeated.
The next workstream needs its own concrete scope and explicit GO; installation authorization does not authorize product implementation.
