# MVP closure and V1 framing

**Date:** 2026-10-28  
**Status:** proposed execution plan, not implementation authorization.  
**Utility:** separates a verifiable MCP-first MVP closure from a bounded V1.  
**Usage:** review this plan before a scoped development/qualification GO.

## Authorized preparation

Florent: « Ok, prépare le plan pour clôturer le MVP et cadrer la V1, avec l’usage réel via MCP comme priorité. Toujours sans commencer le développement. »
This work changes planning documents only. No code, installation, application/agent startup, WSL operation, MCP configuration change or native model prompt.
The earlier integration objective is unfinished and blocked. Its frozen counters cannot account for this planning lot; do not close it as achieved merely to obtain another objective. Capture account-quota snapshots before/after preparation and retain their precision limits.

## Product priority and known baseline

MCP is the primary usage channel (evidence `2026-10-28-mcp-is-primary-usage`). The Windows application observes, explains and debugs the same durable operations; it is not a mandatory operator console for every agent action.
Preserve the glossary: Relay project — Projet relayé; Workspace — Espace de travail; Installation directory — Dossier d'installation; Data directory — Dossier de données.
WSL-to-Windows relay capability remains messages only. An agent's separately authorized local WSL tools are distinct from requests to execute a Windows command through the relay.
Existing paid qualification proves one Codex exchange, one Claude exchange and independent messages in isolated fixtures. It does not establish current-chat MCP usability or reliable normal-profile startup.
The actual six-tool facade exposes execute_command, send_agent_message, get_result, list_results, read_inbox and list_profiles. State/start controls exist elsewhere, so current MCP-only use lacks readiness/start support.
The project trust entry is applied. The installed client settings screenshot shows node_repl and phpstorm, not agent_relay. Whether that screen excludes project-scoped entries is not settled; inspect the actual conversation registry before drawing a configuration conclusion.
The visual refresh, custom icon and in-window Quitter are implemented. Operator feedback accepts the general improvement, reports insufficient header-icon resolution and a basic inbox presentation, and identifies unclear operation labels/raw errors.
The normal-profile listing starts in its dedicated command/workspace folder, not automatically in the user's repository. The current working directory is not a security boundary, and the application grants no implicit administrator privileges.

## MVP closure: ordered work packages

### M1 — Establish the real MCP connection first

Inspect the actual Codex Windows configuration layers, current-thread server/tool registry and startup errors. Verify the selected runtime and data directory. Determine the supported reload behavior before requesting an operator action.
Prefer the existing project-local configuration. If it cannot serve the actual client, prepare the smallest alternative with exact scope/diff/backup; a new global registration is not covered by the prior project-trust approval.
Do not interrupt other conversations. The installed Restart action targets the Codex app-server; do not present it as a harmless per-server refresh. If an interruption is indispensable, describe its concrete impact and wait for explicit authorization/safe timing.
Exit gate: the assistant in this existing conversation discovers Agent Relay tools through the real client and calls a harmless profile/list operation. A standalone native app-server or hand-written stdio test remains supporting evidence only.
Stop before further product work if the client cannot expose the server; preserve the specific error and exact operator action needed. Do not migrate the product to a different client/cloud architecture to substitute for the requested end state.

### M2 — Complete the minimum MCP agent lifecycle

Expose the configured agent state and explicit startup through MCP, using the existing host/session controller. Recommend two narrow operations for status and startup; settle their names/contracts during design rather than silently adding another general control API.
Return distribution, Linux user, configured workspace, client and readiness reason. Distinguish not started, starting, ready, attention required and unavailable. Do not claim ready merely because tmux/process creation succeeded.
Reconcile an expired wait with observed session/MCP state; a continuing startup must not be reported as a user cancellation. Never silently resend a message after an uncertain dispatch.
The nominal agent request must be feasible without clicking Démarrer in the application. Startup remains explicit and model-free; permissions and native approval requests remain authoritative.
Exit gate: this conversation can identify the correct Codex/Claude profile, request startup and establish readiness or an actionable reason without the UI.

### M3 — Make the MVP understandable and diagnosable

Use the same operation/message identifiers and structured data through MCP and the application.

| Gap | Minimum closure behavior |
| --- | --- |
| Ambiguous ls | Show command name, executable/arguments, distribution, user and actual working directory before execution; show targeted paths from arguments as well as the starting directory. Confirm the actual execution context in the result. |
| Unnamed operation | List the action and destination/context, not just Terminé and a profile. Preserve a readable preview, state, time and link to its result. |
| Raw JSON errors | Expose a stable error code, concise actionable explanation and separate technical details. A missing/initializing session should be understandable without interpreting Python JSON. |
| cancelled followed by startup | Qualify the mismatch and report observed startup/readiness; preserve uncertainty instead of false success or cancellation. |
| Basic inbox | Match the operation list/detail presentation; identify sender, time and whether a message is a correlated reply or independently initiated. |
| Copying | Provide copy actions for sent/received text, results and raw diagnostics. Clipboard text must match the chosen content. |
| Blurred header mark | Use a suitable high-resolution asset from the existing vector, independently of the small Windows icon bitmap. No logo redesign. |

Technical details remain available on demand; do not replace normal explanations with a debugging-only window. Do not create an unrestricted administration console or a full monitoring platform in this package.

### M4 — Prove ordinary agent-initiated messages

