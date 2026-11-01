# Windows startup preference implementation

**Utility:** retains the authorized startup implementation checkpoint and qualification.  
**Usage:** resume only this local application work; no commit, publication or unrelated configuration change is authorized.

## Scope and objective

Florent: « Très bien applique », following the reviewed startup prototype and Général naming. Implement the preference, isolated nominal/error tests, documentation and guarded local application update. Preserve current startup opt-in (do not enable the user's scope), projects and permissions. No Windows reboot, agent/model invocation, shared client configuration change or release.

Tracked objective attempted; native goal creation is refused by the unrelated historical blocked goal (145775 tokens/901 seconds). Do not falsely close that goal. This scoped dated record carries the checkpoint. Start approximately 10:10 UTC (11:10 Europe/Paris); separate token accounting unavailable.

## Design

Keep per-user, per-data-directory Run ownership. A durable native launcher under the data directory forwards to the current runtime pointer without a console or --show. Retain its self-contained dependencies independently of versioned runtime cleanup: hard-link immutable payload files where supported, otherwise copy. Only install this launcher on explicit switch activation. No scheduled task, service, global installation or model start. Ordinary source development needs a qualified self-contained launch payload before enabling; errors must explain that requirement. The local update will use such a payload.

UI owns pending/error display; native preference adapter owns registration/read-back and launch validation. General settings shows unknown state if inspection fails, rather than Disabled. Failed writes preserve the last confirmed state. The technical console reuses the same registration owner.

## Completed implementation

General settings now exposes the reviewed switch, confirmed state, pending lockout, keyboard focus recovery, readable error/retry and unknown-read state. IStartupPreference owns the testable native boundary; no project/MCP permission was expanded. The technical console still reuses StartupRegistration.

The per-data bootstrap retains native dependencies separately from versioned payloads. Staged immutable payloads use hard links where supported; ordinary portable payloads use copies. Startup reads the selected runtime pointer and forwards to tray with no --show or console. The Run command omits the duplicated data argument: the bootstrap derives its scope from its parent directory. A pre-registration length guard follows Microsoft's documented 260-character Run limit; see the Windows interface guide for the primary reference. Missing targets are refused before registration; stale registered targets remain inspectable/disableable. Actual Windows startup policy can independently block the registration, explicitly disclosed in the UI.

## Qualification and diagnostics

- Normal integration/process suite passed: local durable exchanges, scope/permissions, recovery, duplicate ownership and process smoke. Artifacts: local/apps/agent-relay/tests/e0f7e5e5b5d542eba5441043d3a1c7cd.
- Native UI → actual isolated HKCU Run activation/deactivation passed, including failure preserving confirmed state, Retry, unreadable state, compact own-control rendering and cleanup. Actual retained native bootstrap with space-containing paths, implicit parent data scope, bounded host, duplicate launch, source pointer arguments and missing target refusal passed.
- Initial process qualification timed out without a localized stage. Added explicit stage diagnostics and a 30-second bootstrap wait allowance; subsequent completed checks passed. No reboot/logoff was performed and no real Windows-login measurement is claimed.
- Reusable command: apps/agent-relay/tools/test-startup.ps1, passed against the final staged payload, including its success cleanup. Final report: local/apps/agent-relay/qualification/startup-1bebb4d7616e4f508930da02e30417d6/render/verification.json. Earlier initial/replacement captures remain under qualification/startup-settings/render.
- Production own-control capture after deployment passed (General/Project/Settings/Diagnostics/conversation), under qualification/startup-settings/deployed-render. Settings capture shows both retained real projects and Disabled startup.
- Packaged MCP docs check passed: 24 tools, five resources and seven schema-checked examples; zero model/agent-session calls. Workspace verification and git diff --check passed. These are observations, not operator evidence.

## Local deployment and preservation

Final host PID27404, immutable runtime fingerprint22654D961AF029322FAD200FBD531EBC5E2AFBE… (full path in reports/startup-settings/host.json). Guarded transaction: reports/startup-settings/deployment-8220e128a73b4ef5be1885931377990b. Existing canonical host metadata and data/ui-host.json now name the new host.

Deployment used the established copy/preflight/snapshot/idle/identity/rollback procedure. The original host record was stale; the only initially live old process was an MCP facade, which was not stopped. The first new tray host was then replaced through its exact recorded identity for the final command-length correction. Hash checks confirmed preservation of relay-projects.json, relay-config.json and all protected connection files. No global Codex connection, user's project file, WSL/native agent, repository tag or published release was modified. Startup remains disabled in the user's scope.

Bulky unused qualification payload/probe/bootstrap copies were removed only after absolute boundary and process-use checks. Captures, small fixtures, reports and the live immutable runtime remain. The test command performs its own successful-run cleanup; failed checks retain diagnostic material.

## Outcome and accounting

Authorized local implementation complete. Approximately 22 minutes (10:10–10:32 UTC; 11:10–11:32 Europe/Paris). Workstream-specific token accounting remains unavailable because the native goal API rejects the pre-existing unrelated unfinished objective; that objective was not changed. No quota reset was used. No commit or publication was performed. A future real-login test or release requires its own authorized scope.
