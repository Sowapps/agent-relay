# Complete update independent of the consuming client

**Utility:** proposes the target architecture and dependency-ordered correction plan for complete, recoverable Agent Relay updates.  
**Usage:** review the target and its explicit acceptance boundaries before authorizing implementation. This is a design candidate, not a claim of delivered behavior or permission to alter the installed stable.

## 1. Target before migration

The user requires one stable installation, complete updates from a local or remote reviewed artifact, idempotence, interruption recovery, rollback, and no dependency on restarting Codex or a fifteen-minute availability window. Project agents use stable; development remains isolated. Existing project files, identities, permissions, history and startup preference remain intact. All failures must be identifiable and retrievable. No uncertain business request is replayed.

The target is client-neutral: Codex is the currently observed constraint, not a dependency of update policy. Agent connectors and local/WSL/future environment adapters remain independent ports. Source checkout, downloads and explicit developer exports belong to local/; application-owned state belongs to installation-relative var/ through RuntimePaths. No new Windows service, scheduled task, listener, package or permission grant is implied.

### Acceptance invariants

| ID | Required result |
| --- | --- |
| T01 | Exactly one stable installation identity, data authority and active application release. A candidate and rollback material are private artifacts, not separately exposed stable instances. |
| T02 | The consuming MCP stdio connection survives a compatible application update. Completion never depends on reconnecting Codex or acknowledging a new cached tool catalog. |
| T03 | Newly supported operations are usable through the already-loaded stable MCP surface, with current authoritative schemas and permission checks. Documentation changes alone do not invalidate working operations. |
| T04 | The release manifest covers every replaceable component, its dependencies and required transport compatibility. No successful result hides a required component left outdated. |
| T05 | The same artifact/hash resumes one transaction. Repeating a completed update changes neither selected release nor host unnecessarily. A different artifact cannot overwrite an unfinished transaction. |
| T06 | Interruption at any published phase leaves a detectable old/new/recovery state. Authorized recovery resumes on the next update invocation or owned relay startup; it does not depend on the previous updater surviving. |
| T07 | Admit no new mutations during the switch. Finish admitted work or retain its exact unresolved state. No arbitrary deadline converts unfinished work into success, failure of the whole update or automatic cancellation. |
| T08 | Roll back coherently before reopening admission. After new writes, never restore an old database automatically; use compatible recovery or a separately reviewed forward correction. |
| T09 | Preserve binding, authorization, histories, startup enabled/disabled and one-host ownership. A startup-disabled installation is not silently enabled to make recovery happen. |
| T10 | Status shows selected version, transaction phase, blocking work, component compatibility and diagnostic references without secrets. Time, bytes, attempts and relevant consumption are retained per update and operation. |

T02 and T04 must be evaluated together. Keeping an old required executable alive and merely calling it a permanent component is not proof of a complete update.

## 2. Established causes and reusable capability

| Existing owner | Actual behavior | Target consequence |
| --- | --- | --- |
| RetainedLauncherPayload | Projects executable/DLL/JSON files from the application into retained launchers. | A retained entry point currently carries application files; physically separate its dependency graph. |
| InstallationUpdate.Prepare/ManagedFiles | Inventories/replaces retained launchers with the application distribution. | Active client-mapped binaries enter the replacement set and block publication. |
| InstallationUpdate.Verify | Requires BrokerVersion == application Version, current worker/catalog digest and current leases. | Replace equality with explicit compatibility/active component inventory; do not weaken verification to host health alone. |
| McpBroker | Already holds stdio, resolves the runtime pointer and replaces workers between requests. | Extract/refactor this owner. Do not introduce another parallel broker or another runtime resolver. |
| McpBroker/McpFacade | Associate tools/list with a package contract hash; reject many calls when it differs. | Separate stable wire compatibility, operation schema revision and build/document identity. |
| Installed Codex observations | Explicit reload works on an isolated backend; notifications and child exit did not refresh/reconnect the observed fixture. Actual desktop exposes no reachable supported control. | Do not make reload, process termination, config edits or notifications prerequisites for ordinary updates. |
| InstallationUpdate, complete_update.py | Already provide guarded inventory, journal, snapshots, retained recovery tool and identifiable errors. | Reuse these owners and refactor their phases; do not write a second updater. |
| stable_update_wait.py | Waits for all mapped clients, then times out. | Retire this operational path after the replacement procedure is qualified. A longer wait is not a correction. |

The preserved client proof is indexed by the [qualification guide](../../doc/developing/codex-mcp-refresh.md). It does not prove that the proposed new boundary or the initial migration already works.

