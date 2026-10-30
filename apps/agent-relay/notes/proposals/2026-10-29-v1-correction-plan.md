# V1 correction plan

**Date:** 2026-10-29  
**Status:** implementation proposal; only audit/documentation preparation is authorized by this request.  
**Utility:** closes the observed gaps against all agreed requirements without declaring untested behavior complete.  
**Usage:** execute only after a scoped GO; use gap IDs and exit gates for implementation, reviews and acceptance.

## Authority and baseline

Florent requests recording transfer limits and a plan correcting all V1 gaps. This document supersedes the implementation sequence/open-status statements in the older MVP/V1 and session-runtime proposals; it does not erase their dated observations or grant implementation permission.
Owners: [functional agent-session](../../doc/target-spec/functional/agent-session.md), [configuration](../../doc/target-spec/functional/configuration.md), [system I/O](../../doc/target-spec/functional/system-io.md), their technical counterparts and the [fixed glossary](../../doc/developing/glossary.md). Evidence `2026-10-28-mcp-is-primary-usage` establishes actual agent use through MCP as primary, with the app for monitoring/debugging.
The three assemblies, private qualified toolchain, SQLite durability, core ports, staged runtime, pipe transport, native clients and18-tool facade are reusable. Existing isolated successes prove those scenarios only. Current-chat MCP use remains unproven; Claude's last qualification failed expired login; latest pixel/tray capture was unavailable.
The original target's one-task restriction describes the historical MVP operating profile. The evening GO subsequently authorized named sessions and parallel work; update current target documentation with that scoped evidence during implementation rather than silently retaining contradictory restrictions.

## Complete gap register

Every item has a source location to inspect and a measurable closure condition. A gap includes missing implementation, implementation inconsistent with the target, or required qualification absent. Unknown future defects are not represented as already audited; newly discovered gaps enter this register before completion is claimed.

