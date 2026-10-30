# V1 implementation acceptance

**Date:** 2026-10-29  
**State:** implementation and qualification in progress; not a declaration of complete V1 acceptance.  
**Authority:** scoped GO in evidence2026-10-29-go-authorizes-v1-correction-plan.

## Interpreting results

An implemented mechanism and a passed deterministic scenario do not establish native/visual/current-client acceptance. Measurements remain observations awaiting Florent's validation. Paths below are relative to apps/agent-relay unless prefixed with workspace local/. The implementation owner is [Projects, participants and adapters](../../doc/developing/projects-and-adapters.md); the complete planned closure criteria remain in the [gap register](../proposals/2026-10-29-v1-correction-plan.md).

## Gap matrix

| Gap | Implementation and focused result | Remaining qualification or correction |
| --- | --- | --- |
| G01 | RelayProjects/OperationScope; two-project real-pipe isolation and native scoped command/file/message identities passed | Current desktop workflow must match the same IDs |
| G02 | Membership roles independent of products/OS; authoritative principal/recipient routing; two isolated principals passed | No additional machine adapter delivered |
| G03 | DPAPI ConnectionStore, per-request authentication, project-bound MCP, forged scope/revocation negative cases passed | Current desktop connection still not loaded |
| G04 | Central ProjectPolicy; deny-by-default capabilities/workspaces/command IDs; native-work versus transfer-write separation passed | Native OS grants remain an additional boundary |
| G05 | Existing real workspace separate from session runtime; real Codex created validation-v1.txt in its assigned workspace | Production user workspace must be explicitly configured; no legacy inference |
| G06 | Neutral RelayApplication/ports/artifacts; non-WSL memory adapter executes actual use cases | Only WSL/local-files production adapters shipped |
| G07 | Shared ProjectCoordinator application route for authenticated MCP/UI/maintenance; legacy mutations denied | Local echo remains a labelled regression adapter |
| G08 | Task/operation/attempt IDs, durable state events, scoped list_events; transition/idempotence scenario passed | Task workflow remains caller-directed, no autonomous scheduler |
| G09 | Distinct process/MCP/auth/approval states; bounded probes; operator confirmed account expiry and login restoration | Lost-helper/startup/approval race matrix not completely qualified natively |
| G10 | Pinned native conversation IDs; explicit Codex close/reopen recovered AMBRE_731 in one continuation | Claude explicit native continuation not paid-tested; no automatic extra prompt |
| G11 | Short lifecycle gates, independent-session and host admission; workspace resource leases | Real simultaneous model work deliberately not multiplied; external terminal writes uncoordinated |
| G12 | Phase-aware unknown/awaiting_approval, targeted cancellation and late-message resolution; deterministic no-replay interruption passed | Native timing races and close/reopen interruption require additional isolated qualification |
| G13 | Pending durability, startup unknown conversion, prepared command dispatch nonce and scoped transfer receipts; matching/foreign receipt tests passed | No complete publication-before-receipt recovery when independent confirmation is unavailable |
| G14 | Literal JSON message text, preserved newlines/Unicode; configurable nativeExecutable and Linux user | New executable binding applies to new sessions; existing private roots retain their pinned context |
| G15 | Native proactive progress/blocker policy; ordinary Codex task produced independent message | Claude short ordinary task produced only final correlated reply; natural initiative criterion remains open |
| G16 | Native SQLite index/cursors, bounded rotating imports, deduplication; Unicode frame-budget scenario passed | Long real native retained-history benchmark not performed |
| G17 | Project/endpoint/session/task/kind/state/content/date filters; count/byte cursors and durable events | UI visual acceptance remains open |
| G18 | Read-only EndpointDiagnostics and inspect_host; real effective-user/tool inspection passed without a model | No general configuration/secret dump or arbitrary shell |
| G19 | Neutral scoped artifacts, independent source/destination/replace rights, explicit UNC identity check; real round trip/SHA passed | No non-WSL byte transport implementation |
| G20 | Configurable size/deadline/progress/cancel/receipts; inspect_transfer/cleanup_transfer passed through actual MCP in both directions, preserving final destinations; native helper operation lock prevents active-helper cleanup | Disk-full/source-mutation/interrupted-publication fault scenarios remain unqualified; uncertainty is preserved |
| G21 | Real256MiB upload4.125s/download2.922s, SHA verified; oversize/collision refused; eight small uploads6.844s | One local observation, not a throughput guarantee or proof for thousands of files; no directory sync/resume claim |
| G22 | Protected references separated from workspace, bound sender, scoped reads, actual user verification | Same-user OS isolation is not promised; Claude environment refresh requires explicit reopen |
| G23 | ProjectWindow, matching operation/message cards, copy/raw details, filters/session/task/load-more, selected real context | Automatic capture target mismatch blocks visual acceptance; no workaround capture attempted |
| G24 | Explicit per-user startup opt-in and stable launcher, shared Quitter path, SessionEnding handler | Registry option deliberately not enabled; actual Windows sign-out/distribution interruption not qualified |
| G25 | Additive schema4, copied preflight, rollback snapshot including protected credentials; injected post-migration failure restored database/pointer and a healthy old host | Real older schema3 distribution downgrade not fault-injected; no implicit legacy project mapping |
| G26 | Stable launcher/connection binding and24-tool version2 facade; scoped project-local Codex fragment prepared for the qualification instance | No agent_relay tools available in this desktop conversation; registration is not acceptance |
| G27 | Bounded line reader,1MiB input/output/frame budgets, schemas/unknown-field denial, explicit protocol/errors | Broader installed-client protocol/version negotiation remains a real-client gate |
| G28 | Content-addressed staging, copied preflight, exact PID/executable/data checks, inactive-work guard, snapshots/health/pointer ordering; invalid preparation preserves host and injected migration failure rolls back successfully | Initial qualification exposed missing protected-file copy and an unclosed Python SQLite reader; both corrected and verified; quiesce callers for cross-version deployment |
| G29 | Native approvals retained, awaiting_approval state and post-dispatch-auth unknown state passed deterministic tests; human handoff/external-effect rule documented | No dangerous command or external effect was used merely to provoke an approval test |
| G30 | Current architecture/usage/transfer/UI/target docs reconciled, fixed bilingual terms, dated checkpoints | Frozen tracked objective cannot be replaced honestly; final account/time report pending |

