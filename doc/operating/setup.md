# Isolated tool setup and recovery

**Utility:** explains installation, reproduction, cleanup and incident handling for the Windows toolchain.  
**Usage:** follow after an explicit installation GO; run from the repository root in PowerShell 7.2 or later.

## Prerequisites and ownership

The pinned toolchain targets Windows x64. Installation needs HTTPS access to the official publishers and at least 3 GiB of available disk space.
Qualification also needs an existing working Python 3.9+ interpreter and a Windows desktop session for the shell-icon fixture. No Python packages or IDE are installed.
PowerShell and Python are host prerequisites, not bundled parts of this installation.

| Path | Role |
| --- | --- |
| `tools/toolchains/windows-x64.json` | Versioned versions, URLs, algorithms, hashes and provenance |
| `local/tmp/downloads/` | Verified archive cache; persistent repository storage, independent of operating-system temporary folders |
| `local/toolchains/dotnet/<version>/` | Extracted SDK |
| `local/toolchains/netcoredbg/<version>/` | Extracted debugger |
| `local/toolchains/cache/` | CLI home, NuGet caches, temporary files and bundle extraction |
| `local/toolchains/agent-relay/` | Inventory, qualification report, isolated probes and debugger transcripts |

Use explicit executable paths. The tool environment applies only for the operation and restores original values afterwards, including originally absent variables.
Global-tools PATH integration and CLI telemetry are disabled; there are no global PATH changes, services or automatic startup entries.

## Normal operation

```powershell
./dev.ps1 setup -App agent-relay
./dev.ps1 qualify -App agent-relay
./dev.ps1 doctor -App agent-relay -Json
```

Setup downloads only locked publisher ZIPs, verifies the full archive checksum before extraction and records a hash for every extracted file.
It stages an installation before publishing it. Repeating setup verifies and reuses a matching installation. Qualification clears package sources for its dependency-free fixtures.
SDK selection runs beside the application's copied `global.json`; a project-path argument alone does not select that SDK pin.

Doctor exposes `ToolchainReady` separately from application readiness. Application sources can remain unimplemented while the development toolchain is qualified.
Build/test/run continue to report that absence and never fall back to running a Python prototype.

## Reproduce without network

```powershell
./tools/install-toolchain.ps1 -Offline -InstallRoot local/toolchains/reproduction/fresh -SkipConfigure
./tools/qualify-dotnet.ps1 -App agent-relay `
    -ToolchainConfig local/toolchains/reproduction/fresh/toolchain.psd1 `
    -ReportDirectory local/toolchains/reproduction/fresh/qualification
```

Choose a previously absent reproduction directory. The second installation uses the verified archive cache and explicit paths to its own copied tools.
`-SkipConfigure` preserves the active tool selection. Each installation retains its own generated toolchain configuration and size/provenance report.
The qualification report is machine-specific; reproduce the checks instead of copying a green report between machines or tool locations.

## Incidents

| Situation | Action |
| --- | --- |
| Network unavailable | Preserve partial download, retry the same official URL once; then report the network failure. Offline setup works with a valid cache. |
| Missing/corrupt cache in offline mode | Fail before extraction. Do not silently enable downloads. |
| Corrupt cache in online mode | Preserve the rejected archive separately and fetch the locked publisher URL again. |
| Interrupted/modified installation | Verify recorded payload hashes, stage a fresh copy and preserve the previous directory under `.recovery/`. |
| ZIP path escapes staging | Reject extraction, even when the supplied archive hash matches its lock. |
| Another installer is active | Fail clearly. Exclusive file handles release when the process exits, including a crash; a remaining lock filename is not a stale lock. |
| Less than 3 GiB free | Stop before download/extraction. Clean only explicitly identified experiment-owned files. |
| Build/debug/desktop failure | Retain probe and transcript, keep qualification false, isolate the failing gate. Never infer compatibility from executable presence. |
| Qualification input changes | Doctor marks the previous result stale. Rerun qualification against the changed inputs. |
| Partial qualification JSON | Doctor reports invalid-record; rerun qualification to replace it. |
| Failure outside authorized scope | Preserve diagnostics, continue independent work and propose a concrete next scope. |

## Cleanup

Keep the archive cache while offline reproduction is useful. It can be removed after review if downloading again is acceptable.
Remove reproduction tool copies after retaining their reports; retain primary tools for development. Inspect `.recovery/` and partial files before deleting them.
Before recursive deletion, resolve each absolute target and verify that it is the intended child of repository `local/`. Never follow a computed path outside that boundary.
