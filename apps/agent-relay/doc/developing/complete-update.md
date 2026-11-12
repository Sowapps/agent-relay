# Complete portable update

**Utility:** defines the single complete-update transaction, its recovery boundaries and operator procedure.  
**Usage:** use for an existing portable installation; first installation and changing data scope are separate operations.

## Contract and ownership

One stable installation retains its data directory and client registrations. A downloaded release and a locally packaged commit must both produce the same input: an exact reviewed ZIP, SHA-256 and matching portable verification. A checkout is never copied directly into a running installation. Development RC qualification uses isolated copies; project agents continue using stable.

InstallationUpdate in C# owns the component inventory, immutable candidate identity, phase journal, file reconciliation, activation and paired file recovery. RetainedLauncherPayload owns the projection used both by initial launcher preparation and by updates. complete_update.py adapts archive acquisition and the existing deployment host/snapshot/startup commands. pilotage_stable.py upgrade delegates to this same procedure, preserving its scoped-project and configuration checks.

The complete inventory covers the distribution manifest, root documents/commands/notices, app/ including presentation and packaged environment adapters, the retained MCP launcher and an existing startup launcher. New files are created, retired managed files removed and changed files replaced atomically. User project files, grants, credentials, runtime logs and explicit local/ outputs are not distribution files. Before-images are recovery material, never another active stable installation. The update retains the historical installation directory identity; moving it just to rename it would strand loaded registrations.

If packaged native helpers change while retained native sessions exist, preflight refuses with update_adapter_sync_required before replacing components. The existing explicit owned-session synchronization procedure must first be qualified for that candidate; the updater does not silently overwrite a running native agent. Unchanged helpers need no remote call. No global software, account login or startup opt-in is created by updating.

## Normal command

After the exact stable archive has passed portable verification:

```powershell
python -B apps/agent-relay/tools/complete_update.py --data <absolute-installed-data> --release-directory <absolute-verified-release-directory> --version <X.Y.Z>
```

For Pilotage AP, retain the documented pilotage_stable.py upgrade command in [development commands](development.md#pilotage-ap-stable-operation); it calls the same engine with additional project/configuration verification. Do not replace its named connection or point another project at development.

The release bundles Update-AgentRelay.cmd and its tools/updater adapters, so source checkout access is unnecessary. The CLI requires existing Python and PowerShell 7 operational tooling; the candidate carries the C# engine used to run installation-update. A downloaded ZIP with SHA256SUMS.txt is qualified by the bundled verifier when its verification receipt is absent. A mismatched existing receipt is refused. Stable CLI consumers cannot select an RC; development tests invoke the same engine on explicit isolated fixtures. Run the new release's launcher from outside the installation being replaced. Application-owned temporary update files and receipts live under installation-root var/apps/agent-relay/update/; archive extraction remains beside the selected archive. No additional installation is initialized.

## Sequence and durable state

1. Check the manifest, every distributed hash, current installation, stable/RC identity and potential native-adapter incompatibility. Different bytes cannot reuse an installed version. The same unfinished candidate resumes the same transaction; a different candidate conflicts.
2. Acquire the update-driver lock. C# also excludes concurrent journal mutations. Persist preparing intent before filling the candidate stage; repeated preparation fills only missing/mismatched staged files.
3. Capture the prior selected runtime and startup preference. Persist an owned admission barrier, confirm the running host drained admitted work, enforce maintenance-idle and stop only the exactly identified tray owner. An unidentified owner or busy work refuses safely.
4. Take an offline SQLite-aware snapshot with its protected credentials, runtime metadata and a checked file-hash manifest. Publish the snapshot directory only after preparation succeeds. Keep it until recovery no longer needs it.
5. Apply files under host ownership. The flushed journal retains each before/after hash before effects. On resume, an installed file matching after is already published; before can be replaced; any other content is a conflict. A locked file is never forced or overwritten in place. Failed partial application restores paired before-images.
6. Activate the fixed app/ runtime pointer and contract history while preserving the binding and data scope. Start the selected host, check the authenticated version/contract, unchanged startup preference and real scoped MCP. All component hashes must agree.
7. Retained clients must actually report the new broker/worker version and catalog contract. A notification is insufficient. Missing support for client reload yields awaiting_clients with recovery instructions. The host can be healthy without the overall update being complete. No work is replayed to test readiness.

The durable phases are preparing, prepared, applying, files_applied, awaiting_clients, complete, rolling_back, files_restored, rolled_back and recovery_required. The journal stores candidate/version/contract identity, owned paths, expected clients, timestamps, byte counts and preparation/apply/rollback durations. ports.json stores adapter observations, snapshot/restart results and elapsed time. Diagnostic status is never authentication or permission.

## Status, resume and rollback

Repeat the same command and exact candidate to resume after interruption. Add --action status for the retained phase and receipt, or --action rollback to restore an unfinished stopped transaction. Neither action installs another version. A completed matching update verifies its state and keeps the same host and files. A repeated verified rollback does not stop the restored host again.

Recovery validates the actual files, owned barrier and snapshot. It does not delete another operation's lock. An unknown/malformed journal, external file change or missing rollback material requires review. Never clear promotion.lock merely because the updater process ended.

Before admission reopens, failure restores managed files, the coherent data snapshot and prior startup preference, then verifies the previous host when it had been running. File restoration alone is files_restored, not rolled_back. If recovery fails, retain the barrier and snapshot and report the exact ports.json path; do not report success or start another transaction.

After successful activation reopens admission, an old database must never overwrite new work. Rollback is refused for awaiting_clients/complete. Review a fresh snapshot and schema-compatible recovery instead. A client retaining an old catalog must reload its own supported connection lifecycle; the updater never starts a replacement Codex backend, forcibly closes Codex or fabricates client acknowledgement.

## Proportionate qualification

The focused C# installation-update scenarios exercise full inventory, a real Windows file lock, restart reconciliation, paired file restoration, user-data preservation, exclusive journal access and manifest tampering. test-complete-update.py tests actual portable hosts/brokers on fictional copied projects: normal update, repeat without host/file replacement, and failed candidate health with coherent database rollback. Archive/MCP verification remains separate from these transaction tests. No test authorizes touching the installed stable, WSL or a model.
