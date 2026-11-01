# Shared hover alignment

**Utility:** retains the authorized cross-view hover adjustment and visual checks.  
**Usage:** local application change only; no commit, push, tag, release or prototype modification.

## Scope and objective

Florent approved « GO » for subtle background-only hover across interactive views, aligned with the HTML prototype and separate keyboard focus. Remove generic inset hover borders/dimming; preserve intentional resting borders, active selection and meaningful keyboard outlines.

Start13:17UTC /14:17Europe/Paris. Goal inspected/creation attempted; historical unrelated unfinished blocked goal still occupies the slot. Track this scoped objective here; do not reuse its counters or falsely mark it complete.

## Plan and checkpoint

Remove desktop.css global brightness/inset-shadow overlays rather than stacking per-view patches. Retain component-owned prototype backgrounds and selected states, give navigation/segmented controls and disclosure links restrained feedback. Verify actual production hover across general/task/activity/message/session/card controls plus keyboard focus with the reusable isolated WebView proof, then guarded local update preserving runtime data and startup registration.

## Completed result

Removed the generic brightness/inset-border overrides and strong navigation fill; existing prototype row/message/task/card treatments now own their hover behavior. Navigation/segmented inactive controls, links, disclosure summaries and selects retain restrained feedback. Removed the startup-switch hover halo; its color feedback remains. Resting card/control borders and selected states remain intentional. Keyboard row/message/task-event focus is inset to remain visible inside clipped cards. The earlier tab refinement remains intact; no prototype edit.

Nine actual Chromium forced-hover surfaces passed in the production RelayWebView: general tasks, project cards, navigation, feed navigation, project tasks/activity/messages/sessions and task detail. No global dimming/inset shadow remains; existing prototype card shadow is retained. Native own-control captures were inspected, including complete inward keyboard focus. Artifacts: local/apps/agent-relay/qualification/hover-alignment/verification.json and associated PNGs. Only fictitious fixtures were used; no desktop control or agent/model/WSL call. Reused the previous isolated proof; no production test hooks or unit tests added.

Guarded local deployment succeeded: host36380, reports/hover-alignment/deployment-68a7b1d10f3f4f5facc8905bedc0a514. Only exact owned tray37500 was replaced; MCP facades remained running. Project/configuration/credential file hashes and Windows startup registration were unchanged. Source/deployed CSS matches; qualified released binaries were reused with only the desktop stylesheet replaced. No Git or GitHub mutation; published v1.0.1 remains unchanged.

Scoped objective complete approximately13:21UTC /14:21Europe/Paris, about four minutes. Goal-specific consumption unavailable because historical blocked objective occupies the slot; counters not reused, no quota reset. Verification remains an observation pending operator review. Bulky isolated proof copies are disposable; retain proof sources, JSON and captures for reproduction.
