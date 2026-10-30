# Interface conformance correction

**Date:** 2026-10-29  
**Status:** implementation and local qualification complete; observations await Florent’s validation.  
**Utility:** records corrections against the six rejected screenshots and their technical verification.  
**Usage:** consult with the current Windows interface documentation and scoped workstream record.

## Authorization and implementation

The scoped correction GO covers UI01–UI08/QA01, tests/docs and guarded local qualification publication. Florent separately authorized the pinned WebView2 SDK with « Oui, ajout local uniquement ». No global runtime installation, Windows setting, startup mechanism, translation, WSL command, native agent/model request or unrelated application input was performed.

The former docked/auto-scroll WinForms monitoring composition was replaced by a C# host of the production HTML/CSS/components derived from the reviewed reference. The same implementation owns sidebar, section hierarchy, cards, filters, task/message/activity rows and overlay drawers. Core and infrastructure remain outside the rendering boundary. The old inaccurate view/detail/draw-to-bitmap partials were removed; native manual diagnostics retain their own existing controls. Historical pre-change source is retained in migrations/conformance-20261029/before.zip.

## Gap disposition

| Register | Implemented correction | Qualification |
| --- | --- | --- |
| UI01 | Metric cards use reviewed CSS geometry; no fixed-height native clipping container. | Bounds asserted at1120x728 and960x648; captures reviewed. |
| UI02 | Readable file-too-large cause, state/context and Examiner inside one alert. | Real retained oversized.bin refusal opened in published application; no invented success. |
| UI03 | Fil de travail contains feed selection, search and progressive filters. | Paired general/activity/message views; transfer filter/clear normal path. |
| UI04 | One page scroll region; drawer owns its own scroll; no nested native AutoScroll reconstruction. | Nonblank/overflow checks, initial scroll positions, snapshot/search stability. |
| UI05 | Project summary uses metrics, recent tasks, recent messages and compact context. | Fixture captures and actual Qualification A screenshot/interaction. |
| UI06 | Right overlay/backdrop with compact header, close control, content and footer. | All five drawer types at both sizes; actual alert drawer and Escape close. |
| UI07 | Reviewed sidebar identity, subtitle, icons, project initials/counts, selected state and bottom actions. | Fixture and real screenshots. |
| UI08 | Shared design tokens/components rather than per-native-control approximations. |30 matched-size native captures using one production renderer. |
| QA01 | Visual, native interactive and backend/MCP checks are separate. | Reports identify what each check proves; operator acceptance is not inferred. |

Truthful differences from fictitious data remain intentional: received counts instead of invented unread acknowledgements, unverified connector/native state, actual project permissions, instance scoped to its data directory, real diagnostic labels, and collapsed native administrative tools. Technical cleanup remains in the console. The retained test refusal remains visible. Historical data has not been erased to improve appearance.

## Results and artifacts

- Frozen reviewed reference SHA2565DE37F5B43AA25C036F23FCC62F2F3FBDF1C6059A18D43F260A368B2B81871C9 remains unchanged.
- Browser reference comparison:15 views at two sizes;24/30 pairs pixel-identical after correcting message-read state, remaining differences are explicit diagnostic/settings wording. Browser viewport mismatch was detected and corrected before citing those results.
- Production C# RelayWebView:30 native own-surface captures and checked actual viewport sizes, metric bounds, content/overflow and expected overlays. tools/check-interface-conformance.ps1 is the repeatable entry point. Search focus/scroll survives a real snapshot; untrusted HTML is literal text; empty configuration renders settings; offline diagnostics shows the actual retained cause.
- Offline build: zero warnings/errors. Complete integration/state/session/scope/permissions/transfer suite and WINDOWS_PROCESS_SMOKE pass; local/apps/agent-relay/tests/834338b8ee6448598e254d9d6e528bc1.
- Passive real-data rendering: local/apps/agent-relay/qualification/native-real/render. This is a separate test window using the production host/source; no agent calls. Fixture captures: qualification/native-conformance. These capture the WebView’s own surface rather than the desktop.
- Published runtime5C8B13854AAD9EC7E45854C75845F125F4C3298BEF65EDDB480269F25A6C3913, only v1-qualification data. Final transaction reports/interface-conformance/deployment-1b038947065f48dda06afeb80d3a11de contains preflight and rollback snapshot. No normal-data or global-client configuration rollout.
- Real published-window interaction via Computer Use: inline Examiner opens overlay; copy shows successful acknowledgement; Escape closes overlay; Qualification A summary matches composition. Alt+F4 hides window whilePID60216 survives; --show reopens that same host. Settings/Quitter terminatesPID60216; restarted qualification hostPID55884 is recorded in reports/interface-conformance/host.json and v1-qualification/ui-host.json. No notification-area shell gesture was needed or claimed.
- Actual MCP connection in this conversation: list_projects and list_results succeed against published backend; project scope remains Qualification A. After explicit Quit/restart, list_projects succeeds again. No native prompt, command or inbox import was used to test UI compatibility.

Computer Use capture can return foreground pixels after another owned test window changes focus. One mismatched result was discarded without saving; the exact owned application was reactivated and recaptured before further input. Own-surface captures are the durable visual artifacts. Do not cite automatic UIA text immediately after a click as settled state when the image shows a later update.

## Dependency footprint and reproducibility

SDK1.0.4314.35 official package SHA25690778DEB1B77E36292828B4B5833C9255C27533F40E0DDD55891AE718944EDC4 is under local/tmp/webview2. Private NuGet cache expansion is60,046,196bytes; deployed application changed from3,437,144 to5,300,357bytes. The existing installed runtime156.0.4314.50 is shared, not copied or installed. The isolated first gate took2864ms, created9,659,312bytes of private browser data and observed38,772,736private bytes in the browser process alone; this is not whole-process-tree memory or a performance guarantee. Gate files: qualification/webview-gate/result.json and own-view.png. BrowserPID9416 exited after disposal. App-private browser cache can grow; it contains no authoritative project history.

Central package pin, lock files, private toolchain environment and normal offline build command reproduce this workspace build once the SDK package is in its cache. Clean installations restore the pinned package using the configured official NuGet source into the same private cache. Startup never downloads a runtime. Directory.Build.targets removes the SDK’s unused WPF binding before resolving references; no warning suppression was added.

## Accounting

First active segment13:16:56–approximately14:05UTC (~48min); resumed17:22:52UTC, completed around17:40UTC (~17min), excluding the permission wait. Shared quota was84%, then85%; the later account reading became43% with both reset credits still available. No reset was redeemed by this workstream; those shared readings cannot yield an attributable task delta. The goal service refused a new objective because an unrelated unfinished blocked goal occupies the slot (frozen145775tokens/901seconds); those counters are not this correction’s consumption. The scoped objective and checkpoints were therefore tracked in notes/workstreams/2026-10-29-interface-conformance.md, never by falsely completing the unrelated goal.

The final small offline-error diagnostic addition was verified with the same native conformance suite and republished transactionally; final hostPID35020 and runtime pointer are current in reports/interface-conformance/host.json and v1-qualification/ui-host.json. Real MCP discovery passed after this final publication.
