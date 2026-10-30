# First private release qualification

**Utility:** retains measured packaging/publication results and their limits.  
**Usage:** consult when reproducing the RC1 release or assessing native/clean-machine qualification; not operator-validated evidence.

## Authorized delivery

Florent selected private sowapps/agent-relay, version v1.0.0-rc.1, and authorized scripts, archive tests, repository creation and release publication. The existing authenticated GitHub account had organization administration permission; no human authentication or global configuration action was needed.

Repository: https://github.com/Sowapps/agent-relay . Published prerelease: https://github.com/Sowapps/agent-relay/releases/tag/v1.0.0-rc.1 . Immutable source tag commit317269bd286fb0499f1b2dfb5b0671b25d040ef2. Source file inventory audited318files and excludes local runtime/configuration/history/credentials, IDE settings, build output, caches and SDK. No token/private-key pattern found. No force push/tag replacement or published-asset overwrite.

## Measured archive behavior

Windows x64 self-contained/untrimmed runtime10.0.14 uses its own locked release graph, independent of development lock files. Version.props owns SemVer and MCP serverInfo reports it. Native executable initialization replaces a proposed PowerShell script initializer after Windows PowerShell's AllSigned policy correctly refused that script; no policy bypass was performed. Initial RID lock refusal was fixed with separate packages.win-x64.lock.json, rather than weakening ordinary restores.

Final artifact directory: local/apps/agent-relay/releases/v1.0.0-rc.1/86c4183558574f8abd7b130a1fa6f141. ZIP51965779bytes, expanded128154686bytes, SHA2566ee55d778cdcd5acf78d36dc0193d33c8622061bcb553fa9ffa9355d4ebb1fdf. Runtime/licenses/third-party metadata, offline user guides, launchers and additive request example included. No selected data or SDK bundled.

Actual extracted executable runs with DOTNET_ROOT pointing to a nonexistent folder, dotnet/NuGet environment removed and PATH limited to Windows System32. Tests generated two real local-only project configurations using the packaged administrative route, preserved the first project, exported an existing authenticated participant-scoped connection and exercised exact executable stdio MCP. Five public resources read exactly; server version and24tools checked; foreign-project history, unknown resource and missing/admin connection export refused; existing pointer preserved on failure. Restart and duplicate-host reuse passed. The optional PowerShell7.2 request generator is test/admin tooling, not a Windows app/MCP startup dependency.

Full source application regression and Windows process smoke passed under tests/6374561abf554f48bf1aa6c40d938130. Shared workspace checks passed; generated bin/obj documentation is excluded from source-link validation. Fresh docs probe passed5resources/7schema examples/24tools, no model or WSL. All test hosts stopped, existing live relay untouched.

## Visual boundary

External ComputerUse capture waited for its app permission and timed out. It captured no desktop and was not counted as success. Existing internal render harness ran from a separate copy of the archive, checking exact app/core/infrastructure assembly hashes before adding only the test DLL/deps/runtimeconfig. First capture happened after the short-lived host expired and correctly showed unavailable. Final render-online ran with an owned extracted host alive throughout, verified the available state, and the general image was visually inspected: both test projects, complete metrics/filter/history blocks, no horizontal clipping, green relay. Host stopped in finally. Private SDK hosted only this verifier; actual executable/MCP runtime independence was separately proved. visual-verification.json binds this result to the ZIP SHA.

## Remote result and limits

Actual GitHub release is prerelease=true/draft=false and repository private=true. Remote tag peeled to the expected commit. ZIP and SHA256SUMS.txt were downloaded into a new verification directory; downloaded bytes/hash match the qualified archive and published checksum. publication-verification.json records this result. A subsequent documentation-only commit can record delivery without moving the release tag.

The prerelease is unsigned. Installed WebView2 remains a prerequisite; no clean-machine installation is claimed. Windows native WSL preparation additionally needs Python/WSL/tmux/supported native client and its login; this release task did not repeat paid native tests. MCP docs distinguish source bootstrap and portable startup. Native transcript import and additional provider/transport adapters are not claimed.

Scoped objective completed approximately94minutes wall clock, including diagnostics/authorization timeout. Shared usage indicator45→45percent at the observed rounded account-wide readings; no exact per-task token number and no reset. Historical blocked goal counters are not this workstream. Original explicit-go record: root notes/workstreams/2026-10-30-first-release.md.