| ID | Gap and source | Required correction and closure evidence |
| --- | --- | --- |
| G01 | Real project identity absent: Core/Work.cs; Infrastructure/RelaySettings.cs | Stable RelayProjectId on tasks, sessions, messages, operations, artifacts, receipts and queries; project lookup independent of names/paths; two-project isolation scenario |
| G02 | Principal/secondary roles implicit; message_server.py uses windows-pilot | Endpoint identity separate from adapter/client; project-assigned main/principal and secondary/secondaire roles; neutral sender/recipient routing, including two principals with correct destinations |
| G03 | Caller can select profile/session without a project-bound identity: McpFacade, pipe dispatcher | Bind each MCP connection to an authenticated/scoped principal and permitted endpoint/projects; server-side authorization for reads, writes and lifecycle; forged IDs and cross-project result/inbox access refused |
| G04 | Broad AllowLocalWork/profile commands substitute for project permissions | Central policy for endpoint membership, workspace access, command rules, local agent work, host inspection and transfer read/write/replace; deny by default and evaluate before side effects |
| G05 | Only generated session workspaces: SessionService/WslProvisioner | Configurable real Windows/Linux workspace records, reusable across sessions; private session runtime separate from user working files; no implicit sync, relocation, copying or new rights |
| G06 | WSL-specific orchestration and WindowsPath/LinuxPath in Core.TransferRequest: MvpCoordinator/SessionService/FileTransferService/Core.Work | Neutral domain contracts/use cases/ports; WSL/tmux, native-client and UNC implementations become adapters; non-WSL contract adapter works without changing business cases |
| G07 | Duplicate demo/real request paths; LocalDemo.Message used by session MCP | One versioned production envelope and dispatcher for MCP/UI/CLI; explicit demo fixture isolated from production routing; no local-demo project substituted for real work |
| G08 | Tasks, attempts and events not distinguished from WorkRecord | Stable task/operation/attempt/message IDs and correlations, initiator/destination/project context; pending/running/awaiting-approval/completed/failed/unknown/interrupted semantics; events link to the correct task/session |
| G09 | Polling and string matching overstate readiness; stale MCP marker/pane text: AgentControl/native_clients.py | Separate process/session/MCP/auth/approval/busy readiness; bounded phase-specific checks and startup reconciliation; expired login, lost tmux/helper, missing CLI and stale marker produce actionable states |
| G10 | Resume uses --last/--continue, not durable native identity | Persist actual native conversation ID per relay session; explicit resume of the same context or honest context-lost state; no silent fresh conversation, including manual tmux takeover |
| G11 | Global mutation lock, per-session worker and native completion differ | Explicit admission for lifecycle/config/messages/transfers; short config locks, independent-session startup/work, configurable bounded capacity and safe shared-workspace write policy; same-terminal overlap refused |
| G12 | Timeout/cancellation/auth failure can misdescribe post-dispatch effects | Phase-aware outcomes; cancellation is a request, not rollback; late receipts reconcile once without replay; close/reopen and bridge/native cancellation races tested |
| G13 | State/receipts span SQLite and native files; partial preparation/publication can strand state | Versioned dispatch/receipt identity, durable preparation and recovery for all rows, bounded reconciliation and explicit incomplete states; publication-before-receipt failure stays unknown unless independently verified |
| G14 | send() collapses message whitespace; native instructions/helpers contain fixed client/user paths | Preserve exact text/Unicode/newlines via a literal bounded payload mechanism; endpoint-configured executable/user/client options; project instructions versus qualification isolation handled deliberately |
| G15 | Ordinary autonomous reporting not qualified: prior prompts explicitly requested independent messages | Configure useful progress/blocker/clarification policy; one natural short task verifies an agent-originated message without naming relay tools, separate from final correlated reply, for both clients |
| G16 | Pagination ends at SQLite; relay_files.messages loads all outbox JSON repeatedly | End-to-end bounded retrieval/import with native cursors/limits, stable deduplication and receipt semantics; no full history in every send response; representative retained-history case stays below frame limits |
| G17 | Search scans raw JSON; no project/task/date query or event grouping | Project/endpoint/task/session/type/state/date filters, content/label search and stable cursors; content search semantics exclude accidental metadata matches by default; no cross-project leakage |
| G18 | No useful host inspection beyond fixture command profiles | Explicit authorized host diagnostics distinct from workspace commands: effective user, tools/versions, availability and selected configuration/service status; no secret dumps or implicit admin rights |
| G19 | Transfer is coupled to session/WslBridge/UNC and coarse permissions | Neutral artifact/transfer contract with project, source/destination endpoints/workspaces, effective identity and explicit read/write/replace authorization; retain UNC as WSL default pending measurement |
| G20 | Fixed256MiB/3min and20s hashing timeout; no progress; temporary/receipt crash gaps | Coherent configurable size/deadline policy, bytes/progress, targeted cancellation, integrity/publication receipts and owned-partial inspection/cleanup; disk-full, source mutation, collision and interrupted publication cases |
| G21 | Large/bulk performance, directory transfer and resumability unqualified | Explicit supported V1 file-only workload, measured nominal/near-limit and bounded many-file costs; retain future triggers/options below; no unmeasured high-throughput or bulk support claim |
| G22 | Session metadata and templates exposed wholesale; logs/credentials/inherited instructions lack complete scope policy | Separate secret references/protected storage from workspace files, redact diagnostics, restrict log/raw-detail reads; qualify effective OS identity and instruction sources; do not promise same-user OS isolation |
| G23 | UI original profile composer, recent lists, session IDs only; latest visual acceptance absent | Project-first context, session/task grouping, matching message/operation presentation, filters/load-more, readable progress/errors and copy/raw details; UI observes all MCP types from the same read model |
| G24 | Sign-in option/sign-out handling required but not implemented: TrayContext/Program | Manual launch remains default; explicit opt-in per-user startup with stable launcher and reversible owned registration; bounded graceful sign-out/quit preserving uncertainty and independent native sessions |
| G25 | Config/schema/tool contracts grew without complete migration/version policy | Backup, deterministic additive migration, explicit unresolved legacy project mapping, pinned compatibility/deprecation and rollback recipe; no implicit old-profile authorization migration |
| G26 | MCP registration is mistaken for actual client usability | Current Windows agent discovers/invokes the actual server; full nominal workflow uses MCP and matches UI IDs; supported reconnect/version diagnosis, no imaginary Restart button or blind backend kill |
| G27 | MCP validation/limits/JSON-RPC versions and timeouts inconsistent across layers | Versioned bounded schemas, server-side argument validation and consistent errors, limits/negotiation and timeout ownership; clear unsupported-version/unavailable response and model-free compatibility checks |
| G28 | Qualification, publishing and maintenance can affect runtime before failure | Reusable guarded scripts, explicit prepared deployment/backup/rollback, exact process/root ownership, inactive-work guard and runtime/version check; failed preparation leaves the last usable host available |
| G29 | Native approval/external-effect authorization is not represented in relay state | Expose awaiting approval and a safe human handoff; project permission never grants mail/push/deploy/delete/publish by itself; no automatic approval or replay; test denied/awaiting states |
| G30 | Current target/reference/usage statements conflict or overclaim; objective frozen | Reconcile target versus implementation, bilingual glossary, current docs, dated observations and operator evidence; acceptance matrix includes remaining gaps and honest consumption/time tracking |

