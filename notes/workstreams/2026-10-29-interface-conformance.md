# Interface conformity correction

**Date:** 2026-10-29  
**State:** complete; local dependency approved, implementation/test/docs and guarded qualification publication finished.  
**Objective:** correct UI01–UI08 and QA01 against the reviewed HTML reference, qualify actual layout/scroll/overlay behavior with matched data, preserve real monitoring/MCP, test and document without stopping between lots.

## Authorization

Florent accepted: « Oui. Le périmètre couvre tous les écarts recensés, avec la maquette comme référence : structure des vues, composants, filtres, défilement, navigation et vrais tiroirs en superposition. Je commencerai par vérifier les causes et la capacité du rendu actuel à reproduire fidèlement la maquette. Puis je corrigerai les composants communs et les vues, avec comparaison visuelle à données et dimensions identiques, avant les régressions fonctionnelles et MCP. » Explicit response: « GO ».

Scope includes corrections, tests, current docs and guarded local qualification publication. No unrelated desktop control, native agent/session/model call, WSL command, global configuration, automatic startup, translation or silent runtime installation. Preserve the reviewed prototype as the reference. The six original screenshots and full register are in apps/agent-relay/notes/proposals/2026-10-29-interface-conformance-corrections.md.

## Accounting and continuity

Start14:16:56Europe/Paris (13:16:56UTC), account84% used, both reset credits intact. create_goal attempted; refused due unrelated unfinished blocked integration objective, frozen145775tokens/901seconds. Do not mark that unrelated goal complete or attribute its counters here. Track dated checkpoints, timing and shared-account quota instead. Existing reset policy only near exhaustion, preserve second credit, stay below5% after any authorized reset; no reset now.

Read this record and get_goal before resuming after compaction. Current native qualification host metadata: local/apps/agent-relay/reports/native-interface/host.json, mirrored v1-qualification/ui-host.json; verify ownership, do not trust remembered PID.

## Audit and alternatives

Source confirms filters/tabs docked outside the main scroll region, so their ordering cannot match Fil de travail. DockStyle.Right changes page geometry instead of overlaying it. Metrics use a fixed-height TableLayoutPanel; repeated control-tree replacement during AutoScroll/layout can displace content. These choices require structural correction, not spacing patches.

Option A: rebuild native layout and custom drawing for every component. No new dependency, but duplicates the HTML design implementation, introduces typography/layout/timeline/overlay fidelity work and leaves two renderers to maintain. Technically possible; the former approximate port is not acceptable proof.

Option B, recommended subject to explicit dependency approval: keep C# host/core/infrastructure/tray and passive observation, render production HTML/CSS using WebView2, reusing reviewed components/styles instead of redrawing them. Installed runtime directories155.0.4283.62 and156.0.4314.50 found; no SDK currently in private NuGet cache. Official stable Microsoft.Web.WebView2 package1.0.4314.35 is9.36MB on NuGet; runtime is not bundled or installed. Verify actual loader/runtime, own data folder, offline content, capture, footprint and lifecycle before adopting. Private package/cache/content only; never silently install a missing runtime. A new renderer does not authorize browser navigation, arbitrary JS-to-host actions or exposing credentials.

Sources: https://packages.nuget.org/packages/Microsoft.Web.WebView2 and https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution. Runtime/SKD tradeoffs require one explicit approval because the preceding proposal did not authorize a technology switch. Asked asynchronously; wait for actual response before dependency download/restore or renderer integration. Continue renderer-independent mapping, reference inventory and comparison preparation.

## Planned lots and estimates

Indicative engineering ranges, not a token budget or consumption measurement: reference/component matrix15–25min/2–4k tokens; rendering/scroll/navigation foundation20–35min/4–7k; real-data adapter and bounded bridge25–45min/6–10k; drawer/actions20–35min/4–7k; matched visual/functional verification and publication20–40min/4–8k. Reassess after structural proof; native rewrite alternative is likely more expensive to maintain. Keep user updates concise.

