# Shared stable registration migration

**Utility:** defines the single copied-settings preparation and paired-file recovery procedure for shared stable/development routing.  
**Usage:** use before changing Codex registration names. Each actual configuration write needs authorization for those exact files; a passed rehearsal is not a live client reconnection or deployment.

## Ownership and invariants

codex_registration.py owns registration names, TOML named-table edits and compare-before-atomic-replace. register-codex.py remains its compatible CLI/import facade for existing callers. Workflows import the owner directly rather than depending on another workflow. Its existing update interface retains merge semantics. replace_servers provides exact replacement/removal for an identity migration: stable must not inherit dev-only environment/options. Unknown/unrelated effective settings are preserved; unsupported or ambiguous syntax is refused before publication.

registration_migration.py is the configuration/OS adapter for two files, not a second application updater. It prepares exact before/after artifacts, checks their hashes and target selection, serializes its own writers with the existing file-lock adapter, and reconciles actual bytes on resume/rollback. It never reads/writes application database contents, grants, credentials, startup or project files. All existing connection values are retained under their selected names.

The target is shared agent_relay selecting stable, with agent_relay_dev only in the development checkout. The existing stable source name must be explicitly supplied; no name authorizes a broader identity. The migration refuses an unrelated destination or development alias. Whole stable table replacement preserves the old stable binding and data selection, not a combination of two principals.

## Prepare and rehearse

First make private copies of the reviewed shared and checkout TOML files under local/. Keep those raw settings and backups out of Git. Use a fresh output directory:

```text
python apps/agent-relay/tools/registration_migration.py prepare --shared <copied-shared.toml> --workspace <copied-workspace.toml> --stable-source <reviewed-existing-stable-name> --output <new-private-plan-directory>
python apps/agent-relay/tools/registration_migration.py status --plan <private-plan-directory>/plan.json
python apps/agent-relay/tools/registration_migration.py apply --plan <private-plan-directory>/plan.json
python apps/agent-relay/tools/registration_migration.py resume --plan <private-plan-directory>/plan.json
python apps/agent-relay/tools/registration_migration.py rollback --plan <private-plan-directory>/plan.json
```

prepare changes only its output directory. Its plan selects the two exact copied targets, not the originals. Do not edit target paths in a prepared plan to activate it elsewhere: prepare a fresh reviewed plan for another scope. The selection digest detects changed targets. After a completed migration, explicit stable source agent_relay can prepare the already-correct shape without an implicit legacy fallback.

apply and resume use the same reconciliation. Already-after files are not rewritten. Repeating a completed update changes no target file; the journal still records the no-op observation. rollback restores byte-exact before images when the selected files contain only known before/after bytes. It does not restore application data. Different/absent files, changed proposal/backup hashes or a conflicting alias require review; do not clear the journal or overwrite outside edits.

Phases are prepared, applying, complete, rolling_back, rolled_back and recovery_required. The journal retains numbered attempts, UTC start, elapsed seconds, written files/bytes, no-op result, unacknowledged interrupted attempts and error history. status checks current hashes against before/after, instead of treating the last saved phase as proof of publication. A process crash between file replacement and progress acknowledgement is resumable. A journal-storage failure is surfaced with the primary failure, never suppressed.

## Limits before live activation

The two-file transaction is recoverable, not simultaneously atomic to external observers. A receipt lock coordinates these adapter calls only. The OS replacement is atomic for one file; comparison followed by replacement cannot exclude an uncooperative external writer in that interval. Known concurrent changes are refused; do not claim universal conditional-CAS protection. Live activation must first establish a coordinated client/settings-write window and a supported lifecycle action. Missing targets and external changes are not repaired by guessing or automatically creating files.

Codex's observed settings Restart action failed in the actual client and required a full manual restart. Do not repeat it as a qualified MCP-only reconnect. A prepared/applied configuration does not prove that a loaded client has adopted it. The client gate and actual same-chat version/identity/catalog checks remain separate from this adapter's success. See [qualified client behavior](codex-mcp-refresh.md).

## Maintenance selection

pilotage_stable.py uses shared agent_relay by default. If the old project-specific stable alias is still present, default selection refuses with a migration instruction. An explicitly reviewed legacy/custom entry can be selected with --server for prepare, upgrade and status; switch/confirm follow the receipt's exact recorded server. There is no automatic fallback to another principal. The standby adapter likewise records and checks its selected entry; it is not a solution to the client-restart problem.

Qualification commands are test-registration-migration.py for copied-file migration/recovery, test-stable-update-wait.py for dependent refusal behavior, and test-pilotage-stable.py --release-directory <verified-portable-fixture> --server <explicit-fixture-name> for the isolated maintenance witness. Use only the checks justified by a change. The latter's chat evidence is simulated, not an actual desktop chat acknowledgement; it starts/stops only its fictional relay hosts and never models or WSL.
