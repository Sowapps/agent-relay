# Complete portable update

**Utility:** defines the single update transaction, readiness and recovery boundaries.  
**Usage:** use for an existing portable installation with a qualified stable-transport connection. First installation, initial legacy-client migration and changing data scope are separate gates; development qualification never authorizes a stable deployment.

## Owners and input

One stable installation retains its root, data, projects, permissions and startup preference. Both remote releases and local packaging supply the same reviewed archive, SHA-256 and portable verification. Never copy a checkout directly over a running installation.

InstallationUpdate owns the existing exclusive journal and transaction. Its selected-release implementation uses schema 2; schema-1 journals retain recovery compatibility. InstallationSnapshot owns coherent C# database/configuration/credential snapshots and restoration. SqliteWorkStore owns maintenance-work checks; update additionally blocks uncertain work. InstallationRuntime is the narrow host/startup-status adapter. It obtains PID/start time over an authenticated host handshake and checks the exact process image immediately before stopping that idle host. No process-name guessing, client kill, request replay or model call is an update step.

The [minimal transport](mcp-transport.md) owns stdio and cooperative succession. complete_update.py adapts reviewed archive acquisition and invokes the same C# transaction; it does not duplicate its schema-2 phases. Its legacy typed-tool probes and reload callbacks are not used for the generic surface. The old stable_update_wait receipt entry delegates to this procedure once; it has no client-exit loop or fifteen-minute expiry.

## Paths and preflight

RuntimePaths owns the immutable release slot below the installation's var/apps/agent-relay/releases, the explicit transport selection and transport state locations. A candidate's app/ and transport/ payloads are staged into its checked slot; root documentation/commands remain transaction-owned files. Old loaded files are retained rather than overwritten. Retention is recovery/cleanup material, not another selected stable version. Developer local/ outputs and user project workspaces are never internal prerequisites or distribution files.

The candidate must contain the independent self-contained transport, current MCP identity and operation revision manifest. Every source file/hash is checked, same-version different bytes are refused, reused operation revisions cannot change schemas, and incompatible wire requirements refuse before cutover. Changed native helpers with retained sessions require the existing owned-session synchronization qualification. No remote inspection is needed when helpers are unchanged.

A schema-2 update requires an existing qualified transport selection. Active legacy broker connections or an unqualified initial connection return update_initial_migration_required before publication. Do not relabel the old monolithic broker as a minimal transport or repeat an unsafe client restart to bypass that gate. The initial migration/client coordination procedure remains separately required.

## Command and phases

After the exact archive is verified, use the existing complete_update.py entry with --data, --release-directory and --version. Its --action status, resume, rollback and diagnostic actions need no original archive after verified staging. Packaged launchers call that same adapter. Common registration/startup/UI integration and final archive qualification must be complete before deployment; source fixtures alone are not installed readiness.

1. Prepare a checked private candidate and independent recovery runner under the existing update directory. The same candidate resumes the same transaction; a conflicting candidate refuses. Completed before-images are never replaced.
2. Persist the owned admission barrier and awaiting_work state. Drain admitted requests and block on pending, running or unknown durable work. Waiting survives interruption with no expiry and never stops/replays that work. Repeated resume checks the same state.
3. Stop only the verified idle host, take exclusive data ownership and capture the coherent SQLite backup plus protected configuration/credentials. A partial capture uses its own private directory; a completed snapshot is verified and immutable. A missing original snapshot after publication is a recovery error, never an invitation to take a new snapshot of changed data.
4. Publish all immutable slot bytes and managed root files, retaining verified root-file before-images. Persist selecting intent, then publish the checked runtime/transport selections and contract history behind the admission barrier. Known before/after values are reconciled; observed external changes refuse. The two files are not claimed to be simultaneously visible or protected from uncooperative same-user writers by OS compare-and-swap.
5. Start/verify exactly the selected host, authenticated version/contract, preserved configuration/credentials and startup preference. Await every expected live transport identity, and reject any current mismatching transport. Idle gateways change cooperatively without another client message. An acknowledgement gap retains awaiting_transports and the barrier, not a partial-success result.
6. Persist reopening intent before releasing the barrier. That irreversible boundary prevents old-snapshot rollback even if the process dies before recording complete. Resume finishes selection/readiness/release without restoring a database over newly admitted work. Repeating a completed matching transaction is a checked no-op.

Selection checks use the exact manifest; a metadata file alone is not proof that required old code stopped executing. Diagnostic transport leases are observations, never credentials or execution authority. No complete-update claim implies that an already-loaded legacy client was migrated.

## Recovery and errors

Before reopening intent, failure restores coherent prior selection, protected data and managed root files, verifies prior host/startup behavior and waits for required transports to return. Immutable failed candidate bytes remain retained for diagnosis/guarded cleanup; they are not selected. Cancelling while awaiting_work leaves the working old host alone. After admission may have reopened, restoring an old database is refused; inspect retained results and use compatible forward recovery.

Only this transaction's barrier can be removed. Unknown schema/phase, missing snapshot, conflicting file/selection or failed restoration retains a retrievable error and admission protection. Flushed intents and same-volume atomic file replacement support process-interruption recovery; process-exit tests are not power-loss certification.

Expected and unexpected CLI failures use the existing C# InstallationUpdateDiagnostics owner: code, diagnosticId, safe message, recovery and retained details path. Retrieve through --action diagnostic with its ID. Logging failure is explicit and preserves the primary error; rollback failure preserves both causes. Do not put credentials or complete agent requests in public diagnostics.

## Useful qualification

selected-update exercises real files/SQLite with a runtime adapter fixture: busy/uncertain drain, immutable slots, transport acknowledgement, idempotence, coherent rollback, actual child exits after stop/selection/reopening and preservation of work after admission. installation-runtime verifies authenticated PID/start time/image, quiescence and exact owned-child stop on a real isolated host. The retained schema-1 installation-update scenario protects legacy recovery. Full portable-host/transport/archive qualification is a separate delivery gate; no test authorizes a stable deployment, existing-agent work or global startup modification.