## Repeatable artifacts

Deterministic suite local/apps/agent-relay/tests/89cfb497d55a4592aab34b36910bb525 passed build, SQLite/pipe/use cases, scoped transfer-receipt recovery and bounded-line resynchronization, plus Windows process smoke. Shared tools/verify.ps1 passed documentation reachability/links/evidence, syntax, entrypoint and isolated toolchain contracts.

Native acceptance manifest: local/apps/agent-relay/v1-qualification/v1-acceptance.json. Initial Codex task, Claude task and Codex continuation each have one paid-intent marker; three total user prompts in this workstream. No paid retry of Claude's absent independent message. Native/helper updates were restricted to these three owned session UUIDs.

Publication artifacts: local/apps/agent-relay/reports/deployment-qualification/ and reports/v1-publish/. Current qualification host metadata is reports/v1-publish/host.json, mirrored to v1-qualification/ui-host.json. Normal deployed data has not yet been migrated by this workstream.

Final deterministic suite: local/apps/agent-relay/tests/fc8e8475a56f40a8bd0d546d4b7bb120. Model-free I/O observation: v1-qualification/io-1f83381f81b948da8e3c65567623b385.json. Scoped partial maintenance: v1-qualification/partial-maintenance.json. Publication negative/rollback result: tests/deployment-4ddf3bfc7cb94873ac71717a9284e222/result.json. All passed. No additional model prompt was used for these scenarios. Shared verification also completed successfully. Binary I/O fixtures were explicitly removed after verification; the logical-byte cleanup count and durable receipts remain in the report.

Accounting checkpoint: baseline70%, final account observation79% at07:19UTC, both reset credits intact and no redemption. The original tracked objective remains blocked at its frozen145775tokens/901seconds, because actual tools in this desktop conversation have not been demonstrated. A new goal was refused while it exists; those counters do not measure V1 implementation. L0 snapshot at05:23:58UTC to this checkpoint is approximately1h56. Native dispatches were two Codex prompts and one Claude prompt, with metrics retained in v1-qualification/native-usage.json, separate from the main agent's consumption.

The project-local .codex/config.toml agent_relay fragment now targets the V1 qualification data directory and its protected main-a connection. Global Codex settings and the normal deployed application/data are retained unchanged. The qualification project is not silently promoted to a production workspace or granted rights on /home/sowapps/projects.

## External gates

The current desktop must reconnect through a supported client lifecycle before first-class tool acceptance is possible; its settings do not expose the previously suggested Restart button. Do not automate Codex UI or kill an internal backend. Automatic approval review rejected further computer-use state reading after the selected Agent Relay target returned unrelated content; visual acceptance remains manual or requires an actually resolved safe targeting mechanism. Do not describe or retain unrelated captured content.

OS sign-out/shared WSL shutdown needs a current safe window or isolated OS, not a stale morning authorization. Native model testing remains proportionate to Florent's warning; no extra Claude prompt is inferred from successful authentication.
