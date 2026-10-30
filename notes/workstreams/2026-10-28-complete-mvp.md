# Complete MVP workstream

**Date:** 2026-10-28  
**State:** complete; tracked objective closed.

## Authorization

Retained operator scope: evidence `2026-10-28-go-authorizes-complete-mvp`.

Florent explicitly approved the complete usable MVP, not another standalone demonstration. Latest GO follows a plan covering Windows configuration/results,
direct WSL commands, real Codex/Claude messages, WSL-originated messages without command capability, persistence and interruption handling.
Authorized model budget: one short exchange per WSL agent, plus one second attempt per agent only if necessary. Record exclusive attempt markers before sending.
Dedicated persistent WSL directories only, outside `/home/sowapps/projects`. Do not touch existing agent sessions or global client configuration.
No Windows service, automatic startup, unrelated feature or additional external effect. Do not stop between in-scope implementation stages for another GO.

## Acceptance and director plan

1. Inventory existing C# contracts and qualified native-client helpers; preserve useful behavior without copying the POC wholesale.
2. Define durable operations (pending/running/completed/failed/unknown), configuration profiles and a neutral command/message port. Commit intent before dispatch; never silently replay uncertain external work.
3. Implement reusable WSL process bridge, dedicated tmux/client integration and message-only inbound delivery. Existing sessions remain human-accessible.
4. Expose a Windows stdio MCP facade forwarding to the running C# host; never start a second relay implicitly. Linux MCP exposes messages only.
5. Complete Windows controls for configuration, command/message submission and retained results/inbox. Clearly distinguish command output, local diagnostics and real agent replies.
6. Run deterministic integration/error/restart checks, then four real cases through the application: ls without agent, Codex reply, Claude reply, unsolicited WSL→Windows message with command denial.
7. Check persisted completed results after restart, bounded timeouts/unavailable/unknown outcomes, cleanup of only test-owned resources, documentation and objective closure.

## Additions and incident policy

Necessary technical additions enter this plan/checkpoint and receive focused verification. Product features beyond these four cases go into a separate backlog without blocking delivery.
Prefer existing AppCommands, isolated toolchain, SQLite serializer, frame codec, runtime staging and actual integration tests. Apply DRY/SOLID with neutral ports and small adapters.
If a native CLI version differs, inspect its local help/config first without prompting a model. Diagnose an outcome from logs and correlated records, not terminal text alone.
Missing session/profile produces unavailable; timeout after possible dispatch produces unknown. Duplicate IDs never dispatch again. Late replies may resolve unknown when correlation is proven.
No paid retry until the preceding attempt's state is inspected. If quota/auth/access blocks a real case, preserve proof and report the actual boundary instead of declaring MVP ready.

## Existing state and checkpoint

First C# slice builds and passes 13 core/SQLite/pipe integration checks, actual tray/client restart tests and runtime-staging regression.
App projects: Core, Infrastructure, Windows, Tests. Root build/test/run/doctor tools and private SDK are functional; Microsoft.Data.Sqlite10.0.14 is pinned.
The current responder is only local echo. No MCP or WSL application adapter exists yet. Previous POC native integration is reference material only.
Visual UI capture previously timed out waiting for computer-use app approval; do not assume a screenshot or button interaction was verified.
Current work: objective created, read root/app rules, architecture and prior qualification runner. Next: inspect native controller and current C# contracts; qualify current WSL environment without model calls.

## Checkpoint — implementation in progress

WSL inventory: Ubuntu-24.04/sowapps, tmux3.4, CodexCLI0.153.0, Claude2.1.312; no ancestor instructions under /srv.
Prepared dedicated root `/srv/agent-relay-mvp-0648bcc5a394480ca524d1ee2d461921`, owned sowapps, with separate command/codex/claude workspace/data/config and source helpers.
Windows qualification data/config at `local/apps/agent-relay/mvp-qualification/`; provision.json confirms zero model calls and credential references only.
Implemented neutral WorkService (intent/pending/running/completed/failed/unknown, duplicate guard, no replay), SQLite schema2 migration/work/inbox and C# WSL executor.
Python helpers under application wsl/: durable file module, messages-only MCP server, native client controller, JSON process bridge.
Windows stdio MCP facade has six tools and forwards to host. CLI submit/result/history/inbox/profiles/start-profile added; legacy local tests preserved.
MVP window now has profile/command selection, submit/start/configure, operation history/result and received inbox. Profile editor saves validated central configuration.
Application builds and the previous full local suite/process smoke pass. Need additional work-state tests and real acceptance.
Native clients have NOT yet received any model prompt. Current action is the model-free local acceptance (real ls, MCP tool list/get_result, start idle native sessions).
Runtime metadata path `local/apps/agent-relay/mvp-qualification/runtime.json`. An initial import-scope error prevented creating it; fixed by importing RuntimeStaging after New-AppCommandContext.
Official Codex MCP documentation was fetched at https://learn.chatgpt.com/docs/extend/mcp?surface=cli; tool allow/approval settings align with qualified native config.
Next: finish local acceptance, inspect startup MCP readiness, deterministic operation tests, then one short real exchange per native agent with exclusive markers.

## Checkpoint — real acceptance passed, finalization in progress