Nominal: identical reference fixture, normal/compact app viewport, same route/filter/scroll/detail; snapshot structure and pixels agree before accepting. Real data preserves exact scope and native uncertainty; failure alert explains file-too-large and keeps Examiner inline. Important failures: long/empty/large histories, live refresh while scrolling or typing, overlay not changing background geometry, unsupported connectors, untrusted messages/HTML, unavailable relay/renderer and clean app exit.

Next: preserve source/reference snapshots; map all view/component/drawer contracts and real DTO fields; build paired fixture/export strategy. Once dependency approved, qualify tiny isolated WebView host before replacing the production renderer. No production rollout until actual matched visual review and functional checks pass.

## Checkpoint 15:04 Europe/Paris

Dependency approval remains unanswered; no SDK download, package reference, runtime installation, native renderer integration or deployment occurred. The live application remains unchanged.

Prepared renderer-independent PresentationSnapshot mapping with project-scoped task IDs, connector/connection descriptions, permissions intersected with session-owner grants, preserved historical execution paths/arguments, independent messages and no credentials. Expanded monitoring projection checks pass; offline build passes with zero warnings/errors. Added a human file-too-large explanation.

Prepared reusable HTML/CSS/components/controller under Presentation/Assets, detached from native host pending approval. The frozen reviewed mock SHA256 remains 5DE37F5B43AA25C036F23FCC62F2F3FBDF1C6059A18D43F260A368B2B81871C9. prepare-conformance.py generates test-only paired pages/fixtures in ignored local storage, never production fake data. Browser comparison covers 15 views at 1120x728 and 960x648; 60 captures, 22/30 pairs initially pixel-identical, others differ in truthful diagnostic/settings labels or message-read marking (subsequently corrected, compact recaptured). Browser layout checks found no clipped metric card, missing content or missing expected overlay. These are browser observations, not C# qualification or operator-validated evidence.

Initial compact reference captures accidentally retained width1120 despite requested960; actual innerWidth check caught this. Retained invalid initial report separately, reran compact pairs serially in the same browser tab at verified960x648. Latest browser-comparison-checks.json contains corrected results. Do not cite initial report as passing.

Artifacts: local/apps/agent-relay/qualification/conformance/{comparison-cases.json,browser-comparison-checks.json,browser-pixel-comparison.json,reference-fixtures.json,*jpg}. Prior source snapshot local/apps/agent-relay/migrations/conformance-20261029/before.zip; reference.html frozen there. Six operator screenshots reviews/interface-conformance-20261029/pj1..pj6.png. No native agent/session/model/WSL command invoked.

Remaining independent checks: honest partial-history pagination and permission details; browser failure/untrusted-message cases; screenshot review; record reusable verification findings. Pending SDK approval is required before isolated WebView host qualification, actual C# integration, native visual comparison, full functional/MCP regressions and guarded publication. Do not report workstream complete.

## Checkpoint 15:04 Europe/Paris — independent work complete

Offline full test suite passes including WINDOWS_PROCESS_SMOKE and all integration/state/session/permissions/transfer tests; artifacts local/apps/agent-relay/tests/a42d9e4941b9413d80310759ea3c0d47. The smoke launches only isolated local test hosts; no native agent/model/WSL execution. Existing real MCP list_projects succeeded read-only from this conversation; this proves discovery remains available on the unchanged deployed build, not new-renderer integration.

Fixed clear-history action mismatch, included pending operations in running filters, exposed actual project permissions in advanced context, marked partially loaded operation history honestly and made load-more available even when local filtered rows are exhausted. Disabled projects expose no session controls. These preparation assets are still not integrated into the native application.

Browser failure scenario displays human file-too-large cause with inline Examiner; metric bounds pass. Untrusted HTML/script text is rendered as literal text, with zero injected image/script nodes. Advanced type filter selects one transfer; clearing restores three operations. Captures manually inspected for true overlay and inline alert button. Viewport override reset. Browser tabs are temporary and may be recreated from recorded URLs.

