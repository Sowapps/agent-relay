# Windows interface

**Utility:** defines the monitoring UI, rendering boundary and lifecycle ownership.  
**Usage:** read before changing presentation, runtime packaging or interactive qualification.

## Presentation ownership

ProjectWindow is the C# monitoring host. Presentation/RelayWebView owns the embedded WebView2 control and its local resource boundary; Presentation/PresentationSnapshot owns passive data projection. Presentation/Assets owns the production HTML, design tokens, CSS, components, views and controller. Repeated cards, rows, filters and overlay drawers have one owner. The reviewed HTML prototype is an immutable comparison reference, not a second production renderer. Simulation controls and fixture data exist only in explicit development tools.

The sidebar provides General, relayed projects, Settings and a persistent relay indicator. General contains metrics, an inline-action alert, the Work stream and project tiles. A project contains Summary, Tasks, Activity, Messages and Sessions; Summary shows metrics, current/recent tasks, recent messages and compact context. Filters belong to their history section. Advanced filters and diagnostics remain collapsed. Details use a right overlay/backdrop, independent internal scrolling, fixed header/close control and contextual footer; they do not shrink or reflow the underlying page.

Settings offers read-only connector/connection/permission context and a collapsed administration/diagnostic section with refresh, project editor, legacy history, technical console and Quitter. The TechnicalConsoleWindow remains the separate manual operation/binding surface. Native dialog controls retain UiTheme/MonitorComponents; they do not duplicate the monitoring page layout.

## Runtime and local resource boundary

Microsoft.Web.WebView2 is centrally pinned with locked NuGet content hashes. Restore uses the private workspace SDK/cache. The Windows project copies presentation assets and the WinForms/Core bindings plus x64 loader into the content-addressed runtime. The package also supplies a WPF binding: the application-local build target removes that unused reference before assembly resolution; warnings are not suppressed.

An installed Evergreen WebView2 runtime is required. It is discovered, never installed automatically. A missing runtime or failed presentation displays a native diagnostic/quit fallback while the relay/MCP lifecycle remains independent. Browser data lives under the selected relay data directory in presentation-cache. It is cache/preferences, not project history or credentials; do not put it in the executable staging tree. Runtime installation/update remains outside ordinary application startup.

The control maps only the presentation asset directory to https://agent-relay.invalid/index.html. It rejects navigation away from that page, nonlocal resources, popups, downloads, permissions, host objects, browser context menus, developer tools and browser accelerator keys. CSP allows local scripts/styles/images and denies network connections, frames, forms and objects. Untrusted message/result text is escaped. The UI receives no native binding credentials. Its bounded message bridge selects explicit application actions; it cannot submit arbitrary pipe requests, shell commands or scripts. The native action handler validates retained IDs and project/session capabilities; application policy remains authoritative. Diagnostic inspection is an explicit action, never polling.

## Data and scope

Core MonitorProjection groups operations by project/task identity. Replies link only to an actual message operation with matching project/session identity. Independent reports never acquire a fabricated request/task. Presentation task IDs include the project ID. Historical workspace/program/argument context comes from retained execution data rather than current command rules. Provider and environment are separate descriptors: views never branch on Codex/Claude/WSL names. Unsupported connectors remain readable with unavailable actions omitted; role alone grants no action. Disabled projects expose no session controls.

PipeMonitorSource uses authenticated administrator-only observe_* retained reads. They do not import native inboxes, inspect agents, reconcile transfers, execute commands or prompt models. A green relay indicator confirms observation of the local relay, not agent authentication or an external client's MCP connection. Session/connector state remains unverified or retained until explicit inspection. This window cannot attest that another MCP client has connected successfully.

Reads use bounded 50-item/400KiB pages; visible lists initially show five rows. Histories/reset use all-history; summaries distinguish visible, matching and loaded counts, and badges describe loaded context. Empty states explain filters and offer reset. Session date filtering uses last retained activity, or no date if none is available. Filters apply to loaded data. Partial operation history is labelled; load-more follows retained cursors. Newest-head refresh and older pagination have separate state. A burst with no overlap reopens pagination; older active operations remain watched through passive exact reads. Monitoring message cursors are newest-first and independent of operational MCP inbox consumption. Read/unread acknowledgement is unavailable in the retained journal, so the UI reports received message counts rather than inventing unread counts.

The host refreshes configuration scope before observations and removes deleted projects. Unchanged data does not rebuild the view. Changed snapshots preserve search focus/selection, scroll and open details. Full operation details survive unchanged previews; changed outcomes invalidate that retained full version. Async detail replies only refresh the currently matching drawer. Failed refresh keeps retained rows and exposes relay uncertainty. Unknown work never offers replay. Native approval stays in the native agent session.