## Chosen architecture and design decisions

Retain the existing three assemblies initially. Core owns domain objects, neutral ports and application use cases in separate folders; Infrastructure owns adapters/storage/transport; Windows is the composition/UI host. A fourth assembly is justified only if an actual build/dependency boundary requires it. Avoid a new framework, event bus or generic repository layer.
Split the current coordinator's domain orchestration from transport translation. Proposed focused ports cover configuration/authorization, durable records/events, endpoint session control, messaging, commands, host diagnostics and artifact transfer; share one port only when semantics genuinely match. MCP/UI/CLI map to the same use cases.

Proposed model: RelayProject identifies authorized workspaces and endpoint memberships; Endpoint identifies a participant and adapter binding; Membership assigns main/principal, secondary/secondaire and optional existing reviewer/observer roles within that project. Session references an endpoint/project/native conversation and chosen workspace; it does not create project authority. Task carries intent/correlation; Operation and Attempt record execution; Message/Artifact/TransferReceipt retain their project and endpoint identities.
Names are editable metadata; UUIDs are stable identities. A configured endpoint can participate in several projects with different rights. A task chooses an authorized destination; caller-supplied project/sender strings do not authenticate that caller. A role alone never grants a capability. Windows/WSL/Codex/Claude remain adapter/configuration facts, not business roles.
The neutral transfer contract identifies source and destination by endpoint/workspace/relative path. WindowsPath/LinuxPath and Windows-relative upload/download directions remain only compatibility/adapter vocabulary; they cannot remain domain fields while claiming an OS-neutral core.

Keep one central versioned configuration initially. Bind MCP launch context to an existing endpoint/principal plus scoped project membership, using an unforgeable local credential/reference where necessary. Validate that binding again at the application boundary, including read/query/result access. A filename, cwd, arbitrary project ID or CurrentUserOnly pipe alone is not project authorization.
Keep the administrator UI/maintenance capability explicit and separate from an agent connection. Native message servers receive a scoped endpoint/session binding from trusted configuration, stamp sender/destination/project, validate reply ownership and cannot invent destination rights. WSL-to-Windows capability remains messages only; neutral endpoint roles do not make this directional policy symmetrical.
Permissions include workspace read/write, configured direct commands with parameter/path constraints, native local work, host diagnostics, file source read/destination create/replace and session lifecycle. Local OS/native-client permissions are an additional boundary. Define revocation for future submissions and active work explicitly; do not claim already-started effects can be undone.

Existing native clients/private config and human tmux access remain. Separate adapter runtime/configuration roots from configured workspaces; do not substitute fixture files for a user's project. Same workspace can serve several sessions; serialize conflicting mutations by default within the relay unless an explicit project policy permits concurrent writes. Distinct workspaces/sessions may run concurrently within the configured limit.

Use explicit native conversation identity after creation, not latest-session guessing. Preserve project instructions when authorized; qualification isolation must not become a blanket product policy disabling legitimate AGENTS.md/CLAUDE.md. Inspect inherited sources and reference protected credentials without logging their contents.

Keep SQLite and a bounded durable event/read model. Do not promise exactly-once external effects: intent-before-dispatch, stable idempotency keys, phase-aware uncertainty and receipt reconciliation remain the model. A response message and native runtime-idle state are distinct observations.
Protocol versioning must permit a narrow compatibility adapter only where scope can be proven. Unscoped legacy calls cannot silently select a default project. Large results/artifacts use bounded references/pages; model-message and command-output limits are explicit and truncation is visible.

