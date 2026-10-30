# Review controls visual adjustment

**State:** complete; rendering verified and published.  
**Objective:** alert acknowledgement becomes an icon-only cross with title and accessible name; drawer keeps a clearly bordered/contrasted explicit button. Preserve acknowledgement semantics, store and history. Verify rendering and publish locally.

Authorization: Florent supplied PJ1 (drawer action too faint) and PJ2 (alert text button to replace with icon). Proposal: « Dans le tiroir (PJ1) : un vrai bouton “Marquer comme examiné”, avec bordure et contraste suffisants. Dans l’alerte (PJ2) : une croix discrète, avec l’infobulle et le libellé accessible “Marquer comme examiné”. GO pour appliquer ces deux corrections visuelles ? » Florent: « GO ».

Start2026-10-30 05:42:39UTC/06:42:39Europe/Paris; shared quota43percent. Tracked objective inspected and creation attempted; refused due unrelated unfinished blocked goal. Its145775tokens/901seconds do not belong here and must not be falsely completed. Scoped objective tracked here. No new dependency, global change, WSL/model/agent request, automatic startup or real acknowledgement. Previous scoped acknowledgement workstream is complete; this is a new authorized visual-only scope. Backup local/apps/agent-relay/migrations/review-controls-20261030/before.zip.

Implementation choice: reuse existing close SVG/icon-button primitive and primary button token. Keep one reviewAction helper with compact/full presentation variants and the identical action/id. Verify actual native drawer style and alert title/accessible name at compact size using the existing native review fixture. No need to repeat unrelated backend tests for CSS-only changes.

## Closure06:44Europe/Paris

Only renderer.js/desktop.css presentation changed: reviewAction reuses the same predicate/action/id with compact icon-only title/aria-label variant in the banner and primary text-button variant in the drawer. Removed now-unnecessary compact stacking. No controller, persistence or backend change.

Offline build passes with zero warnings/errors. Existing native review vertical test extended to assert the actual alert SVG/title/accessible name/no text/32px hit area, explicit primary drawer text/border/background/height, then actual native mark/store/reopen/unchanged journal. Fresh fixture qualification/review-controls-20261030 passes; before-review.png and drawer-review.png manually inspected at compact size. No unrelated backend-suite rerun or agent/WSL call. Current Windows interface doc updated.

Published only v1-qualification using owned/idle guard, copied preflight and rollback. Final identity at reports/review-controls/host.json, mirrored v1-qualification/ui-host.json; transaction reports/review-controls/deployment-9f58082b41ad4c5a9c3668326df29c78. Runtime7D074B4D3C05803B6AB16F5CDC1A67C82BAE7AEE8DF89ECEA44BE1667423898D, PID54204. No real alert automatically acknowledged. Application left open. Objective complete in this scoped record; unrelated blocked goal-service item preserved. Start05:42:39UTC, finish05:44:25UTC (~2minutes). Shared quota43→43percent at integer precision, no reset.