Current consumption observation: shared account84% to85% used, ~48minutes elapsed since13:16:56UTC. Integer percentages are coarse shared-account measurements, not exact task tokens. Two reset credits intact, none redeemed. Tracked objective remains unrelated/blocked with frozen145775tokens; do not report that as this task usage or complete it falsely. Workstream remains incomplete, waiting for explicit local WebView2 SDK authorization already requested. No package downloaded/referenced and no app published. Next action on YES: isolated runtime/capture/lifecycle qualification before native integration, then matched native screenshots, full regression and guarded qualification publication. On NO: reassess faithful native renderer without adding dependency.

## Resumption 18:22 Europe/Paris

Florent explicitly answered the dependency question: « Oui, ajout local uniquement ». Authorization covers the pinned official WebView2 SDK in project dependencies/private caches only; no global runtime install or Windows setting change. Resume the same authorized correction scope. Next: download pinned package, isolated own-window runtime/capture/lifecycle proof, then integrate production host. Existing tracked goal inspected and remains unrelated/blocked; manual workstream accounting continues.

## Checkpoint recorded at 18:30 Europe/Paris

Pinned SDK downloaded under local/tmp/webview2; SHA25690778DEB1B77E36292828B4B5833C9255C27533F40E0DDD55891AE718944EDC4, restored exclusively to local/toolchains/cache/nuget. Central package versions and lock files record1.0.4314.35. Isolated gate tools/webview-qualification passed using installed runtime156.0.4314.50: own capture960x648, true overlay, startup2864ms, browser process private38.8MB (not full process-tree memory), private browser-data9.7MB first run; browserPID exited after disposal. Capture qualification/webview-gate/own-view.png manually inspected. No global install. SDK unconditionally adds WPF reference; app-local Directory.Build.targets removes unused WPF binding before assembly resolution, not a warning suppression.

C# host integration underway: RelayWebView restricts navigation/resources to own virtual origin, denies permissions/downloads/popups/host objects, owns private presentation-cache; ProjectWindow uses passive source, stable snapshot version, refresh scope and native fallback. Old inaccurate ProjectWindow partial views/details/draw-to-bitmap verification removed (before.zip retained); reusable MonitorComponents used by other native diagnostic surfaces remains. New native conformance driver uses production RelayWebView/capture at matched viewports. Next compile/run paired30native captures and inspect. No publication yet.

## Closure 18:40 Europe/Paris

All UI01–UI08/QA01 correction criteria implemented and technically qualified, subject to operator visual acceptance. Thirty matched-size production C# captures pass, including explicit actual-size checks and overlay assertions. Additional cases pass: snapshot during search preserves focus/scroll, untrusted HTML is literal, empty settings render, offline diagnostic explains its cause. Complete local integration/regression and Windows process smoke pass (tests/834338b8ee6448598e254d9d6e528bc1). Real project rendering and interactive published-window alert/overlay/copy/Escape/project-summary/X-hide/same-host-reopen/Quit checks pass. Two passive MCP tools succeed from this conversation on the published backend, and discovery succeeds after final publication. No new native agent/model/WSL test.

Current Windows interface documentation and tooling README updated; reproducible check-interface-conformance.ps1 added and verified. Dated report apps/agent-relay/notes/observations/2026-10-29-interface-conformance.md is indexed. This report closes the scoped implementation objective; unrelated frozen goal-service item is not falsely completed. No required development lot remains in this scope. Operator acceptance is still an operator action, not fabricated evidence.

Final runtime and PID are read from local/apps/agent-relay/reports/interface-conformance/host.json and mirrored v1-qualification/ui-host.json; do not trust a remembered PID. Last deployment retains preflight/snapshot/rollback in its transaction directory. Application left open. Source snapshot and immutable mock retained; no global settings/startup install or normal-data rollout. One foreground-mismatched Computer Use capture after test windows changed focus was discarded without saving; target reactivated before more input. Durable proof uses own WebView capture.

Active time roughly65minutes across two segments, excluding approval wait. Shared-account readings84→85→43percent cannot supply a meaningful task delta; both reset credits remain, none used here. New tracked objective unavailable because the unrelated blocked goal occupies the only slot; its145775tokens/901seconds do not belong to this workstream. Manual scoped record now complete.