## 3. Options challenged

| Option | Benefit | Reason to retain or reject |
| --- | --- | --- |
| Longer/persistent wait for Codex exit | Small source change. | Rejected as target: keeps client shutdown as a prerequisite. Durable waiting is appropriate only for actual admitted work. |
| Force-close facade / send list_changed / edit registration | Can affect a particular client lifecycle. | Rejected as update dependency: observed client does not automatically reconnect/refresh; risks losing response or changing the wrong connection. |
| Expose/start another Codex backend | Provides a reload RPC. | Rejected: does not control the owning backend. Existing supported control remains an optional adapter only. |
| Streamable HTTP instead of stdio | Removes the per-client application executable from the client process tree if the client connects directly. | Possible separate transport; catalog refresh still matters, requires endpoint/auth migration and supported current-client reconnection. Not a magic fix for the existing stdio connection. |
| Keep a generic stable MCP entry surface and replaceable relay engine | Application updates need neither new tools/list nor replacement of application code loaded by Codex. | Preferred candidate for ordinary updates, conditional on proving initial migration and the permanent transport maintenance boundary below. |
| Frozen typed MCP tools only | Strong direct descriptions and simple calls. | Retain as compatibility routes where useful; insufficient alone for arbitrary new operations without a catalog refresh. |

Recommendation: a stable generic MCP surface, explicit transport/operation compatibility and independently activatable application release. Keep optional typed aliases generated from the same operation owner; they are conveniences, not required for access to new features.

### Current capability matrix

| Candidate | Same client stream through engine change | Arbitrary required loaded transport replacement | New operation without cached-catalog refresh | Current connection migration |
| --- | --- | --- | --- | --- |
| Existing broker | Worker selection exists in source; whole-package replacement currently blocks on launcher files. | Not provided. | No: current operational catalog guard can refuse it. | Already loaded, but not the proposed minimal boundary. |
| Extracted minimal transport + stable discovery/invocation | Intended capability to qualify, reusing existing broker behavior. | Unproved; cannot be assumed from extraction. | Intended capability through unchanged generic schema, to qualify. | Unproved: current catalog lacks generic tools. |
| Direct client HTTP MCP | Avoids mapping the Agent Relay application executable into its client transport process. | No Agent Relay stdio executable in this topology; server still needs its own coherent update. | Only if its loaded MCP surface is stable; HTTP alone does not refresh a catalog. | Requires a supported client rebind; not exposed in the current observation. |
| Owning Codex reload | Isolated reload works but replaces the MCP connection. | Can release/recreate transport when actually reachable. | Isolated catalog refresh proved. | Current owning-backend access unavailable; a second backend does not solve it. |

This matrix is analysis, not a new test result. It rules out advertising the preferred candidate as satisfying the entire mandatory requirement before G0 is resolved.

Two traces make the remaining assumption concrete. For an ordinary update with an already-loaded compatible stable envelope: the same connection submits a request, the host persists/drains admission, the transaction selects the checked engine, and the next unchanged-envelope call discovers/invokes the current operation. No tools/list change is needed. For a release requiring different persistent transport code: the engine can be staged, but activation cannot be called complete until the required new transport is active on the existing connection. There is currently no established step accomplishing that handover. Initial migration has the same missing client-cooperation step for exposing the generic surface. Do not skip these steps in a nominal diagram.

## 4. Proposed responsibility map

| Capability | Responsibility and intended owner |
| --- | --- |
| Minimal MCP transport | Extract the framing/connection loop from McpBroker into a separately packaged C# transport component. Preserve the original client stream, binding identity and bounded protocol frames. No SQLite, model/session policy, tool catalog snapshots, UI, update decisions or copied application assemblies. |
| Current operation discovery and dispatch | One application registry projects operation identifiers, revisions, input/output schemas, descriptions and required capabilities. Both generic invocation and optional typed aliases call existing authorized application routes. No second permission/command interpreter. |
| Release selection and owned paths | RuntimePaths and the existing checked runtime descriptor remain the sole layout/selection owners. Transport consumes a narrow versioned projection; no second resolver or path constants. |
| Update transaction | InstallationUpdate owns inventory, durable intent, cutover, recovery and rollback rules. Small collaborators may own protocol compatibility or migration strategy, without splitting every phase into a new service. |
| External lifecycle | Existing deployment/startup/connector adapters perform process/file/host actions under the C# decision. Python stays orchestration; business/recovery rules remain C#. |
| Completion projection | Transaction owner reports complete/blocked/recovery with component facts. UI, MCP and CLI render the same result. Leases and old process versions are observations, not independent completion policy. |

