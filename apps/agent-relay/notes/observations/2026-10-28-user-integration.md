# Native Windows user integration

**Date:** 2026-10-28  
**Status:** measured observations awaiting operator validation; integration remains open.

## Findings

Florent authorized the user-integration closure plan, reserving global configuration changes for a separate reviewed GO. No additional model turn was sent.
The actual Windows MCP facade passes initialization, six-tool discovery, real execute_command/get_result/list_results, profile/inbox reads and missing-agent-profile refusal before dispatch.
Request `863dcae2-66b2-4101-91c8-1619d4ae57ec` returned alpha.txt/beta.txt, exit0. Previous successful native Codex/Claude acceptance was retained without repetition.
Codex Desktop binary 0.165.0-alpha.1.2 discovers all six tools without toolsError through app-server in a private process. This creates no conversation or model turn.
That report identifies process-only server overrides: it establishes native compatibility, not this desktop conversation's connection.

The user's effective native configuration disables the project `.codex/config.toml` because this project is not trusted. A CLI trust override does not change the early layer loader.
A private CODEX_HOME trust fixture successfully loads the layer. No user setting was changed.

## UI and fixes

Computer Use captured the real window and three-profile editor: Ubuntu-24.04, sowapps and the correct dedicated normal-use root.
Found/fixed a result detail that stayed waiting after submission until manual selection, unnecessary list rebuilding and WinForms LF rendering.
Corrected UI request `8ec945b8-352b-45fa-a68c-a341fa24f8a5` visibly reached completed and showed both filenames on separate lines without manual selection.
Closing the window through Computer Use hid it while the host continued serving that result (window-close.json).
The available window inventory exposes no notification-area target. Physical icon visibility, reopening and Quitter were not visually verified; do not mark them passed.

## Checks and reproduction

Final suite: thirteen exchange/transport cases, nine durable-work cases, runtime staging and actual tray-host/client process smoke pass.
Artifacts: `local/apps/agent-relay/tests/8e942faf3bca410c9b75a72903a2279d/`. Shared documentation/routing/installer/environment verification passes.
Corrected runtime: 511C54D55C16C5DD3C9640A6B5930AFAB7F3FC1864B2004CD1DA7A372B153B7D; exported fragments and project configuration reference it.
Raw reports: `local/apps/agent-relay/reports/user-integration/`, including native-discovery.json, isolated-native-discovery.json, mcp-roundtrip.json, window-close.json and host logs.
Reusable diagnostic: tools/codex-mcp-check.py. `export-mcp.ps1 -CodexProject` refreshes/backups the managed local connection and refuses unrelated settings.

## Pending boundary

The original approval boundary below is historical; the subsequent scoped GO and application are recorded in the following section.

An exact trust addition, original-byte backup and before-hash are prepared in the report directory, applied=false. Target: C:/Users/fhaza/.codex/config.toml.
Apply only after Florent's explicit authorization and an unchanged original hash. Then restart the MCP connection using the supported client action and test the real desktop conversation.
Do not automate Codex UI, restart other conversations or send new native model prompts. Outstanding tray checks must remain explicit. The tracked objective stays open.

## Approved trust and remaining client action

Florent explicitly authorized the reviewed trust addition (evidence `2026-10-28-go-authorizes-project-trust`). The original config hash and identical backup were verified, then only that addition was applied atomically.
codex-trust-proposal.json retains applied=true and the after-hash. approved-native-config.json confirms the actual user configuration enables this project layer without an isolated home or server override.
The desktop app-server has no exposed listen option, and the documented daemon control socket is unavailable. No separate daemon was started and no existing process was interrupted.
The supported MCP restart action must therefore be performed by the operator; no available tool exposes it, and Computer Use guidance prohibits automating Codex UI.
Normal-use Agent Relay was opened for the operator's icon/reopen/Quitter check; manual-host.json identifies that host. Do not confuse its startup with a completed operator validation.

## Operator lifecycle validation

Florent subsequently confirmed that all three icon gestures work (evidence `2026-10-28-tray-lifecycle-validated`). The owned host44272 is absent after Quitter; its stderr log is empty.
This closes the visual lifecycle qualification gap. Florent also reported restarting an element described as a hook; actual current tool inventory still exposes no agent_relay tools, so MCP reload is not yet established.
One focused question now asks whether agent_relay appears in the MCP server list. No change to another server, hook or existing agent session is authorized or needed.

## Installed-client navigation correction

Read-only inspection of OpenAI.Codex26.1023.6123.0 resources confirms that the standalone MCP navigation entry can be hidden by a feature gate. The Plugins management screen contains a tab translated MCP.
Use Paramètres → Plugins → MCP to locate agent_relay. The installed Restart handler targets the Codex app-server, not only an individual MCP transport; a harmless per-server refresh must not be assumed.
Florent subsequently reported dissatisfaction with the generic tray icon, missing in-window Quitter and overall appearance. Earlier three-gesture validation establishes only those gestures, not acceptance of the visual finish. A proposed visual cleanup still awaits its scoped GO.
