# Interface conformity gaps and correction framing

**Date:** 2026-10-29  
**State:** implementation authorized by the subsequent explicit GO; active execution record in notes/workstreams/2026-10-29-interface-conformance.md.  
**Utility:** retains the complete visual rejection and frames the next correction plan.  
**Usage:** compare the reviewed HTML prototype with the C# interface before estimating or implementing any correction.

## Authority and baseline

Florent: « Note tout, préparer toi à faire un plan de correction ». This authorizes recording and preparation, not application changes, publication, agent calls, new dependencies or machine control. The reviewed HTML prototype is the reference; a functioning C# control does not establish conformity. The previous functional/rendering pass did not correctly qualify fidelity and must not be presented as visual acceptance.

Reference: local/apps/agent-relay/prototypes/interface-v1/agent-relay-maquette.html and its maintained sources. Six original operator images are preserved under local/apps/agent-relay/reviews/interface-conformance-20261029/pj1.png through pj6.png. No new screenshot was requested or captured from the desktop.

## Operator gap register

| ID | Source | Observed deviation | Correction criterion |
| --- | --- | --- | --- |
| UI01 | PJ1 | Metric cards end abruptly: lower borders/rounding are clipped. | Full card bounds, padding and rounded outline remain visible at the reference sizes and during resize. |
| UI02 | PJ2 | The failure alert gives no understandable cause; Examiner is a separate button below the alert rather than an inline action. | One alert component contains state, readable context/cause and the inline Examiner action, positioned like the prototype. Technical detail remains optional. |
| UI03 | PJ3 | History tabs, quick filters, search, period, participant and advanced controls form an unstructured pile above the summary. The Fil de travail section/hierarchy is missing or misplaced. | Reproduce the prototype's section order and containment. History selection and filters belong to Fil de travail, with aligned simple controls and advanced disclosure. Do not remove needed capabilities merely to hide layout errors. |
| UI04 | PJ4 | Scrolling exposes a very large blank area/extra margin; metrics appear near the bottom instead of following the project header. Block boundaries remain incorrect. | One intentional scroll surface per content region; no accumulated offset, fabricated blank space, cropped block or displacement after navigation, filtering, polling or resize. |
| UI05 | PJ4–5 | Project summary substantially differs: generic transfer rows/participant cards replace the reviewed hierarchy of metrics, current/recent tasks, recent messages and compact context. | Port each project section and its ordering, sizing, row/card presentation and contextual affordances. Data differences may change text/counts, not the page composition. |
| UI06 | PJ5 | The detail view docks beside and shrinks/reflows the page; it is not the reviewed drawer. Header, close control, backdrop, sections, timeline and footer differ. | Right-side overlay over the existing content, dimmed backdrop, bounded width and internal scroll, compact header/close icon and contextual footer. Background layout must stay in place. Reproduce each drawer type, not a generic detail column. |
| UI07 | PJ6 | Navigation is flat button stacks; missing subtitle, pictograms, project initials/colors/count badges and reviewed active-item treatment. Spacing, sizing and visual hierarchy differ. | Reusable navigation items reproduce the prototype's identity, icons, project markers/counters, grouping, active state, alignment and persistent relay indicator. Names remain configuration data. |
| UI08 | All | Broad visual fidelity is insufficient despite the earlier declaration of completion. Cards, tabs, rows, state badges, typography, spacing and action placement are approximations. | Shared components follow the reference design system consistently across all views and states. Similar colors and working buttons alone are insufficient. |
| QA01 | All | Existing checks established functional behavior and some clipping constraints, but not actual prototype/app conformity. | Compare matched views with the same fixture data, viewport, scale, scroll position and state; inspect screenshots side by side. Maintain a per-view gap checklist. Do not declare complete while visible deviations remain. |

## Failure in PJ2: confirmed retained qualification result

The displayed oversized.bin matches the retained I/O qualification report io-1f83381f81b948da8e3c65567623b385.json: request ac8f8800-022d-4b67-96b1-6ce00de459cc, state failed, code file_too_large, message “The file exceeds the configured transfer limit.” The qualification script v1-io-acceptance.py explicitly creates a256MiB+1byte file and expects this refusal. Thus this is a real retained rejected transfer produced intentionally by a negative qualification test; it is not evidence of a new relay outage.

The UI does not explain that distinction. Proposed readable cause: « Transfert refusé : le fichier dépasse la taille maximale autorisée ». Preserve the actual retained outcome; do not convert an expected test refusal into transfer success or infer test provenance merely from a filename. Examiner must stay inside the alert. Existing qualification history and real user activity must remain distinguishable without silently hiding failures.

## Diagnosis to establish before coding

Confirmed structural divergence: filters are docked outside their reference section; metrics and alert are composed as independent blocks; detail uses DockStyle.Right and may hide the page on compact windows instead of overlaying it; the sidebar uses generic buttons. These are implementation choices, not unexplained platform restrictions.

Clipping and scroll-gap mechanisms still require a focused source/layout audit. Candidate causes include auto-size/fixed-height interactions, nested scrolling, retained AutoScrollPosition during control-tree replacement and layout-triggered reentrant rendering. Do not label any candidate a proven root cause from screenshots alone. A hidden-control bitmap check is insufficient to prove the live scrolling path is correct.

## Proposed correction sequence

1. **Conformity specification:** inventory every prototype view and drawer, map its section tree and shared components, extract actual tokens/dimensions, and build a matched-fixture review matrix. Separate missing backend data from missing UI composition. Estimate each lot's time/token range and technical uncertainty before implementation.
2. **Layout foundation:** establish deterministic layout/scroll ownership, remove clipped metric containers and blank-scroll displacement, preserve state without reentrant control-tree rebuilding, and prove the general/project skeleton at normal and compact sizes.
3. **Shared presentation and pages:** implement faithful sidebar, tabs, metrics, inline-action alert, history section/filters, activity/message/task rows and project summary sections. Keep one component owner for repeated behavior. Preserve neutral connectors/connections and real MCP integration.
4. **Overlay drawers:** port the actual overlay/backdrop behavior and each contextual header/body/footer/timeline; retain underlying page geometry and scroll. Technical disclosure must match the reviewed progressive detail.
5. **Conformity acceptance:** matched screenshot comparisons for all views/drawers, normal/compact sizing, empty/long/loaded states and live scroll/filter/polling cycles; then focused functional/MCP/lifecycle regression. Functional success and visual fidelity are separate gates.

Do not silently substitute a different page structure because native controls are easier to assemble. Assess whether the existing rendering approach can meet the reference; if a different renderer becomes necessary, compare dependency, packaging, footprint, accessibility and maintenance costs before proposing a change. No technology switch is authorized here.

## Scope and next gate

This is the recorded gap register and framing for the correction plan, not an applied repair or a finalized estimate. Application source, runtime, configuration, native sessions and prototype are untouched in this recording task. Present the concise correction plan next; await an explicit GO before developing or deploying corrections. Translation remains excluded. This review reopens visual conformity of the previous interface lot; its functional test results remain observations, not proof of fidelity.
