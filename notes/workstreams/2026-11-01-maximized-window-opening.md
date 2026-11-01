# Maximized opening and restoration bounds

**Utility:** retain the authorized small native-window workstream.  
**Usage:** resume through native validation, dedicated-branch delivery and selected-instance deployment; no release, agent calls, persisted window preferences or project changes.

## Authorization and design

Florent explicitly approved startup maximized and a centered restoration rectangle occupying85% of the selected screen working area's width and height, excluding taskbar. No size/position persistence. Only the top-level monitoring window is affected; embedded preview/test surfaces keep their explicit dimensions. Calculate after native DPI scaling during OnLoad, using Screen.FromControl().WorkingArea coordinates directly (no second DPI multiplication). Clamp the minimum to the restore rectangle on small displays. Tray opening must not force the maximized window back to Normal; a minimized window returns maximized, other user-selected states remain for this process only.

## Checkpoint

Branch codex/maximized-window-opening from clean main120ed8a. Start20:26UTC /21:26Europe/Paris on01/11/2026. Scoped tracked goal creation was attempted and rejected because an unrelated historical blocked/unfinished objective still occupies the API. Do not attribute those counters to this task. Next: native implementation and own-window test checking initial maximize, actual restoration bounds, tray reopening, a fresh window and captures; docs and standard delivery preserving protected state.

Native own-window scenario PASSED at local/apps/agent-relay/qualification/window-opening/render/verification.json; initial maximization, actual restored Bounds/RestoreBounds, hide/reopen, minimized recovery and a fresh window after a manual resize are verified. Working area2560x1528, DPI144; restore2176x1298 at(192,115), exactly85% with integer rounding. Captures inspected for both states. No model/native-agent calls or existing project mutation. Shared verification passed. Next: finish ordinary smoke, commit/push dedicated branch and integrate, standard selected-instance deployment with protected-file hashes, matching MCP registration command update, delivery record.
