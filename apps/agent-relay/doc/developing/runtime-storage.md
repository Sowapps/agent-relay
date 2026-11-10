# Runtime storage and migration

**Utility:** separates application-owned operational files from developer files and explicit project workspaces.  
**Usage:** use this contract before adding persistent state, deploying a runtime or migrating an existing scope.

## Ownership and resolution

Native layout facts have one packaged owner, wsl/native-layout.json, consumed by C# NativeLayout/RuntimePaths and Python native_layout. Provisioning, lifecycle, migration and helper synchronization must consume that owner. Preserve the definition with the helpers; do not infer a session solely from a basename prefix. Root recognition does not replace OS ownership and identity-marker validation.

The operating installation root owns `var/apps/agent-relay/`. It is not `apps/agent-relay/var/` inside the source application. A portable installation at `C:\Tools\AgentRelay` defaults to `C:\Tools\AgentRelay\var\apps\agent-relay\default`. The checkout uses the same root-relative layout. Direct EXE launch, supplied CMD launchers and default development launch resolve the same scope; the working directory does not select storage.

`installation.json` beside the binary declares `{ "version": 1, "root": ".." }` in an extracted package. Source staging supplies the relative path from its immutable binary directory to the checkout root. A missing descriptor requires an explicit `--data-dir`; no user-profile or local/ fallback is invented. `--data-dir` remains an exact operator choice, including an explicitly selected developer fixture. Initialization creates the runtime pointer; the first host/admin operation creates the durable store.

| Root-relative path | Owner and content |
| --- | --- |
| `var/apps/agent-relay/<scope>/` | SQLite journal, settings/projects, protected connections, attention reviews, browser cache, operational logs, host metadata and retained startup launcher |
| `var/apps/agent-relay/<scope>/workspaces/<session-hex>/` | Internal Windows session staging, separate from configured project workspaces |
| `var/apps/agent-relay/<scope>/staging/transfers/<request-uuid>.part` | Private download partials; final files are published only to the explicitly authorized destination |
| `var/apps/agent-relay/run/<fingerprint>/` | Immutable prepared Windows runtime; deployed host/MCP need no private SDK or checkout Python |
| `var/apps/agent-relay/preparing/`, `deployment/`, `migrations/` | Application preparation, operational reports and retained migration/rollback snapshots |
| `local/` | Developer/operator tools, experiments, test artifacts, exports and downloads deliberately requested there; never an application prerequisite |

On a WSL host the current adapter owns `/srv/agent-relay/var/apps/agent-relay/sessions/<session-hex>` and `profiles/<id>`. Helpers, private client configuration, socket, logs, native message index and upload partials belong there. A partial is `<root>/<client>/data/staging/transfers/<request-uuid>.part`. User project workspaces remain independent; do not relocate them, copy their contents or infer synchronization. Explicit legacy roots remain accepted for controlled migration. Other environment adapters must provide an equivalent owned-runtime contract rather than reuse WSL paths in the core.

Private Windows/WSL storage is excluded from scoped file transfers. Traversal/reparse checks apply to workspaces and private staging. Atomic publication requires staging and destination to share a filesystem/volume; it is refused before copying when they differ. No sibling partials are created in user workspaces. Staging cleanup addresses only the retained request, never the final delivered file. These checks are not a security boundary against concurrent arbitrary same-user modifications.

## Standard migration procedure

Prepare a self-contained candidate from the current checkout with `tools/export-mcp.ps1 -DataDirectory <var-preparation-scope> -PrepareRuntime`. The migration uses that C# runtime to read and change the Windows startup entry even when the source data comes from an older release. Then use one command, first without `--apply` to review the plan:

```powershell
python -B ./apps/agent-relay/tools/migrate-storage.py `
  --source <absolute-legacy-local-scope> `
  --destination <installation-root>/var/apps/agent-relay/<scope> `
  --runtime-pointer <prepared-scope>/mcp-runtime.json `
  --config <Codex-config.toml> --check-native `
  --report <explicit-operator-report.json>
```

Repeat `--config` for each reviewed connection pointing to that exact scope, then add `--apply` within the authorized migration scope. The command refuses destination merges, active work, unowned roots, conflicting process identity and concurrent client edits. Only the exact idle tray owner may be stopped; MCP facades and other chats are not terminated. Early shared profile roots without a marker need the strictly recognized legacy layout/owner; session roots always need their session ownership marker.

The command copies a consistent SQLite store and protected credential bytes, preserves project/grant and historical payload bytes, rewrites only active internal session/profile paths, stages native copies and verifies authenticated MCP reads for each configured participant. A maintenance gate holds new tasks during verification. Startup is transferred only if already enabled for this scope, using its new retained native bootstrap. Config merges preserve each connection UUID and all unrelated settings. Failed preparation retains a diagnostic candidate and restores old references/startup/native roots and the old host when safe; changed client files are never overwritten during rollback.

The C# startup operation owns the registry entry name, the retained launcher command and the exact string value/type in the rollback receipt. It verifies the reviewed entry before each change and refuses unrelated or unexpected commands. The Python procedure orders those calls with the data migration and preserves the original startup receipt in `startup-before.json`.

Migration and deployment call the prepared C# runtime's `maintenance-idle` operation to check durable `pending` and `running` records in read-only mode. Migration repeats the check under host ownership. Only an initial deployment can opt into a missing database. A readable history of terminal outcomes remains eligible for maintenance. A live replacement also uses its separate admission barrier for requests already admitted by the host.

Use a prepared runtime that exposes `maintenance-idle`. For a real rollout of an archive that predates this command, use the administration tooling from that archive's release checkout. The development rehearsal can exercise earlier archives with an explicit current guard.

After successful cutover, owned legacy files are retained in the var transaction backup. Developer artifacts and declared workspaces remain in place. `migrated-to.json` and a directory at the former SQLite filename prevent older launchers from creating a second writable store. Already open old MCP facades must reload through their client's supported lifecycle: they reread their original data path for every request. A new connection is qualified before completion; there is no hidden dual-store forwarding. An idempotent repeat reports the existing migration. Once new work is admitted, rollback requires a new, deliberate maintenance review rather than restoring an older snapshot automatically.

## Qualification

The ordinary suite covers descriptor resolution, retired-store refusal, protected transfer paths and durable partial cleanup. `tools/test-storage.py --runtime-pointer <pointer>` qualifies a real local-only project/connection migration, dry-run, active-work refusal, injected failure/rollback, payload retention, config isolation, restart and defaults without a system .NET SDK or local/ directory. `tools/test-native-storage.py --distribution <distro> --user <user> --report <path>` separately qualifies only marked WSL fixtures: native copy/commit/rollback, credential reference preservation, partial recovery, atomic private staging and cross-filesystem refusal. The latter needs explicit WSL test authorization and invokes no model. Release verification tests the packaged descriptor and default scope; operational var contents are forbidden in release payloads.