## Ordered execution lots

| Lot | Scope | Depends on | Exit gate |
| --- | --- | --- | --- |
| L0 | Inventory current data/contracts/requirements; capture backups and acceptance IDs; diagnose actual client MCP gate without mutating user runtime | Implementation GO | Reviewed mappings, reproducible baseline, precise blockers and safe rollback plan; native prompts not repeated for inventory |
| L1 | G01–G05,G08: neutral project/endpoint/workspace/role/policy model and caller binding design; migration mapping and envelope schemas | L0 | Two project examples and permissions resolve deterministically; ambiguous old records remain explicitly legacy/unassigned |
| L2 | G06,G07,G11,G27: extract neutral use cases/ports; production routing and shared transport contracts; one vertical authorized message/result path | L1 | Non-WSL contract adapter exercises the same cases; denied read/write/project spoof produces no external dispatch |
| L3 | G09,G10,G12–G15,G22,G29: adapt Codex/Claude/WSL/tmux, native identity, exact messages, approvals, recovery and independent routing | L2 | Each adapter qualifies short nominal/attention paths; continuation, targeted interruption, late reply and no-replay behavior meet the state model |
| L4 | G18–G21: real project workspace commands/host diagnostics and neutral file-transfer adapter; coherent bounds/progress/receipts | L2; lifecycle contract L3 | Effective contexts/rights verified; file round trip and meaningful failure cases pass; UNC retained unless evidence warrants another transport |
| L5 | G16,G17,G23: bounded end-to-end events/inbox/results, filters/grouping and monitoring UI | L2; events from L3/L4 | MCP retrieves older/new events without full-spool scans; UI shows identical project/task/session/message/transfer IDs and usable copy/details |
| L6 | G24,G25,G28,G30: opt-in startup/sign-out, packaging/config migration/publishing safeguards and documentation | Migration design L1; stable contracts L2–L5 | Old data readable, no new rights inferred, prepared rollback works; startup stays disabled unless explicitly selected |
| L7 | G26 plus all gates: real-client end-to-end acceptance and final audit | L3–L6 | Current assistant uses MCP for ordinary Codex/Claude/project/file workflow; matching UI, recovery and permissions checks; every register row closed or explicitly arbitrated |

Introduce durable data columns/collections and one migration owner before allowing new scoped writes. Preserve original IDs, output, timestamps and unknown status. A user-owned existing session cannot be interrupted or reassigned just to make migration easy. Configuration preview/backups precede authorized application; unresolved mappings are read-only until assigned with authority.
For startup, prefer the simplest per-user stable launcher registration with a reversible owned entry; compare it against a scheduled task only if lifecycle requirements actually need the latter. Implementation must not enable either during installation or qualification without the corresponding explicit opt-in/GO. Windows sign-out behavior must be measured, not inferred from clicking Quitter.

## Failure decisions and qualification gates

| Situation | Planned response |
| --- | --- |
| Caller/project/session/recipient mismatch | Refuse before dispatch; return scoped error; record denial without exposing another project's content |
| Workspace absent, moved, linked or permission revoked | Explain exact context, fail closed for new effects; no automatic recreation, relocation or broadening |
| WSL/client/tmux absent or stopped | Model-free health/preflight; explicit permitted start policy; actionable unavailable state; no silent install |
| Authentication, startup menu or native approval needed | Report attention/awaiting approval with safe handoff; no login automation, update installation or automatic approval |
| Native session/context lost | Keep task/history, identify loss and require explicit restart/new conversation; do not fabricate continuity |
| Deadline, disconnect, cancellation or restart after possible dispatch | Preserve unknown/interruption requested, inspect receipts/state, never replay automatically |
| Host/SQLite/disk write fails | No successful acknowledgement before durability; retain last usable runtime; diagnose without deleting evidence |
| Transfer disk full, source change or collision | Preserve final destination until valid publication; report failure/uncertainty by phase, identify owned partial; cleanup only explicitly owned resources |
| Permission/configuration changes while work runs | Apply defined revocation/admission policy and retain captured execution context; no silent mutation of native permissions |
| Old configuration or protocol is incompatible | Refuse unsafe interpretation with upgrade/rollback guidance; preserve source data and secrets |
| Real desktop MCP is still unavailable | Continue independent authorized work; keep final acceptance blocked; request only the exact supported human action, never claim a standalone substitute |