Messages/results provide copy before optional technical data. Technical operation details expose passive events and explicit partial-file inspection; cleanup remains in the technical console. Session rename/close/reopen/inspection and interruption reuse existing authorized application use cases with the session owner as acting participant. No page navigation invokes them.

## Session conversation

The conversation page is scoped to project/session and reachable from session list/detail and operation/message details. ProjectObservation carries an optional session ID; the same passive reader requests bounded observe_operations/observe_messages pages with independent session cursors and no native inbox import. One selected thread refreshes passively; leaving it stops that subscription. Loaded rows merge by identity into retained project context while preserving newer revisions. Thread metadata exposes loaded/partial/error state; revoked project contexts are removed. Earlier thread pages load explicitly, separately from ordinary project pagination.

The presentation model orders actual request, result/state and message events chronologically. It suppresses a duplicate result when the exact correlated reply is present; independent messages stay distinct. It never joins another project/session or invents native transcript text. Previews are labelled and link to exact retained operation details. Unknown session activity dates are not replaced with today.

All clickable monitoring controls have visible hover and keyboard focus treatment, including active navigation and segmented tabs. A reviewed-operation notice links directly to all-history/all-state project activity and the retained drawer; the cross title describes that path.

## Examined attention items

The operation alert exposes an icon-only cross with accessible name « Marquer comme examiné » and a title explaining both the action and project/activity/all-history destination. Its detail drawer retains the explicit text label on a bordered, contrasted primary button. Both use the same acknowledgement action. It removes that exact operation revision from the alert, General attention count and « À suivre » task/operation filters. The ordinary history keeps the original outcome and marks it « examiné ». This action confirms review; it does not resolve an error, approve native work, cancel or replay anything.

AttentionReviews stores a versioned attention-reviews.json file under the selected relay data directory, outside SQLite journal facts and outside presentation-cache. Entries are scoped by project/operation and hashed state, exact UpdatedAt ticks and error code. A new revision no longer matches and becomes visible again. The native action passively reloads the retained operation and compares the displayed token before an atomic, flushed file replacement; a stale drawer cannot review a newer outcome. The browser changes its display only after native success. Full operation-detail caches preserve updated review state.

A damaged/unreadable review file hides no alerts and is preserved; the interface reports a warning instead of silently overwriting it. One running host owns the data scope. Review status is a local administrative monitoring preference; existing participant MCP history/outcomes remain unchanged. Deployments preserve the file in place, and restarting the application reloads it.

## Lifecycle and identity

TrayContext owns the NotifyIcon, window and relay shutdown. ProjectWindow raises QuitRequested; Settings/Quitter and the notification menu reach the same exit. The window X hides it. Relaunch with --show activates the existing host through the authenticated administrative show_window action, creating no second host or icon. The data-directory file lock remains authoritative. Closing the UI does not kill independently owned agent sessions or replay uncertain work. Automatic startup is read-only in this monitoring surface and is never enabled implicitly.

UiTheme owns native identity assets. The executable/notification area use the committed multi-size ICO generated from Assets/agent-relay.svg. The HTML brand uses the same opposite-arrow identity at its own size. Regenerating committed raster/ICO assets uses tools/generate-icon.py; ordinary builds need no image generator. Resource embedding and process single-instance tests do not establish a visual notification-area-shell check.

## Reproducible qualification

Build/test with the normal application commands. tools/prepare-conformance.py checks the frozen reference hash and generates paired test-only pages/data without modifying the reviewed original. tools/check-interface-conformance.ps1 builds and runs 15 view/drawer cases at 1120x728 and960x648 through the production C# RelayWebView. It asserts actual viewport dimensions, nonblank content, metric bounds, absence of horizontal content overflow and expected overlays, then captures the control's own surface. Review the captured images as well as the machine checks; passing functional tests is not visual acceptance.

render-interface-check.ps1 with an explicit running data directory passively renders real General/Project/Settings/Diagnostics data through the same production host. These are separate test windows; no synthetic data enters the deployed application. WebView2 CapturePreview captures only its own content; desktop foreground capture is unnecessary. A control capture proves rendering, not tray gestures or user input. For interactive checks use Computer Use only against one exactly selected owned Agent Relay window. Stop on a locked desktop. Do not substitute unrelated foreground pixels for proof. Record automatic observations under application notes, awaiting operator validation.
