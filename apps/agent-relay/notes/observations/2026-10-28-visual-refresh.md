# Native interface refresh

**Date:** 2026-10-28  
**Status:** scoped implementation/checks complete; visual acceptance belongs to Florent.

## Scope and delivered changes

Authorized by evidence `2026-10-28-visual-refresh-go`. Added a vector two-arrow mark and nine-size ICO shared by executable/window/profile dialog/tray. Added in-window Quitter using the notification menu's context exit path.
UiTheme owns shared presentation. Main window separates header, adaptive destination/action entry and history/result detail. Profile editor uses readable labels, applies current edits on save and preserves existing command names/identifiers/additional rules.
Explicit accessibility labels and selected-result guards prevent wrong label association and stale async output. No debug window, framework migration, new agent capability, model turn or global edit.

## Measured checks

Computer Use captured command mode, message mode (selection only), profiles and final window. Custom mark/Quitter are visible; retained output is readable after restart.
Actual in-window Quitter stopped own processes38832 and42076. Final real UI listing904d29d9-2a2c-4cfe-b900-e92fe57e7a95 completed once: exit0, stdout alpha.txt/beta.txt, user sowapps, nonce671263ee-5394-42bb-9b94-3aa9b5c8899d.
The listing took about14s including WSL startup. Its first early observation was still running; it was never resent. Saving an unchanged profile preserves the complete parsed three-profile configuration.
ICO regeneration is identical: SHA2564cde6d110cab866f4302f41179f01a2acce31b570ec381dbd2519b04cb373ebd, frames16/20/24/32/40/48/64/128/256px.
Final build: zero warnings/errors. Thirteen exchange/transport cases, nine durable-work cases, staging regression and host/client process smoke pass (artifacts6dadb533eb6b423988447dcd73e44271). Shared verification passes.

## Artifacts and boundaries

Reports: local/apps/agent-relay/reports/visual-refresh/; final-quit.json, profile-save.json, ui-command.json, logs and delivered-host.json. Final runtime EA491287F7DA38EC312A2EDF494F588F9B20000CAF5A3EF4C6519143BF191173; fragments and managed project-local connection reference it. Older runtimes are retained.
The delivered app remains open for review. No native agent session was started/stopped/prompted. Physical notification-area placement is not exposed by Computer Use; the tray uses the custom embedded mark, but no physical tray screenshot was captured.
A narrow-window drag did not establish resizing; minimum-size/multi-DPI visual qualification is not claimed.
The earlier objective remains unfinished at current-chat MCP loading. The API rejected another objective because that one is unfinished; its blocked counters remain145775tokens/901s and do not measure this lot. Separate wall-clock timestamps are retained. Do not invent token usage or falsely complete the old objective.