All four real cases passed on first paid attempts through C# application: ls; Codex correlated reply; Claude correlated reply; independent messages from both agents.
Windows proof files: mvp-qualification/acceptance-local.json, acceptance-agents.json, acceptance-codex.json, acceptance-claude.json, denial-and-usage.json.
Direct ls request9ebe6056-ea83-4f34-9a03-959e9c10099c; Codex15ed64c4-9c92-40c9-a12a-31f409788581; Claude87d8da1c-7445-4e82-8d12-74c8f83d51c9.
Exactly one exclusive dispatch marker each. Codex native usage20516 tokens (20318 input incl9984 cached,198 output); Claude4 input+1591 cache creation+1181 cache read+378 output,2 assistant API messages within one user exchange.
Message-only MCP denial verified for both clients: tools/list only send_message and forged execute_command returns isError. No extra paid prompts authorized/needed now.
Implemented WorkScenarios tests for once-only intent, pending duplicate, conflict/busy/capability denial, interrupted→unknown/no replay and completed persistence. All pass with actual SQLite.
Added central AllowLocalWork option (defaultfalse); native per-profile launch uses project tools only when explicitly enabled and preserves native approval controls. Changing running-session permissions is refused, not silently applied.
Added GUI provisioning via packaged helpers; no manual .NET prep. Source process runner shared by provisioning and WSL adapter.
Unknown operations can reconcile from existing native message/command receipts on explicit refresh, without replay. Config changes blocked while active. Stored inbox remains readable without WSL.
Recovery test initially compared full JSON dicts and failed only because optional WorkResult.details=null was added; fixed to assert unchanged request/state/result and original update timestamp.
Current tasks: final no-prompt recovery validation and fault cases, GUI/default configuration, user docs/MCP fragments, exact cleanup/metrics. Do not send more agent prompts.

## Checkpoint — ready for final verification

Recovery of both completed native replies and four inbox messages passed after host restart without prompt replay. Fault injection into the Codex proof record resolved unknown from the existing outbox (fault-recovery.json); timestamp legitimately updated.
Final neutral result also retains command program/args/workspace/user/nonce details. Explicit refresh can recover late command receipts without executing again.
Prepared normal-use configuration through the actual C# provisioning path at `local/apps/agent-relay/data/relay-config.json`.
Normal-use WSL root `/srv/agent-relay-mvp-a70aa9a4cf81428cb176440a4b856724`; profiles command/codex/claude-3e63d3d8, permissionsfalse by default, no models prompted.
Normal-use visible-window lifecycle and real ls passed (data/installed-smoke.json); requesteef175fd-e2e1-47fd-947a-8870fc6a40c2, user sowapps, nonce validated.
MCP Windows JSON/Codex TOML fragments exported to normal data directory. No global client setting changed. GUI provisioning ships Python helpers with runtime.
Latest C# build/test passes 13 legacy integration +7 durable-work checks, staging regression and tray/client smoke. Await cell1142 result if needed.
Current docs: using/mvp.md, developing/mvp-architecture.md; stale local-only using index rewritten. No new paid call is needed.
Remaining: stop only qualification-owned native sessions while retaining proof data; regenerate final runtime/MCP fragments after last UI name-display edit; final full checks/docs/evidence/report; goal closure.
Normal-use profiles/root must remain available. Do not stop WSL distribution or unrelated tmux/native sessions.

## Final checkpoint

All implemented functional acceptance and deterministic checks pass. Native model budget consumed exactly one short user exchange each; no more prompts needed.
Normal-use C# prepare, MCP export and visible-window/real-ls smoke passed on the final runtime. Latest normal ls319d6b66-f150-4926-82bf-fe88e8626eb6, with full execution details.
Qualification-owned tmux server and six verified descendant processes were stopped; data retained, default root untouched. No distribution/global session shutdown.
Current default config/MCP fragments in local/apps/agent-relay/data. Current docs describe WSL MVP, native local-work permission, queue/delivery semantics and recovery.
Old runtime copies are being cleaned while retaining the one referenced by mcp.windows.json; resource/cleanup JSON under local/apps/agent-relay/reports/mvp/.
Remaining only final documentation/shared checks, native-call count confirmation and objective metrics/closure. No additional product work or paid invocation is authorized by this completion checkpoint.

## Delivery checkpoint

Final build has zero warnings/errors; thirteen local integration cases, nine durable-work cases, staging regression and tray/client smoke pass.
Final test artifacts `local/apps/agent-relay/tests/07f6eb0f09de4e26904293da48bf8baa/`.
The final runtime/MCP fragments reference FB3BD279B4F9291278D49D63BB57F9F5ED5FA24EE668EC14AC8B8805AEB0EB54,2694548 bytes.
Both real agent exchanges succeeded once; paid second attempts unused. Qualification server/process cleanup confirmed; normal data/root remain ready.
User launch: ./dev.ps1 run -App agent-relay -Offline. Native clients start only on explicit UI action. No global MCP registration or startup setting changed.
Source/doc implementation and objective are complete. Final objective counters:249748 tokens,3332 seconds (55min32s).
No work remains in the authorized MVP scope. Do not send another model prompt or start a follow-up feature automatically.
