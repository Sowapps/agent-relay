# Multi-application preparation

**Date:** 2026-10-28  
**Status:** measured observations awaiting operator validation.

## Authorized scope

The explicit GO covers reversible multi-application organization, shared conventions and entry points, isolated .NET qualification and a conditional application skeleton.
The skeleton is conditional on successful compilation, execution, nominal testing and actual debugging.
No automatic installation, service, external model or WSL project access is included. No WSL command or model was executed during this lot.

## Organization

Application source, documentation and history moved under `apps/agent-relay/`. Shared development rules and commands remain at root.
Operator evidence remains canonical in `notes/evidence/`. Existing proof bodies are preserved byte for byte; only the index receives new entries.
POC data moved under `local/apps/agent-relay/`; Python source resolves the repository registry marker rather than assuming its old depth.

The pre-migration archive is `local/migrations/20261028-123828/before-layout.zip`.
Its SHA-256 is `2BBE5752661274B620EFA43ECB2E0C677D7E267BCC8D1DCDB554B2215E686C03`.
The same directory holds the original 55-file hash manifest, move list and source file map. It is ignored local data, not a committed backup.

For rollback, first save any subsequent work, extract the archive into a separate staging directory and compare it with the manifests.
Restore mapped files deliberately, including application local data using the move list. Do not blindly overwrite the current repository or remove shared files added afterwards.

## Qualification result

PowerShell 7.6.6 runs on .NET 10.0.12. No .NET SDK was found in PATH, common Windows install locations, environment roots or SDK registration.
Visual Studio Installer exists but reports no installed Visual Studio instance. No usable debugger was found through the inspected command and common installation paths.
This is an inventory result, not an exhaustive filesystem search or a debugging demonstration.

The console probe and qualification runner are prepared. The runner exits 2 and records all execution/debugging checks false because the SDK is missing.
No C# compilation, nominal execution, application test or breakpoint was achieved. No SDK or debugger was installed.
The C# application skeleton and deterministic application message path were consequently not started.

## Checks and limitations

Eight subprocess routing checks pass against two isolated fixture applications: build, test, run, combined doctor, missing selection, unknown identifier, invalid identifier and an escaping registry path.
These establish routing and error contracts only. They do not establish application readiness.
Six real application entry-point checks confirm help exits 0 while doctor, qualify, build, test and run exit 2 for the missing SDK.
The captured results are in ignored `local/verification/entrypoint-results.json`; the qualification result is in `local/toolchains/agent-relay/qualification.json`.
Multiple PowerShell executables were discovered; selecting the first result explicitly fixed an invalid invocation caused by concatenated executable paths.

The shared checker validates local links, heading anchors, document headers, index reachability, evidence references and PowerShell syntax without accessing external WSL references.
The historical follow-up link to a renamed MVP section was repaired. Existing proof content is kept unchanged.

Migration audit verifies all 55 original archive entries against their original hashes, finds all 55 files at mapped destinations and all 15 original proof bodies byte-identical.
Thirteen original files differ intentionally: indexes/rules, repaired follow-up link, current POC path reference, two Python runners and generated .NET output exclusions.
The archive hash still matches. Application local data contains 24 POC, 7 qualification and 9 storage-check diagnostic files.
The POC layout inspection parses all eight Python source files and loads the two runners with native process execution forbidden.
Both resolve their new persistent local directories and referenced source files correctly. No live WSL behavior is inferred from this inspection.

## Design issues kept explicit

The qualification build must run beside the copied `global.json`: specifying a project path alone does not select that directory's SDK pin.
The runner now changes directory for that operation and restores it afterwards. This branch remains unexecuted until an SDK is installed.
Doctor's readiness remains false until actual qualification and application behavior exist, even if an SDK is later discovered.

## Next scope to authorize

Prepare an isolated Windows x64 SDK 10.0.403 under `local/toolchains/dotnet/`, without global PATH changes or services.
Use the official [SDK downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) and preserve downloaded archive provenance and checksum.
Qualify a command-line debugger, preferably netcoredbg for direct agent control, against the actual .NET 10 probe before any application code.
Its upstream [release](https://github.com/Samsung/netcoredbg/releases/tag/3.2.0-1092) provides no local compatibility proof; do not assume it works from its existence.
The upstream [asset list](https://github.com/Samsung/netcoredbg/releases/expanded_assets/3.2.0-1092) names `netcoredbg-win64.zip` (3.36 MB),
with SHA-256 `3c410a45fa502415203a94fcb88654af65bf8e3dac158a5527a722e7a6b9274a`.
It was identified but not downloaded. The proposed isolated debugger directory is `local/toolchains/netcoredbg/3.2.0-1092/`.

If that route fails, investigate the official [Microsoft C# debugger route](https://code.visualstudio.com/docs/csharp/debugging) as a separate tooling choice.
Do not install a broad IDE or switch application technology automatically. Resolve the prerequisite first, then return to the conditional skeleton.
