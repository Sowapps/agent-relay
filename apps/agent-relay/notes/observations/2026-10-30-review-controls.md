# Review controls visual adjustment

**Date:** 2026-10-30  
**Status:** implemented and qualified; observations await operator validation.  
**Utility:** records the two approved presentation corrections.  
**Usage:** consult with current Windows interface documentation and the scoped workstream.

Florent’s PJ1 identified the drawer action as too faint; PJ2 requested replacing the banner text action with an icon. The explicit GO covers only those visual changes. The alert now uses the existing close SVG in a32px icon-only button with title and accessible name « Marquer comme examiné ». The drawer uses the existing primary button style, retaining its full text, border, contrasted background and keyboard focus style. Both use the same durable acknowledgement action and unchanged revision semantics.

Offline build passes without warnings/errors. The native review scenario asserts actual shape/labels/styles in C#, captures both variants at compact size, executes the real native acknowledgement and verifies persistence after window reopen with unchanged SQLite outcome. Artifacts local/apps/agent-relay/qualification/review-controls-20261030/render/{before-review.png,drawer-review.png,after-review.png,reopened-history.png,review-verification.json}; before/drawer captures manually inspected. No real alert was marked, no agent/model/WSL request, new dependency or global configuration was used. No unrelated backend retesting was needed.

Guarded qualification publication and rollback snapshot are recorded in local/apps/agent-relay/reports/review-controls/host.json and its deployment transaction; v1-qualification/ui-host.json mirrors current ownership. Application left open. Source backup local/apps/agent-relay/migrations/review-controls-20261030/before.zip. About2minutes; shared quota43percent before and after at integer precision, no reset. Tracked objective creation remains blocked by an unrelated unfinished goal; this scope is closed in notes/workstreams/2026-10-30-review-controls.md without misattributing the old goal counters.
