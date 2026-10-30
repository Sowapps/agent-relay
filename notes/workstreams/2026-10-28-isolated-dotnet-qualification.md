# Isolated .NET qualification workstream

**Date:** 2026-10-28  
**State:** work completed; final objective closure and consumption are reported to the operator.

## Authorization and boundaries

The authorization is retained as evidence `2026-10-28-go-authorizes-isolated-toolchain-installation`.

Florent approved the proposed 30–60 minute lot with:

> Assure toi bien sûr d'avoir toutes les infos conservées afin de ne rien perdre en cas de compression. GO

The preceding proposal covers a pinned manifest, reproducible isolated installation, real compilation/run/test/debug qualification,
reinstallation from an empty directory using retained archives, documentation and objective consumption reporting.
Archives belong in repository `local/tmp/downloads/`; tools and isolated caches belong in `local/toolchains/`.
No global environment changes, services, administrator installation, external model calls or WSL access. No application implementation in this lot.

## Existing state

Previous lot reorganized the repository for multiple applications. All 55 original files remain; 15 original proofs are byte-identical.
Root `doc/` owns shared rules; `apps/agent-relay/doc/` owns the application. Operator decisions are canonical at `notes/evidence/`.
English prose; preserve French quotations and the fixed bilingual glossary. Existing WSL experiments are historical and must not be rerun.
Read root AGENTS, development/documentation rules and application AGENTS before relevant edits.

SDK and debugger were not found; only PowerShell's embedded .NET runtime exists. `local/toolchains/agent-relay/qualification.json` reports false.
An isolated C# fixture exists at `tools/qualification/dotnet/`; its message round trip must not be counted as the application.
`tools/qualify-dotnet.ps1` currently stops at missing SDK and has no real debugger driver. Full qualification must remain false until demonstrated.
`dev.ps1` exposes doctor/qualify/build/test/run; application actions remain guarded. Shared routing fixtures pass eight cases.
Previous shell downloads failed DNS under restricted execution; test authorized network access before diagnosing the host as offline.

## Plan and incident policy

1. Read official release metadata, lock versions/URLs/checksums and capture provenance.
2. Implement installation through verified cached ZIPs, fresh staging directories and checked moves confined to the installation root.
3. Exercise missing/corrupt/incomplete installations and repeat installation without network from the verified archive cache.
4. Build/run/self-test the C# fixture; drive a real debugger, bind a source breakpoint, inspect input values, step and continue to the expected result.
5. Record actual sizes/time, run relevant shared checks, update current documentation and close the tracked objective with measured consumption.

Use only official download alternatives. Reject corrupt archives before extraction. Preserve incomplete state separately and never overwrite a verified installation blindly.
If space is insufficient, clean only explicitly identified experiment-owned files. Validate absolute paths before any recursive move/delete.
If a debugger candidate fails, diagnose it and try an authorized isolated alternative; a global IDE or technology change requires a new scope.
Continue independent work if a gate blocks. Never manufacture qualification, retry paid agents, or extend the GO into application development.

## Resume checkpoint

Goal started 2026-10-28 15:16:56 UTC. Read local rules. Network succeeds with authorized escalated execution.
Locked SDK 10.0.403 SHA-512 and netcoredbg 3.2.0-1092 SHA-256 from official metadata in `tools/toolchains/windows-x64.json`.
Installer is `tools/install-toolchain.ps1`; both packages downloaded, checksum-verified, extracted and fully file-hashed with installation markers.
Main SDK archive 300745255 bytes, extracted payload 807619073 bytes; debugger archive 3524161 bytes, extracted 8822880 bytes.
Local executable configuration is `local/toolchains/dotnet.psd1`; tool folders, reports and caches stay under ignored local storage.
SDK reports 10.0.403, runtime 10.0.14 and Windows Desktop runtime present. Build/run/nominal probe test pass.
First debugger attempt died after configurationDone; trace in `local/toolchains/agent-relay/probe-6fd2292d740c4346a329d97b18912b54/`.
Debugger adapter is `tools/qualification/debug_probe.py` (DAP, source breakpoint, evaluation, step, continue).
Fixed debugger cleanup so a closing-pipe race cannot mask the original failure. Need retry with permitted debugger execution and inspect real exit code.
SDK first run attempted global-tools PATH integration and failed under restricted execution; explicitly disabled it with DOTNET_ADD_GLOBAL_TOOLS_TO_PATH=false.
No global install, service, WSL or model call occurred. Qualification remains false until debugger success; production application absent.
Real debugger retry with authorized execution passed (source breakpoint, expression values, step, exit 0).
Main probe `probe-9dcafec063584dda8f5217c084d4ceac` also passes Windows message loop and real Shell_NotifyIcon add/remove.
Offline fresh installation under `local/toolchains/reproduction/clean-20261028/` reproduced all checks in its own probe/report directory.
Seven offline installer fixture cases pass, including corrupt archives, recovery and concurrent install refusal.
Environment restoration test found PowerShell binds null to empty .NET strings. Explicit NullString restores absent environment variables; verify that fix next.
`dev.ps1 setup -App agent-relay [-Offline]` is now the explicit install entry point. It requires app selection; other commands never install.
Environment restoration now passes after NullString fix, including failure callbacks and unchanged user/machine PATH checks.
Four isolated report checks pass: matching report, changed fixture => stale, failed report => not-qualified, truncated JSON => invalid-record.
Shared verification passes links/indexes/evidence/PowerShell syntax, nine routing cases, seven installer cases and environment restoration.
Current setup/reproduction/incident documentation is under `doc/operating/{setup,toolchain,entrypoints}.md`.
Added explicit Python 3.9+ host prerequisite; no Python installation or packages. SDK and debugger main/reproduction runtime module paths were inspected and are separate.
Qualification report now uses schema 1 and atomic publication; final qualification runs still needed after these source changes.
Final main qualification passed 15:42:44–15:42:50 UTC; final reproduction passed 15:42:51–15:42:57 UTC, including explicit own-runtime path assertions.
Main report matches current input fingerprint. Doctor returns ToolchainReady=true, qualified, debugger demonstrated, application not-implemented/Ready=false.
Online bad-cache recovery was also tested with the small official debugger archive; active configuration stayed unchanged.
After exact resolved-path and process checks, removed reproduction dotnet/netcoredbg and duplicate online-test payload/cache; reclaimed 830055823 bytes.
Kept reproduction reports/probes, primary tools and canonical archives. Retained measured dirs total 1126379637 bytes (~1.05 GiB), with ~42.7 GiB disk free.
Detailed outcome is `notes/observations/2026-10-28-isolated-dotnet-qualification.md`; raw report copies/resource/cleanup JSON are under `local/verification/toolchain-20261028/`.
Final shared checks and four qualification-record cases passed. Nine routing cases, seven installation cases and environment checks remain green.
Final native guards: doctor=2 (toolchain qualified, product absent), build/test/run=3 (not implemented). No owned dotnet/netcoredbg process remains.
Work and the tracked objective are complete. Final objective metrics: 82281 tokens, 1820 seconds (30 min 20 s).
No further action remains in this lot. Do not start application development without a new scoped GO.
