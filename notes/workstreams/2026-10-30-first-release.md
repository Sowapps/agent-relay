# First private release

**Utility:** retains authorized release scope, checkpoints and recovery information.  
**Usage:** read before resuming; publishing is authorized only to the private sowapps/agent-relay repository for v1.0.0-rc.1.

**State:** completed and remotely verified.  
**Objective:** prepare reusable version/package/publication tooling; test the Windows x64 archive independently of the checkout/private SDK; initialize/review source control if absent; create private sowapps/agent-relay and publish tested prerelease v1.0.0-rc.1 with archive/checksums/source. Preserve live relay scope and existing configurations. No global software/install/startup change, no paid agent or WSL task.

Authorization: Florent accepted private sowapps/agent-relay and v1.0.0-rc.1 and said « GO pour préparer le script, tester l’archive et publier la release. Enchaîne et documente. » Creation, source push/tag and release upload are included. Only missing authentication/organization authority requires human action.

Start2026-10-30 07:32:56UTC /08:32:56Europe/Paris; shared quota45percent. Tracked goal inspected: historical unrelated blocked goal still occupies the slot (145775tokens/901seconds, not this task). Maintain scoped objective here rather than falsely completing the old goal.

Plan: verify GitHub account/repository access and current layout; choose portable self-contained Windows package and app-local data, document prerequisite WebView2; one release/version source and guarded idempotent scripts; validate extracted executable/UI/MCP/new-project administration without checkout paths; audit source allowlist for local data/secrets before first commit; create private repository, push reviewed source/tag and publish verified prerelease/assets; verify remote result and record limits/costs.

Checkpoint: git.exe and gh.exe installed; no GitHub connector tool available, use existing CLI. Existing live host PID57468/data v1-qualification must remain untouched.

## Packaging checkpoint

GitHub authenticated Cartman34 has admin membership of Sowapps; private https://github.com/Sowapps/agent-relay created successfully, origin configured. Local git init main done, no commit/push/tag/release yet. Global Git/MCP settings unchanged. No secrets detected in candidate source scan; 315initial source candidates, none >1MB; local/.codex/IDE/bin/obj ignored.

Version.props1.0.0-rc.1 imports into all projects, MCP reports actual informational SemVer. Portable Windows self-contained .NET10.0.14, distinct packages.win-x64.lock.json alongside development locks. release.ps1 supports Lock/Package/Verify/Tag/Publish, exact source/ZIP/visual checks, private-repo check and no silent tag/asset overwrite. package-release.py includes source-input/file manifests, SHA256SUMS, third-party licenses/metadata and stable ZIP ordering. Documentation and package launchers updated; portable initialize is a native executable mode that exports only an existing authenticated non-admin scoped connection. No global settings, SDK/Python or PowerShell script needed for Windows startup/MCP.

Initial publication build refused release RID against development locks; separate release locks fixed it. First extracted test proved this system's Windows PowerShell AllSigned restriction: replaced proposed PS initializer with native executable initialize and simple .cmd launchers, without bypass/policy edits. Another verifier compared CRLF literally; normalized documentation text comparison. Failed artifacts/hosts retained/stopped, never counted as passes.

FINAL_ARTIFACT_DIRECTORY local/apps/agent-relay/releases/v1.0.0-rc.1/86c4183558574f8abd7b130a1fa6f141. ZIP51965779bytes, expanded128154686bytes, SHA2566ee55d778cdcd5acf78d36dc0193d33c8622061bcb553fa9ffa9355d4ebb1fdf. Exact extracted Verify passes: no global runtime vars, nonexistent DOTNET_ROOT, System32-only PATH; two preserved projects, authenticated scoped MCP, five resources exact content, SemVer,24tools, foreign-project refusal, missing/admin UUID export refusal without pointer mutation, restart/duplicate owner. No WSL/model.

Full dev.ps1 test passes (integration, native Windows process smoke, no native agent) artifacts tests/6374561abf554f48bf1aa6c40d938130. tools/verify.ps1 passes after excluding generated bin/obj docs and adding missing mcp-tools contract heading. Live-schema docs check5resources/7examples/24tools passes releases/docs-rc1-check.

Own GUI test launched exact extracted executable PID47804, title Agent Relay - RC1 archive verification with180second lifetime. ComputerUse external capture timed out on app approval; no unrelated screen captured. Reusing internal render harness in own-render-probe: exact archive app/core/infra DLL hashes checked, only test harness DLL/deps/runtimeconfig added to separate copy. Running monitor-render on extracted data, output FINAL_ARTIFACT_DIRECTORY/render; inspect before recording visual-verification.json. Private SDK hosts verifier only; extracted app/MCP independence already passed. Do not claim external capture succeeded.

Next: finish internal capture/inspect and record exact-archive visual result; ensure test host exited; inspect staged source/commit; release.ps1 Tag then Publish with FINAL_ARTIFACT_DIRECTORY; download/verify remote assets and privacy/tag; close record with observed consumption. If any package source input changes, repackage/reverify rather than waive the source-hash gate. Preserve live user app57468.

## Ready for source commit and publication

Exact archive online internal rendering passed. Initial render completed after the short-lived visual test host expired, so it correctly showed unavailable; not treated as healthy proof. Retried with the owned host alive for the whole capture, terminated in finally: render-online shows both test projects and green relay; general image visually inspected. visual-verification.json matches the exact ZIP SHA. No desktop access is needed; external ComputerUse approval expired and no capture from that tool is claimed. Application runtime/MCP independence and actual controls from the archive were tested separately. All test hosts stopped; production scope unchanged.

## Completion10:06Europe/Paris

Published https://github.com/Sowapps/agent-relay/releases/tag/v1.0.0-rc.1 in the confirmed private repository. Source commit/tag317269bd286fb0499f1b2dfb5b0671b25d040ef2. Audited318staged source files: no local data, credentials, IDE/MCP configuration, SDK/cache/bin/obj; no token/private-key pattern. Git whitespace check accepts intentional Markdown breaks and preexisting terminal blank lines without changing qualified package source. Source/tag pushed without force; release is published prerelease, not draft, with ZIP and SHA256SUMS.txt.

Retried download from GitHub into remote-verification and confirmed byte SHA256 equals qualified ZIP6ee55d778cdcd5acf78d36dc0193d33c8622061bcb553fa9ffa9355d4ebb1fdf;51965779bytes (about52MB decimal),128154686expandedbytes. publication-verification.json records actual asset/privacy/tag checks. No installation/global config/WSL/model. Existing live user relay not replaced. No human action needed for GitHub; earlier external ComputerUse app permission was not used and no desktop-control permission is required for the internal renderer.

Scoped manual objective complete. Approximately94minutes wall clock (07:32:56→09:06UTC), shared quota45→45percent as displayed/rounded account-wide; no precise per-task token counter available because historical blocked goal occupies the API slot, no reset used. This duration includes the external capture authorization timeout and independent packaging diagnostics; it is not CPU/test duration. Report is an observation awaiting operator validation. A following documentation-only commit records the publication; immutable release tag stays on317269b. No next workstream inferred.
