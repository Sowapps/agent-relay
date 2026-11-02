# Manual storage cleanup: inventory and design

**Utility:** proposes a bounded manual maintenance tool from the observed storage owners and Florent's protected-day rule.  
**Usage:** review the fictional prototype before authorizing an implementation. No deletion or automatic retention policy is delivered by this design.

## Read-only inventory

Observed02/11/2026 under this installation's var/apps/agent-relay:11,619files,4,384,365,584apparent bytes (about4.08GiB). Largest logical groups: immutable runtime versions about3,059MiB; migration snapshots193MiB; five qualification payload groups about122MiB each; selected-scope MCP/startup launchers about121MiB each; presentation cache about71MiB; deployment reports about7MiB. The raw report is local/apps/agent-relay/ui-polish/storage-inventory.json.

These are apparent file sizes, not disk allocation or recoverable space: hardlinked payload files can be counted repeatedly, and required/active files are included. The inventory reads metadata only, follows no symlinks/reparse points and deletes nothing. Developer local/, toolchains, source workspaces and WSL were not scanned. Existing qualification scopes under var are recorded as such, not silently adopted as disposable application data.

The selected data scope has approximately1.6KiB of MCP error logs and11KiB/22MCP diagnostic records at observation time. Therefore a conservative first cleaner would free little here. Meaningful runtime pruning is a separate, more demanding dependency problem; it must not be advertised as automatically recoverable space.

## Existing owners and limits

