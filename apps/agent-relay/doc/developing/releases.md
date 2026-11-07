# Build and publish a release

**Utility:** defines the repeatable portable-package, verification, tag and authorized publication process.  
**Usage:** obtain explicit destination/version/publication authority, then use release.ps1 from a qualified source checkout.

## Version and build ownership

Version.props is the single application SemVer source, imported by all C# projects. Assembly/file numeric versions are explicit there; MCP serverInfo reports the informational SemVer. A release candidate uses a prerelease tag such as v1.0.0-rc.1. Never move an existing published tag to different code.

The supported first target is Windows x64, self-contained and untrimmed. The ZIP includes app-local .NET runtime, native SQLite/WebView loader, presentation assets, WSL helper scripts, English/French starting READMEs, public guides, initializers and third-party notices. Package-local guide links resolve inside the archive; contributor references point to the repository. WebView2 Runtime remains external. There is no installer/global SDK, Python launcher, implicit startup registration or client settings rewrite. The human can opt into Windows startup in Réglages → Général. Native WSL preparation still needs its documented Python/WSL/client prerequisites.

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

The check adds two local-only projects through the packaged administrative route without dropping the first, exports a scoped connection, reads all five resources with actual stdio MCP, checks server SemVer and the26tool inventory, refuses foreign-project history and traversal resources, then restarts and tests duplicate-host ownership. It starts no WSL/native agent/model. Verification stops only its own child host, even on failure; it preserves artifacts and refuses overwriting a previous extraction.

For portable content rendering, run `render-release-check.ps1 -Receipt <qualify/result.json>`. It rechecks the archive checksum, extracts to a fresh test directory and loads the packaged application assemblies and assets. Only the test runner is added. The application captures its own WebView with synthetic data. Inspect the generated images and then store visual-verification.json beside verification.json with passed=true, the matching archiveSha256 and the inspected capture paths. The receipt is limited to application content rendered by WebView2. Portable startup and host behavior are qualified separately by Verify.

## Tag and publish

Review git status/diff, source file inventory and secret/data exclusion before the initial source push. .codex/, IDE settings, local/, bin/, obj/ and caches are ignored. The existing GitHub repository `sowapps/agent-relay` is the explicit target. The script accepts either public or private visibility and reports the verified value. Creation and visibility changes are separately authorized actions. Git credentials and organization permission are not provided by the script.

Commit the reviewed source and documentation. Then:

```powershell
./apps/agent-relay/tools/release.ps1 -Action Tag -Directory $releaseDirectory
./apps/agent-relay/tools/release.ps1 -Action PlanPublish -Directory $releaseDirectory -Repository sowapps/agent-relay
./apps/agent-relay/tools/release.ps1 -Action Publish -Directory $releaseDirectory -Repository sowapps/agent-relay
```

Tag verifies clean source, matching version/source-input hashes and both exact-archive checks. An existing tag must already point to HEAD. PlanPublish verifies the same preconditions as Publish, including the exact GitHub identity, the single Git push target, the main/tag commit and the absence of the remote release. It reports the destination, visibility, tag, commit and archive checksum using read-only commands. `test-release-publication.ps1` runs this entry point against local command stubs.

Publish atomically pushes main and the exact tag, then creates the GitHub release with ZIP/checksums and the reviewed release-notes.md body. RC versions use --prerelease and stable versions use --latest. On an unknown upload outcome, inspect the existing remote release and hashes before deciding on recovery.

Download the published asset into a separate local verification folder and compare its SHA256 with the qualified archive. Confirm repository visibility, tag commit, prerelease state and asset names. Keep dated measurements in notes/observations and runtime artifacts in local/. Publication outcome may be recorded in a later documentation-only commit; the release tag stays on the qualified source commit.

## Contract version discipline

Only stable X.Y.Z and RC X.Y.Z-rc.N are supported (evidence `2026-11-02-mcp-stable-rc-versions`). Maintain one current RC line for qualification and the published stable independently. Every newly deployed MCP contract revision increments the RC/stable version, never just a hidden fingerprint. Unrelated internal commits can retain the contract version with a distinct build generation. The MCP initialize/status surfaces expose the actual version. Candidate metadata fingerprints tool schemas, public envelope revision and packaged public guides; deployment retains a version-to-contract ledger and refuses a changed contract under an already-deployed version before stopping the host. Promotion/rollback must preserve that ledger; do not erase it to bypass the gate. No tag/GitHub release is implied by a local RC deployment.