Dependencies point toward contracts and policy. Complementary open/close, select/restore, admit/drain and publish/recover operations reference the same invariant owner. Add targeted see/seealso references at extracted lifecycle boundaries; do not create a web of incidental links.

### Stable MCP surface

Proposed small fixed surface: discover scoped operations, invoke an operation, inspect a retained result and inspect relay/update readiness. Names and exact fields are finalized before the first implementation point, not multiplied into dozens of meta-tools.

Invocation carries operation ID, explicit schema revision, request ID, project/session scope where applicable, and arguments. The bound credential determines the actor; caller-supplied endpoint fields cannot create authority. Discovery is permission-filtered but never substitutes for admission/execution checks. An invocation is not a shell command or a bypass around existing tools. Oversized input/output, unknown revisions and revoked grants have bounded typed errors.

Operation registry/schema is the single definition. Resource docs are live projections or packaged matching documentation, not another source of validation. Evolving an operation means adding a supported revision/translation or returning a precise unsupported_revision refusal with no effects. A package digest change is not, by itself, an incompatibility. Keep product versions stable/RC only; transport protocol and operation revisions are compatibility identifiers, not new product release channels.

Cache policy: discovery carries a revision; stale invocation receives a specific refresh-discovery response, not an instruction to restart Codex. No implicit mutation retry. Retained IDs allow the caller to inspect an uncertain outcome before choosing a new action. Supporting long operations does not require keeping a single MCP call open indefinitely: existing retained-operation behavior remains authoritative.

### Admission, correlation and transport loss

The host is authoritative for admitted work. A per-installation cutover epoch prevents stale workers from admitting work against the retired release. The transport cannot invent acceptance. A rejected maintenance call states confirmed-not-started and the update ID; do not silently queue unlimited work or replay it after restart.

When acceptance may already have occurred, retain the request/operation ID and report uncertainty; reconcile against existing durable receipts. A lost response is not a failed business operation. Do not kill an execution worker merely because an MCP response is slow. Drain waits for the actual admitted operations, not for all open client connections. Protocol/request deadlines remain bounded; they are distinct from expiring the update transaction.

## 5. Single-installation layout and activation

Keep the installation root and data identity. Prepare an immutable candidate privately under the existing owned update area. Keep selected release bytes separate from the updater/transport bytes that stay loaded during cutover. A checked atomic descriptor selects one active engine release. Exact directory names must be derived through RuntimePaths after comparing the current fixed app/ layout with the existing release-slot mechanism; do not scatter a new layout through scripts.

One logical stable installation may retain a staged candidate and rollback files internally. It must not expose two stable registrations, two data stores or two admitting hosts. Cleanup removes old bytes only when no retained recovery or loaded-process reference requires them. Temporary old bytes are reported as cleanup retention, not as an additional active stable. The active component manifest still identifies the exact required binaries; retention is not permission to keep required old application code executing.

The startup entry targets a stable transport/bootstrap identity that resolves the current release. Enabling/disabling startup remains an existing user preference. Complete update validates that identity and pointer. It neither rewrites a version-specific Windows startup path each release nor installs another automatic-start mechanism. An existing entry migration is an explicit, backed-up adapter effect under its authorization.

## 6. Durable transaction and recovery target

The following are proposed semantic phases to reconcile with the existing schema, not an instruction to create a second journal:

1. Validate artifact, manifest, transport compatibility, free space and installation/data scope; take the exclusive update identity. Unchanged completed candidate is a verified no-op.
2. Persist preparation intent, fill/check the immutable candidate and retained recovery runner. Partial download/stage is resumable; original ZIP is not required after verified staging completes.
3. Publish admission barrier and drain admitted work. Persist exact blockers; awaiting_work survives updater exit and has no fifteen-minute expiry. An unknown outcome remains unresolved until reconciliation, not automatic cancellation.
4. Snapshot the quiescent database and protected state; apply required migrations under exclusive ownership. Stage UI/docs/adapters/startup payload and all required components before selection. Verify migration compatibility with rollback.
5. Persist cutover intent and atomically select the verified candidate. A process death is reconciled from actual before/after hashes, pointer and host ownership; never assume an intent was applied.
6. Start exactly one selected host; verify authenticated release identity, full manifest, migration/schema, preserved scope/startup and requests through the still-open transport. Reopen admission only after these pass.
7. Persist completion and allow retention cleanup. Deleting rollback material is a separate guarded cleanup step, never the commit point.