Native endpoint instructions must explain when to send an independent progress, clarification or blocker message while doing authorized work. The operator should not have to name an MCP tool or a correlation parameter.
Use a short ordinary task that naturally requires reporting a blocker/clarification, in a dedicated qualification workspace. Verify the agent's independent message and its separate task reply; merely prompting it to reproduce a technical tool invocation is insufficient user-workflow qualification.
Both Codex and Claude use the same message-only contract. Independent messages have their own identity/origin and no mandatory reply identifier. A transport acknowledgment is not Windows receipt.
MCP read_inbox must retrieve them while this Windows conversation is active, with repeatable identity and no duplicate import. The application must show the same records. Automatic wake-up of an inactive chat is not claimed by this MVP.

### M5 — One final acceptance through this conversation

Run the following through the actual MCP client; keep the application open only as a matching observer/debugger:

1. Discover the toolset, profiles and actual state/context.
2. Run one authorized direct listing in the explicitly displayed directory and retrieve its confirmed result. No WSL AI agent participates in that path.
3. Start/inspect Codex readiness, send one short ordinary task, retrieve its genuine correlated reply and independent message.
4. Repeat the agent case with Claude; do not infer support from Codex success.
5. Retrieve messages again and confirm stable identities/no duplicates. Compare selected operation/message IDs between MCP and the application.
6. Restart only the owned relay host at a safe idle point; reconnect and retrieve retained results/messages without replaying work. Run bounded local fixtures for unavailable/timeout/unknown and directionality refusal instead of consuming another model exchange.
7. Confirm no raw-JSON-only nominal error, false startup cancellation or unclear listing context. Verify copy actions and final visual changes once.

Recommended model budget for a subsequent explicit GO: one short user exchange per native client, capable of producing an independent message and a correlated reply in that exchange. No automatic paid retry; inspect state/quota before any proposed additional attempt and stay within the authorized budget. No existing agent sessions or /home/sowapps/projects.
Capture quota before/after, model usage if supplied, duration, versions, request/message IDs, effective contexts and retained receipts. Shared account percentages are not exact task token counts and may include other sessions; unchanged rounded percentages do not mean zero consumption.
Reuse working .NET/SQLite/native adapters, staging, existing deterministic tests and scripts. Add only focused cases for changed contracts/lifecycle/error mapping and the nominal user path.

## MVP acceptance gate

The MVP is proposed for Florent's validation only when M1–M5 are supported by evidence from the current client, both real native agents and the same app-visible durable records.
No success claim based solely on generated configuration, a separate test client, a UI click or historical isolated acceptance. Any missing item remains explicitly open.
Update current documentation in doc/, dated observations here, and operator validations one fact per notes/evidence/ file. Close the existing integration objective only after its full actual requirements are met; do not silently shrink it to close the visual lot.

## V1 framing: proposed order

### V1-A — Useful MCP observation and retrieval

Define a stable operation envelope shared by MCP/UI: action type, readable label, endpoint, Relay project/Workspace or WSL-host context, lifecycle timestamps, status, correlation/parent IDs, result/error and referenced artifacts.
Add bounded pagination/cursors and filters for agent, task/exchange, operation type, state and date; search readable labels/content. Unread/message cursors must allow retrieving new messages without repeatedly returning an ever-growing inbox. Define marking/read semantics explicitly.
The UI groups by task/exchange, shows independent agent events distinctly, and lets users open human-readable or raw details and copy/export the selected content. Preserve saved history when schema/contracts evolve.
Do not render every future MCP operation as a bespoke screen: use shared summaries/details with specialized views only where they help understand commands, messages or transfers.

### V1-B — WSL host knowledge and project workspaces

Separate access to a Workspace from authorized inspection of the WSL host. Expose distribution/user, tools/versions, services and relevant configuration presence through explicit diagnostic operations and native-agent permissions.
Show effective user, starting directory, targeted paths and scope before a meaningful action. Do not treat cwd as confinement or administrative authority. Do not collect credentials or dump unrelated configuration; expose the information needed for the task and retain native permission controls.
Specify how an agent can determine installed/configured capabilities through MCP and its permitted local tools. Host inspection must not depend on a misleading ls of the application fixture directory.

### V1-C — Explicit file transfers

Make Windows→WSL and WSL→Windows transfers proper MCP operations, also observable in the application. Use local transport, not a mandatory external cloud service.
Require explicit source, destination endpoint/path and authorization scope. Keep installation/data directories distinct from project workspaces.
Before transfer: identify file/directory intent, count/size and collision policy. Recommend failing on an existing destination by default; replacement requires an explicit authorized choice. Inspect symlinks and resolved paths so scope is not bypassed.
During/after transfer: progress/cancellation state, temporary destination then safe finalization, size/integrity confirmation and durable receipt. Do not report success until the destination is verified. Do not automatically retry an uncertain overwrite.
Start with individual files. Decide directory transfer, limits, symlink/permission handling and resumability during technical framing before development; no automatic continuous synchronization is implied by workspace association.

## V1 boundaries and decisions

Keep one active external task initially. Multiple concurrent agents/tasks, background scheduling, automatic chat wake-up, service/sign-in startup and unrestricted remote administration are separate proposals, not hidden prerequisites of this V1.
Retain plain user-facing language and raw diagnostics on demand. Preserve WSL→Windows messages-only directionality and project-specific authorization for file/command effects.
Before V1 implementation, settle versioned contracts/history migration, retrieval/read semantics, host-versus-workspace permission scope and transfer collision/limit rules. Technical choices belong to the agent; only product tradeoffs or effects requiring authorization go to Florent.

## Next authorization

Present a concise scope for M1–M5, including the bounded two-client model budget and any exact client/global change separately. Await explicit GO before development, runtime checks, configuration edits or prompts.
V1 implementation requires a later scoped GO after its framing is reviewed. Acceptance of this plan is not a development GO.
