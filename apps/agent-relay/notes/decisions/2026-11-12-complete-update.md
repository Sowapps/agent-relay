# Complete and recoverable portable update

**Utility:** retains the choices behind the complete-update transaction.  
**Usage:** review before changing installation identity, publication, rollback or client acknowledgement.

**Status:** implemented in development under the operator's five-minute conditional GO; isolated measurements are not operator acceptance or stable deployment.  
**Owner:** InstallationUpdate / RuntimePaths / RetainedLauncherPayload in C#; complete_update.py adapts the existing deployment and client-control capabilities.  
**Supersedes:** the runtime-pointer-only interpretation of routine upgrades in the earlier shared-startup decision. Stable data identity and startup preference continuity remain in force.

## Context and choice

The operator requires one stable installation/version, complete managed-component updates and idempotence (evidence `2026-11-12-complete-stable-update`). Updating only the worker left the original app, broker/startup launchers and distribution files on older bytes. An old loaded client could still reject operational requests despite healthy fresh probes.

Use a C# owned-file manifest and flushed transaction journal, with source acquisition converging on one qualified archive. Reuse existing Python platform adapters for exact host discovery/stop, SQLite-aware snapshot and supported client control; do not reproduce file-state policy there. RuntimePaths remains the C# installation/data-path owner. The initial-launcher and update projections share RetainedLauncherPayload.

Keep the existing installation and data paths. Publish the selected version into its fixed app/ plus all other managed components. Recovery material is private transaction history, not another active stable. Before-images and intent hashes reconcile an interrupted file replacement. Coherent data rollback is allowed only before admission reopens. A file rollback is not a completed recovery until adapter restoration/startup checks also pass.

## Alternatives and consequences

Keeping immutable versioned worker folders and an unchanged broker would simplify a pointer swap, but fails the complete-component requirement. Renaming or moving the historical installation on every update risks stranding client registrations and the startup preference. A new installer framework or second updater service would add installation/maintenance machinery without solving client-owned catalog refresh. Atomic per-directory swaps alone cannot commit app, retained launchers, root distribution files and data metadata together; a journal is still needed.

Choose per-file atomic replacement plus before/after hashes and consistent snapshots. Locked components cause safe refusal/recovery; never overwrite a mapped executable, kill Codex or fabricate a reload. The supported control connector may request and verify refresh. Unsupported client control leaves awaiting_clients, not complete. Changed native session helpers with retained sessions refuse before replacement until their owned synchronization is qualified; no native agent is silently changed. These refusals preserve coherence rather than weakening the checks to announce success.

## Verification and continuation

The focused C# tests and portable copied-installation rehearsals are retained in the root stable-1.1.1 workstream. The real Pilotage AP catalog remains a separate deployment check. The [current procedure](../../doc/developing/complete-update.md) owns commands/states. Future changes supersede this record explicitly and must keep update/recovery complementary and the real client acknowledgement boundary intact.