Resume on explicit update invocation, next gateway-owned cold start or existing enabled startup. Recovery acts only on a previously authorized matching transaction; it does not fetch a different release, enable startup or execute a model. With no process running and startup disabled, recovery begins on the next authorized launch, not by inventing a scheduler.

Before admission reopens, restore the coherent prior files/pointer/data and startup selection on failure. A failure of recovery retains a barrier and both diagnostics. After admission reopened or new writes occurred, automatic old-snapshot rollback is refused; use a backward-compatible engine selection only if its schema contract explicitly permits current data, otherwise forward correction. The same guard must own CLI, MCP and restart recovery.

### Failure cases that change the plan

| Event | Required behavior |
| --- | --- |
| Disk full/access denied before cutover | Keep current engine serving where safe; retain stage/error, do not claim update complete. |
| Busy or unresolved operation | Persist awaiting_work with IDs; do not cancel, replay or wait on idle client connections. |
| Process/power loss during stage, barrier, migration, pointer switch, startup or final acknowledgement | Reconcile exact phase and owned effects on resume; old or new coherent release, never mixed admitting state. Flush durability and filesystem atomicity must be qualified on the actual storage. |
| Conflicting updater/candidate or unexpected file/pointer | Refuse with diagnostic; do not overwrite external changes or clear another lock. |
| Candidate host fails readiness | Restore coherent prior state before admission; if restoration fails, retain recovery_required. |
| Response lost after admission | Report request ID/uncertainty, reconcile retained result, no replay. |
| Client idle for hours across update | Next call works via the same MCP surface and current compatible dispatcher, without changing its cached catalog. |
| Required transport upgrade unsupported by live connections | Preflight refuses before cutover; this is an unsatisfied target boundary, not a partial successful update. |
| Diagnostic storage fails | Preserve primary error and expose fallback/secondary persistence failure; never lose the failure because logging failed. |

## 7. Hard boundaries and first migration

The preferred design eliminates ordinary application-release coupling to the client process. It does not establish that an arbitrary future change to the persistent transport itself can be activated in a running process. A minimal transport is not an excuse to declare that executable unmaintainable or permanently exempt from security fixes.

There are two distinct cases:

- If the candidate requires the exact existing transport bytes/protocol, no transport replacement is needed; completeness means every required component matches the candidate manifest, not that all component version strings equal the application version.
- If the candidate requires different transport bytes, a qualified live handover or a supported reconnect of that one MCP connection is required. Current Codex exposes no verified reconnect control to us. No universal no-client-restart guarantee is established for this case. Do not proceed on the assumption that merely reducing the executable size solves it.

The initial connection also lacks the proposed generic tools in its cached inventory. Adding them on disk or to a notification cannot make them callable in that already-loaded client. Evaluate continuing the existing named tool surface through an explicitly compatible adapter as a transition, but this cannot prove access to genuinely new generic capabilities. The old monolithic loaded broker cannot be renamed “minimal transport” to pass acceptance.

**Design gate G0:** prove or identify a supported path for (a) initial migration and (b) required transport changes that satisfies the user's no-Codex-restart requirement. Until then, the design is a qualified candidate with an unresolved boundary, not an approved executable plan for the installed stable. If the proof fails, report the contradiction and alternatives before implementation; the operator decides whether to accept a separately described maintenance boundary. No silent exception to T02/T04.

## 8. Challenge record

The design was reviewed in separate focused passes. Stopping condition is no new concrete issue in the final consistency pass, not a claim that every future failure was anticipated.

| Pass | Challenge discovered | Plan response |
| --- | --- | --- |
| Responsibilities/existing code | A second broker/updater would duplicate already delivered switching/recovery. | Extract current owners; preserve one registry, layout and transaction policy. |
| Full inventory | A gateway left out of the update manifest would hide a half update. | Exact required transport identity/compatibility in manifest; unsupported change blocks before effects. |
| Client/catalog | A dynamic tools/list still depends on client refresh. | Stable generic invocation/discovery; typed aliases optional; first migration explicitly unresolved. |
| Drain/concurrency | Waiting for idle client processes has no relation to admitted work finishing. | Host admission barrier/epoch and authoritative operation IDs; no client-exit countdown. |
| Crash/data recovery | A pointer rollback after new writes could silently lose work. | Snapshot-before-cutover only, durable admission boundary and guarded forward recovery afterwards. |
| Operations/security | Generic invoke could bypass project rules or replay uncertain work. | Same routes/credential/validation, revision/refusal and existing request/receipt correlation. |
| Maintenance/lifecycle | Permanent shim cannot promise arbitrary hot replacement; no running process means no immediate automatic recovery. | Gate G0; distinguish ordinary payload update from required transport change and next-launch recovery. |
| Last consistency pass | Target requirements still exceed proven transport and migration capability. | Keep this explicit blocker; no implementation/deployment readiness claim and no compromise buried in a footnote. No additional concrete contradiction identified in this pass. |

