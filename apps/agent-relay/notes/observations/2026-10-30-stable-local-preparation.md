# Stable v1.0.0 local archive qualification

**Utility:** retains the exact final-version local candidate and qualification limits.  
**Usage:** review before authorizing stable delivery; observations are not operator-validated evidence.

Florent authorized preparation/test and minor corrections only. No commit, tag, push or GitHub release action occurred. Version1.0.0 is declared by the extracted MCP executable; functional app/native source and locked dependencies are unchanged from the RC2 source. Live user relay/data and earlier archives remain untouched.

Artifact directory: local/apps/agent-relay/releases/v1.0.0/49499945af4344c2a67ebc5461b149bc. ZIP51981782bytes, expanded128193407bytes, SHA256171d6bd1629f45c36e458b4aac1741cea7e7d2c41c67571f5dee2c2082bfbf87. Current README/guides/package text match source and local README links resolve. Stable notes and minor prerelease/article/spacing wording corrected.

The distribution script previously unconditionally selected --prerelease. Its actual local selection branch now produces --latest for1.0.0 and --prerelease for1.0.0-rc.2, tested without running any remote command. Installed gh help confirms those options. This affects future publication metadata, not application behavior. The initial branch-only test's child-scope assignment was corrected in the verifier; production logic was unchanged by that diagnostic correction.

Package/Verify passed exact extracted startup without global .NET paths, project preservation, scoped MCP/foreign-project refusals, missing/admin-connection export refusal, five updated resources,24tools, restart and duplicate ownership. Shared documentation/fixture checks passed. Exact archive assemblies were hash-checked in an internal-render probe with an owned extracted host alive throughout; layout passed and general screenshot was visually inspected (French UI, both projects, green relay, complete blocks). No desktop capture/control. Private SDK hosted visual verifier only, not archive startup. All own test processes stopped.

Reports: verification.json,final-verification.json,visual-verification.json,render/render-verification.json,review-summary.md; release flag proof under releases/stable-preparation. Git HEAD/tags stayed unchanged. Approximately5minutes, displayed shared quota47→47percent rounded, no exact per-task token counter because unrelated historical goal remains blocked; current scope closed in root notes/workstreams/2026-10-30-stable-local-preparation.md.

Unsigned package, existing WebView2; no clean-machine prerequisite install or additional model/WSL test claimed. Local stable archive awaits Florent validation before any delivery action.
