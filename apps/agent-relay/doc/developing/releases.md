# Build and publish a release

**Utility:** defines the repeatable portable-package, verification, tag and private publication process.  
**Usage:** obtain explicit destination/version/publication authority, then use release.ps1 from a qualified source checkout.

## Version and build ownership

Version.props is the single application SemVer source, imported by all C# projects. Assembly/file numeric versions are explicit there; MCP serverInfo reports the informational SemVer. A release candidate uses a prerelease tag such as v1.0.0-rc.1. Never move an existing published tag to different code.

The supported first target is Windows x64, self-contained and untrimmed. The ZIP includes app-local .NET runtime, native SQLite/WebView loader, presentation assets, WSL helper scripts, public guides, initializers and third-party notices. WebView2 Runtime remains external. There is no installer/global SDK, Python launcher, automatic startup registration or client settings rewrite. Native WSL preparation still needs its documented Python/WSL/client prerequisites.

Source uses ordinary packages.lock.json; the autonomous release uses separate packages.win-x64.lock.json files and an explicitly fixed runtime framework version in release.ps1. Run Lock only for a deliberate dependency change, review the graph and commit the lock files. Package always restores locked dependencies using the project-private cache. A new runtime security release requires updating the fixed runtime version, regenerating locks and publishing a new package.

```powershell
./apps/agent-relay/tools/release.ps1 -Action Lock   # Only when intentionally changing the release graph.
./dev.ps1 test -App agent-relay -Offline
./tools/verify.ps1
./apps/agent-relay/tools/release.ps1 -Action Package
```

Package returns a fresh RELEASE_DIRECTORY under ignored local/apps/agent-relay/releases. Preserve it. The ZIP has stable entry ordering/timestamps, a file-hash manifest and source-input hashes; do not assume bit-for-bit compiler reproducibility across a different SDK/toolchain. No source history, application data, credentials, caches, private configuration or SDK is bundled.

## Verify the exact archive

```powershell
$releaseDirectory = '<exact RELEASE_DIRECTORY>'
./apps/agent-relay/tools/release.ps1 -Action Verify -Directory $releaseDirectory
```

Verify extracts into a new directory containing spaces, checks every file hash and starts the actual packaged executable with nonexistent DOTNET_ROOT and PATH restricted to Windows System32. Windows initialization/MCP do not use PowerShell execution-policy exceptions. Scripted project-request generation alone uses the caller's already installed PowerShell7.2+, separately from end-user startup.

The check adds two local-only projects through the packaged administrative route without dropping the first, exports a scoped connection, reads all five resources with actual stdio MCP, checks server SemVer and the24tool inventory, refuses foreign-project history and traversal resources, then restarts and tests duplicate-host ownership. It starts no WSL/native agent/model. Verification stops only its own child host, even on failure; it preserves artifacts and refuses overwriting a previous extraction.

Also launch the exact extracted app with --show, explicit test --data-dir and a unique --window-title. Capture only that returned application window, visually inspect actual rendering and quit the owned test instance. Store visual-verification.json beside verification.json with passed=true, matching archiveSha256, the observed window/capture identifier and inspected result. This file records a real observation, not a replacement for looking at the window. Locked desktop or capture refusal is a real gate: do not claim visual qualification without an actual successful render/capture.

## Tag and publish

Review git status/diff, source file inventory and secret/data exclusion before the initial source push. .codex/, IDE settings, local/, bin/, obj/ and caches are ignored. The repository must already exist and be private; creation is a separate explicit authorized first-time action. Git credentials and organization permission are not provided by the script.

Commit the reviewed source and documentation. Then:

```powershell
./apps/agent-relay/tools/release.ps1 -Action Tag -Directory $releaseDirectory
./apps/agent-relay/tools/release.ps1 -Action Publish -Directory $releaseDirectory -Repository sowapps/agent-relay
```

Tag verifies clean source, matching version/source-input hashes and both exact-archive checks; it is idempotent only if an existing tag already points to HEAD. Publish additionally verifies repository privacy, pushes main and the exact tag, then creates a GitHub release with ZIP/checksums and the reviewed release-notes.md body. A version containing a prerelease suffix uses --prerelease; a final version without that suffix uses --latest and is not marked as a prerelease. No force push, tag replacement or silent published-asset overwrite is used. On an unknown upload outcome, inspect the existing remote release and hashes before deciding on recovery; do not blindly recreate it.

Download the published asset into a separate local verification folder and compare its SHA256 with the qualified archive. Confirm repository privacy, tag commit, prerelease state and asset names. Keep dated measurements in notes/observations and runtime artifacts in local/. Publication outcome may be recorded in a later documentation-only commit; the release tag stays on the qualified source commit.
