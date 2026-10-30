# Complete WSL MVP qualification

**Date:** 2026-10-28  
**Status:** measured results awaiting operator validation, not promoted to operator evidence.

## Authorized implementation

The complete-MVP GO and paid-attempt limit are retained as evidence `2026-10-28-go-authorizes-complete-mvp`.
Implemented direct WSL command execution, actual Codex/Claude messaging, Linux-originated messages, Windows MCP, profile/permission editing, dedicated provisioning and durable results/inbox.
The neutral work service commits intent before dispatch, rejects conflicting identities and concurrent work, and never automatically replays uncertain operations.
SQLite schema 2 migrates the existing local exchange database without discarding those exchanges.

The Windows UI owns submission, native-session startup, history/result/inbox and central profile editing. Its provisioning button delegates to the same packaged script used by the tested C# prepare command.
Process execution, JSON/framing, data schema, runtime staging and build/test tools each retain a single reusable owner.
No existing WSL project, unrelated agent session, global client configuration, service or automatic startup was changed.

## Actual four-case acceptance

Qualification root: `/srv/agent-relay-mvp-0648bcc5a394480ca524d1ee2d461921`, Linux user sowapps, Ubuntu-24.04.
Native versions were tmux 3.4, Codex CLI 0.153.0 and Claude Code 2.1.312. Private instruction ancestry was checked before creation.
Native clients used isolated configuration/workspace/data and credential references, never copied credential values. Both MCP endpoints initialized before a prompt.

| Case | Actual result |
| --- | --- |
| Direct WSL command | Request 9ebe6056-ea83-4f34-9a03-959e9c10099c returned alpha.txt and beta.txt, exit 0; no WSL model involved |
| Codex reply | Request 15ed64c4-9c92-40c9-a12a-31f409788581 completed with CODEX_MVP_REPLY and the same UUID from codex-wsl |
| Claude reply | Request 87d8da1c-7445-4e82-8d12-74c8f83d51c9 completed with CLAUDE_MVP_REPLY and the same UUID from claude-wsl |
| WSL-originated messages | Both clients sent independent MVP_INIT messages with no inReplyTo; Windows retained them alongside correlated replies |

The tests ran through the actual C# Windows host/client. The Windows stdio MCP initialization, six-tool catalog and retrieval of the same durable command result also passed.
Both native Linux endpoints advertise only send_message; forged execute_command calls returned isError. Message text is never executed as a reverse command.

The first attempt succeeded for each agent; no second model prompt was sent. Exclusive Windows and Linux dispatch markers establish one user exchange per native client.
Codex's native record reports 20,516 tokens: 20,318 input (including 9,984 cached) and 198 output.
Claude's native records report 4 input, 1,591 cache creation, 1,181 cache read and 378 output tokens; two assistant API messages occurred within its one user exchange.
Those native counters are separate from the coordinating objective's consumption and are not a monetary price estimate.

## Persistence, faults and delivery semantics

Both completed real replies and all four inbox entries survived a host restart without another model invocation.
A later optional result metadata field required comparing semantic fields rather than full JSON-object shape; request, state, output, origin and original timestamps were unchanged.
A separate fault check deliberately marked the Codex qualification record unknown, then recovered it from its existing native outbox receipt without replay.
That reconciliation legitimately updated its timestamp; the original acceptance and subsequent fault record are both retained.

The deterministic work suite covers once-only intent, pending duplicate, conflicting identity, busy admission, denied capability, interrupted intent→unknown/no replay and completed persistence.
It additionally verifies a deadline failure is unknown and a certain unavailable endpoint is failed. A canceled bridge does not deliberately launch new work.
The earlier thirteen local exchange/SQLite/pipe cases, runtime-copy regression and actual tray/client process smoke continue to pass.
Explicit refresh reconciles late messages/command receipts; completed records and the imported inbox are readable locally without WSL.
Settings changes are refused while an operation is active. Local-work permission defaults off and changes native tools only for a newly started dedicated session, with native approvals preserved.
The Linux tool acknowledgment denotes a durable queued outbox entry, not immediate Windows delivery; confirmed Windows delivery follows its import/commit.

## Normal-use installation

The actual C# prepare path created the normal-use profiles under `local/apps/agent-relay/data/relay-config.json`, preserving that data directory's existing database.
Its WSL root is `/srv/agent-relay-mvp-a70aa9a4cf81428cb176440a4b856724`; profiles are command/codex/claude-3e63d3d8.
The visible-window lifecycle and a real configured listing passed through that setup with zero model prompts.
Request eef175fd-e2e1-47fd-947a-8870fc6a40c2 records /bin/ls -1, sowapps, the exact workspace, exit 0 and a fresh verified nonce.
Windows MCP JSON and Codex TOML fragments are exported to the same normal data directory. No global client integration was edited or an inactive conversation awakened.

Launch with `./dev.ps1 run -App agent-relay -Offline`. The application is framework-dependent Windows x64 and uses the qualified private runtime.
An installer, automatic sign-in launch, unrelated project migration and multi-agent scheduling are outside this MVP.

## Proof locations and cleanup

Windows proofs are retained under `local/apps/agent-relay/mvp-qualification/`: acceptance-local.json, acceptance-agents.json,
acceptance-codex.json, acceptance-claude.json, acceptance-recovery.json, denial-and-usage.json and fault-recovery.json.
Normal-use smoke is `local/apps/agent-relay/data/installed-smoke.json`. Deterministic suites retain their own unique test folders and logs.
The final normal-use listing also passed as request 319d6b66-f150-4926-82bf-fe88e8626eb6 after the last helper refresh, with nonce 9ae37b2b-865b-4c99-9e3d-701a74850d4e.
Only the qualification-owned tmux server was stopped, with six descendant process identities checked by PID/start time; cleanup is confirmed in process-cleanup.json.
Qualification Linux data/transcripts remain for audit, and the normal-use root remains provisioned. The WSL distribution and unrelated sessions were not stopped.
The final runtime is retained at `local/apps/agent-relay/run/FB3BD279B4F9291278D49D63BB57F9F5ED5FA24EE668EC14AC8B8805AEB0EB54/`.
It occupies 2,694,548 bytes including packaged provisioning/WSL helpers and its marker. Old runtime copies totaling 18,611,004 bytes were removed after process/path checks.
Normal data occupied 32,402 bytes at measurement; exact resource and cleanup JSON are in `local/apps/agent-relay/reports/mvp/`.

## Verification limits

The current window's startup, configured backend and timed close were exercised as real processes. Pixel-level layout and button-click inspection was not completed;
the earlier computer-use app-access request expired. No screenshot or human-equivalent UI interaction is claimed.
Actual model acceptance used messages-only isolated profiles. Native local-work launch controls are implemented but no extra paid file-modification task was sent.
Simultaneous human typing and automated prompt injection into one native session is not qualified; use the controlled relay path while an operation is active.
Cross-account pipe rejection, every possible interruption point and a standalone distribution installer were not qualified.

## Objective consumption

The complete-MVP objective is closed. Its returned final counters are 249,748 tokens and 3,332 seconds (55 minutes 32 seconds).
Native client counters above are reported separately; the objective figure is not a monetary cost estimate.