## 9. Intermediate lots derived from the target

Every row needs a local point plan immediately before execution. Cost labels are relative engineering scope, not measured time/token promises. Gate order is strict; useful independent design preparation may continue, but a failed architectural gate does not authorize downstream implementation.

| Lot | Scope and primary owners | Exit proof / dependency | Cost |
| --- | --- | --- | --- |
| U0 | Resolve G0: transport lifecycle and current-client migration. Preserve actual-client versus isolated evidence boundary. | Concrete supported migration/handover, or explicit unresolved requirement brought back for arbitration. No installed change. | Small analysis; bounded isolated proof only after its own approval. |
| U1 | Finalize MCP envelope, operation revisions/registry and transport manifest. Identify legacy mapping and required data/protocol migrations. | One owner per contract; end-to-end examples cover same/new operation, stale revision and revoked permission. Depends U0. | Medium. |
| U2 | Extract minimal C# transport and separately package its exact dependencies; refactor existing broker/worker composition. | Same stdio client across engine change, current operation/result access, no lost/replayed request; exact packaging inventory. Depends U1. | Medium/high. |
| U3 | Refactor existing C# transaction for immutable selection, admission/drain, snapshots, startup continuity, resume/rollback and idempotence. Retire standby countdown path. | Targeted phase-interruption and repeated-command proofs on copies; no mixed admitting host. Depends U2. | High, highest data risk. |
| U4 | Integrate one CLI/update entry and common MCP/UI progress/errors/diagnostics; update bootstrap and developer procedures. Prototype first if UI changes. | Same authoritative status across entry points; agent can identify blockers and resume safely. Depends U3. | Medium. |
| U5 | Qualify complete package and migration on isolated installation, then prepare reviewed stable deployment and operator result. | Exact archive/hash, preservation, rollback and actual still-open consuming client. Stable effects require the applicable explicit GO. Depends U4. | Medium; no repeated broad suite without new reason. |

No version/tag/release or five-lot duration promise is proposed before the target and G0 are resolved. A local/remote release source feeds the same verified artifact; it cannot select a different update policy.

### U0 / first point local plan

**Purpose:** resolve feasibility before coding a topology that still needs a Codex restart.

1. Reuse the retained 01/11 and 12/11 observations. Record installed client/backend identity and exposed supported lifecycle mechanisms without taking UI control or repeating model tests.
2. Write a capability matrix for current stdio broker, extracted transport, direct HTTP and owning-client control: what can retain the same connection, replace required loaded code, discover a new operation, and migrate the current cached surface. Mark unknown separately from unsupported in the observed setup.
3. Compare each against T01–T10. Trace one normal update and one transport-breaking update from package through unchanged consuming client, including the initial migration. Identify every assumption requiring client cooperation.
4. Only for a concrete supported handover candidate, prepare the smallest isolated protocol experiment: synthetic MCP, persistent private fixture directory, zero model turns, explicit before/after tools/operation/runtime identities. A new backend proves its own behavior, not control of the actual desktop.
5. Save result, artifacts, limits and next decision. Stop this point if no supported path satisfies the mandatory boundary; do not fall back to a countdown, injection, daemon installation or forced restart. Independent documentation can finish, implementation stays gated.

Preparation status: the historical evidence lookup, source ownership inspection, capability matrix and the two analytical traces above are completed in this design workstream. Reuse them; do not start U0 by repeating them. Its next execution point is identifying a concrete supported handover/migration candidate or producing the evidence-based arbitration. No new protocol experiment has been run under this design-only GO.

**Excluded effects:** no real AP work, replay, configuration rewrite, native agent/WSL call, live facade closure, global install or stable mutation. **Useful verification:** contract/lifecycle trace first; one new fixture only if it resolves a specific remaining hypothesis. **Completion:** a supported satisfying route or an explicit product arbitration with enough evidence to decide. It is not “gateway compiled”.

### Local point-plan template for later lots

Record purpose/target invariants; current checkpoint and prerequisites; affected owners and complementary operations; exact edits/exclusions; one coherent nominal trace; failure/interruption/rollback trace; specific proof with reused evidence; expected artifacts/performance; exit criterion; next point. Recheck actual source before writing these details. If requirements conflict, revise the target instead of improvising an intermediate fix.
