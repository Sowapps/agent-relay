# Visual refresh

**Date:** 2026-10-28  
**State:** scoped implementation/checks complete; app open for operator review. Older MCP objective unfinished.

## Scope

Florent: « Oui, GO pour la reprise visuelle : une vraie icône Agent Relay, un bouton Quitter dans la fenêtre et une interface propre. »
Preserve command/message/history/profile behavior. No new WSL model turns, provisioning, startup installation or global MCP edits. Previous integration work remains unfinished at actual desktop MCP loading.
The tracked-objective API refuses a new goal because the older blocked objective is unfinished, and offers no resume/update-objective operation. Keep that objective intact; do not falsely complete it. Record this lot's wall-clock start separately and report the accounting limitation honestly.

## Design and verification

Keep native WinForms for keyboard/accessibility, offline build and low resource use; avoid introducing a browser runtime or UI framework migration for a visual cleanup.
Use one small shared presentation owner for palette, fonts, buttons/cards and the embedded icon. Design the logo as a vector, generate its Windows multi-size icon reproducibly without a download or image service.
Window: clear header with identity, profiles and Quitter; destination/action panel; command mode hides unused message editor; generous results area with list/detail and explicit empty state; consistent status/footer.
Profile editor: readable labeled fields, logical groups and enough space; retain existing permissions/provisioning semantics.
One quit event must route both window button and tray menu to the same ApplicationContext exit. Closing with X still hides. Test actual button exit, normal read-only listing/result and persisted history, plus existing deterministic suite; no new model prompt.
Inspect real window/profile rendering and a narrow layout. Refresh staged runtime and managed local MCP reference after the build, preserve saved data and the old runtime until no client references it.

## Checkpoint

Inspected current MvpWindow, TrayContext and ProfileEditor. Generic SystemIcons.Application is still used; window has no Quitter button. Earlier measured UI workflow does not establish acceptance of visual finish.
Current normal-use host metadata: local/apps/agent-relay/reports/user-integration/mcp-final-host.json. Verify exact executable/assembly identity before replacing the owned app; do not touch Codex/Claude sessions or Codex itself.

## Implementation checkpoint

UiTheme now owns shared colors/fonts/buttons/cards and the embedded nine-size icon, generated from one SVG by tools/generate-icon.py. Executable, window, profiles and tray use that mark.
MvpWindow has a header with Profils/Quitter, adaptive destination/action card, history/detail region and clearer selected result. ProfileEditor uses readable groups and labels; Enregistrer applies current edits while preserving command names and additional rules.
Actual Computer Use captured command, agent-message and profile views. No agent start or message send. Clicked the new in-window Quitter; owned host38832 is confirmed absent afterwards.
Full suite on final sources passes through process smoke (artifacts6dadb533eb6b423988447dcd73e44271). Last source build includes card invalidation/accessibility/profile-label corrections; still need final staged runtime refresh and final visual review.
The first edge-drag/system-size attempt did not establish a narrower window: do not claim minimum-size visual qualification from those screenshots.
Next: export managed local fragment, reopen final app, verify unchanged profile save and window Quitter on final runtime; record observation and checks. Do not close the older integration objective while current-chat MCP remains unresolved.

## Final checkpoint

Final runtime EA491287F7DA38EC312A2EDF494F588F9B20000CAF5A3EF4C6519143BF191173 is exported and open (delivered-host.json, pid44104).
Final profile save preserves parsed configuration. UI listing904d29d9-2a2c-4cfe-b900-e92fe57e7a95 completed once with expected stdout/exit0. Final Quitter stopped own process42076 before reopening for delivery.
All C# checks/shared verification pass; icon generator is reproducible at nine sizes. No model turns, native session interference, global edits or new product function.
See [observation](../../apps/agent-relay/notes/observations/2026-10-28-visual-refresh.md) for proof scope and physical-tray/minimum-size limits.
Do not mark the older objective complete or report its frozen145775tokens/901s as this lot's consumption. Actual current-chat MCP integration remains unresolved.