| Data | Existing behavior | Proposed treatment |
| --- | --- | --- |
| relay.sqlite, WAL/SHM, retained operations/messages/sessions and transfer receipts | Durable history and recovery/idempotence facts; no age pruning | Excluded. Never delete sidecars or vacuum/compact a live database as a filesystem cleanup. |
| Configuration, connections/DPAPI secrets, project permissions, review state, lock files, runtime pointer/contract ledger | Security, identity and lifecycle authority | Always protected, regardless of age. |
| logs/mcp/YYYYMMDD.log | UTC day files; individual diagnostic text bounded around8,000characters and writes stop after an approximate1MiB threshold; no old-day purge | Candidate old logs only after all temporal/activity protections. This is not a strict global storage cap. |
| diagnostics.log and deployment stdout/stderr | Some files are append-only/undated; no general age/total-size retention | An undated file modified today is entirely protected. Future rotation by explicit owner/date is preferable to truncating live logs. Deployment transactions/rollback snapshots remain excluded initially. |
| mcp-connections/*.json and *.close | Heartbeat leases; latest256read; records older than10seconds display offline; no persisted-record pruning | Offline is not proof of process exit. Only positively terminated, old diagnostic records can be considered. Active, unknown or pending close markers remain protected. Reading256does not bound files on disk. |
| presentation-cache | WebView/browser profile; recreatable in principle, but includes interdependent databases/locks/preferences | Current profile excluded while in use. Do not remove individual old-looking browser files. A future explicitly closed obsolete cache can be treated as one owned unit. |
| staging/transfers/*.part | Retained for uncertainty/recovery; existing operation-specific partial inspection/cleanup available | Excluded from generic cleanup. Use the existing scoped transfer inspection and explicit partial-cleanup use case after verifying the outcome. Age alone does not establish abandonment. |
| Other private temporary/pending files | Some normal success paths remove their own staging; crashes can retain residue | Candidate only if the component identifies it, proves its operation ended and confirms no recovery dependency. Unknown files protected. |
| run/, retained mcp-launcher/startup-launcher, preparing/ | Content-addressed runtimes; retained launchers/hardlinks may support old clients, startup or rollback | Launchers/current runtime always protected. Inactive runtime cleanup deferred until a complete reference/process/dependency inventory can prove unused status for every data scope. |
| Migration/configuration/deployment backups | Preserve rollback and recovery; no uniform retention | Excluded initially. A future explicit backup-retention feature needs its own policy and scope. |
| Exports, uploads, artifacts in user workspaces; local/ developer material | User/agent owned, not generic maintenance residue | Not enumerated or deleted by this application tool. |

## Temporal protection: settled rule

Use the installation's local timezone at both analysis and execution. Let D be the current local calendar date:

- At or after02:00: cutoff is local midnight starting D. Every item with activity on D is protected.
- Before02:00: cutoff is local midnight starting D−1. Both D and D−1 are protected.
- Candidates must be strictly older than the cutoff. At exactly02:00the previous date becomes potentially eligible, never automatically removed.
- Evaluate calendar midnights, not “now minus24/48hours”; daylight-saving transitions must not change which calendar dates are protected.
- Use the latest meaningful creation/modification/component-activity timestamp. An old creation date does not allow removal of a recently modified item. A recent creation date protects a copied old file too.
- Unknown, contradictory, future or unreadable timestamps protect the item. Live resources and required data remain protected regardless of any timestamp.
- An analysis cannot disable this rule or expand the selection when a date becomes newly eligible. Execution rechecks the current local date/time/timezone and all item facts; newly eligible items require a new explicit analysis/selection.

Logs partitioned by UTC day must still be evaluated against local protection boundaries and their latest activity. Do not infer eligibility solely from a filename or date embedded in a backup name. Protection applies to the entire owned artifact if recent records cannot be separated safely.

## Proposed UI and scope

General settings → Stockage et nettoyage. The prototype exposes this tab only through its fictional renderer extension; production has no cleanup tab, command, endpoint or deletion engine.

1. Explain installation/data scope and show occupied space by category, distinguishing apparent size from estimated recoverable bytes. Excluded/required categories remain visible with reasons.
2. Show the protected calendar dates and reference local time read-only. The clock used to simulate01:59/02:00belongs to exterior prototype tools, never to product settings.
3. Select supported categories (old technical logs, positively terminated connection diagnostics, provably abandoned non-transfer temporaries). All other categories remain excluded. No path picker, arbitrary file deletion, “include protected days” override or implicit business-history cleanup.
4. Analyse: read metadata/owner state, produce a bounded frozen list with counts/estimates, reasons for protected/skipped items, timestamp and selection. Cancel changes nothing.
5. Confirm the exact proposed selection. Revalidate each item immediately before mutation; if ownership, activity, content identity or calendar protection changed, protect it and require a refreshed plan when necessary. Never substitute a different file by path.
6. Report actual removed/skipped/failed counts and measured recovered space where available. A partial failure remains explicit; do not report full success or silently repeat deletions. Keep a small dated maintenance record under application var, itself protected for the current day.

No automatic cleaning or scheduling in this first scope. Per-file exclusive ownership/identity checks and a scoped maintenance lock prevent concurrent cleanups; cleanup must not hold a global relay lock that blocks ordinary project work for the duration of a large scan. Reparse points, path escapes, hardlink/dependency uncertainty and unknown owners are exclusion reasons, not prompts to force deletion. One-host ownership is not sufficient evidence that old external MCP processes stopped using their payload.

## Prototype and checks

Versioned prototype/cleanup-preview.js contains fictional objects only, with in-memory analyze/confirm/recheck/result behavior. It has no filesystem, MCP or native deletion calls. A simulation conflict or clock/category change invalidates the proposal. The standalone generator embeds this extension only in the prototype, not production assets.

Targeted own-WebView qualification passed: previous day protected at01:59, eligible at02:00, current day always protected, active connection and required transfer always protected, explicit simulation confirmation and protected items retained. The three UI regressions are tested separately against the same prototype draft and production assets. Reports: local/apps/agent-relay/ui-polish/{prototype-check,application-check}. These are observations, not operator visual acceptance.

## Implementation proposal after design approval

1. Add read-only category/owner inventory and protected-day policy with local clock/timezone injection. Keep filesystem operations behind an explicit maintenance port and keep decisions separate from the Windows adapter.
2. Add bounded plan/preview and exact confirmation/revalidation for the first safe categories only. Preserve public history/permissions and all selected-scope isolation. Version any new MCP contract explicitly if later exposed; a cleanup tool must not be granted to participants implicitly.
3. Check the normal case,01:59/02:00and a daylight-saving boundary, unknown/recent timestamps, changed/live item after preview, path/reparse rejection, concurrent execution and partial failure. One isolated fixture suffices; no user data or paid agent call is needed.
4. Review the actual deletion scope/confirmation UX, then deliver through the normal branch/deployment procedure. Add inactive runtime/backup/cache pruning only when their separate dependency/ownership guards are designed and approved.

Current status: design and simulation ready for review; no real cleanup performed or implemented.