Use a few reusable scenarios, not tests mirroring every method: two-project authorization/routing (including reads and forged reply IDs); neutral adapter contract; two native-client short exchanges and explicit continuation; parallel independent sessions and conflicting workspace admission; durable recovery/no replay; filtered incremental history; file nominal/near-limit/integrity/collision/interruption; actual UI/copy/tray/opt-in lifecycle and current-chat MCP.
Ordinary test commands remain deterministic and do not invoke WSL/models. Native/OS lifecycle tests are separate explicit phases with owned resources, paid-intent markers, receipts and a bounded prompt budget. Qualify Claude login model-free when possible before paid work. Do not reuse expired historical replies as current acceptance.
Never stop a shared WSL distribution using yesterday morning's permission. Distribution shutdown/sign-out tests require a current safe window or an isolated test environment; otherwise document that specific qualification as open. Preserve human tmux access and unrelated /home/sowapps/projects.
Architecture checks verify dependency direction and absence of client/OS names in neutral domain/use cases, plus a non-WSL adapter contract. This establishes extension without delivering an unrequested SSH/remote agent product.

## Transfer evolution triggers

The [current transport reference](../../doc/developing/file-transfer.md) owns actual limits. Native UNC/9P remains the default WSL candidate; no speculative replacement is part of correcting the architecture. First remove avoidable per-file process/metadata overhead where measurements justify it.

| Trigger | Candidate response | Decision evidence and tradeoff |
| --- | --- | --- |
| Thousands of small files are an actual workload | Batch manifest and one streamed archive, with validation/extraction at the endpoint | Compare throughput/files-per-second and process/metadata cost; constrain expanded size, names, symlinks and per-file failures; more complexity than single-file operations |
| Files exceed the supported V1 profile or interruptions require restart | Progress-aware streaming, chunk verification and explicit resumable checkpoints | Compare near-limit/growing files, cancellation and disk consumption; design source-change identity and stale partial handling before claiming resume |
| Repeated transfers change only a small portion of a tree | Differential/synchronization adapter, potentially using an existing endpoint tool | Needs explicit deletion/conflict/metadata/permission policy; not implied by workspace membership or a transfer tool |
| UNC effective identity differs from the configured Linux user, or UNC is unavailable | Fixed helper-owned binary stream under the explicitly selected user; other local transport only after comparison | Identity/correctness can justify an alternative without a speed claim; account for process lifetime, framing, cancellation and packaging |
| Another machine/OS is requested | Adapter for the selected endpoint transport, e.g. SSH/SFTP if qualified | Neutral ports/project policy unchanged; endpoint authentication/availability and transport-specific guarantees must be qualified |

When useful, benchmark representative small-file batches and sequential files at small/near-limit sizes. Measure wall time, bytes/files per second, process count, memory/CPU, hashing time, free-space needs and cancellation/recovery, under comparable warm/cold conditions. Keep benchmarks separately authorized and proportionate; never fill the operator's disk or claim a universal fastest protocol from one workload.
For V1, deliver documented single-file bounds, coherent deadlines, progress/cancellation and receipts. Folder sync, unbounded large-file support and resumability are future options unless the approved V1 scope explicitly selects them; record that supported-profile decision instead of hiding a gap.

## Definition of V1 completion

Every G01–G30 row has a linked implementation location, focused acceptance result and documentation owner. A bounded supported feature profile is explicit; original mandatory requirements cannot be silently deferred. Any remaining product-scope exception requires Florent's arbitration and remains visible in the final report.
Actual MCP use in the current client, both native agent profiles, project/caller isolation, useful sessions/continuation/parallelism, verified project-scoped commands/files, durable no-replay recovery and consistent monitoring are demonstrated together. No success inferred from tool count, generated config, separate protocol clients or compilation alone.
Current docs describe only shipped behavior and accepted target; dated design/observations remain in notes. Measurements are not promoted to operator evidence. Record baseline/end quota and timing, native prompt counters and objective accounting limitations; close each tracked objective only when its actual work is complete.
Present the implementation scope from these lots for a separate explicit GO. This planning request authorizes no code changes, runtime interruption, model prompt, configuration mutation or automatic startup registration.
