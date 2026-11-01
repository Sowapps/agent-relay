# MCP connection update audit

**Utility:** retain the authorized audit, measured boundaries and correction plan.  
**Usage:** audit/test existing connections and local server; no application implementation, agent/model task, permission change, project edit or tag/release.

## Authorization and objective

Florent: « GO pour l’audit du mécanisme de mise à jour des connexions MCP et un plan de correction. Vérifie ce qui est réellement possible, tu peux agir sur les connexions existantes et sur le serveur local, il n'est pas utilisé. » Standing delivery applies to notes-only audit branch codex/audit-mcp-refresh from mainfb6c920. Scoped goal creation attempted but rejected by the unrelated historical unfinished/blocked objective; its counters are not attributable. First timed checkpoint20:57UTC /21:57Europe/Paris on01/11/2026 (initial discovery preceded it).

## Initial measured findings

Current chat itself exposes24tools, lacks set_project_state, and actual list_projects returns invalid_request with raw SQLite Error14. Disk configurations (repository and user) both reference current var/run/4F47... native runtime and var/apps/agent-relay/v1-qualification. Existing Codex-parented facades include older BEFE... executable and retired local/apps/agent-relay/v1-qualification; retired scope has migrated-to.json but old binary does not consult that marker. Current source serves docs/tool schemas from its own process directory; no listChanged capability. Current McpFacade CallAsync constructs ConnectionStore/RelayDatabase locally before contacting the pipe, so filesystem failure is mislabeled invalid_request.

Official OpenAI app-server docs expose config/mcpServer/reload and thread-specific mcpServerStatus/list; verify against installed0.166.0-alpha.5.1 schema and accessible running-server control transport, not a fresh diagnostic server alone. No Codex app-server restart is planned. Existing facade termination can be tested only for verified relay-owned MCP processes, with before/after client checks and no model replay. Local harness/reports under local/apps/agent-relay/qualification/mcp-refresh-audit. Preserve protected state hashes and all registrations.

## Findings and tests

### Failure path

The retired relay.sqlite path is a directory, not a database file. migrate-storage.py deliberately creates that tombstone after moving the old data; migrated-to.json records the destination. It prevents a stale executable from silently recreating an independent database. Do not remove that guard or copy back credentials/databases to make the error disappear. The scoped AP binding and its protected credential exist in the current var scope. No binding was created, remapped or granted rights by this audit.

McpFacade.RunAsync serves tools/resources from the facade binary, not the active host/runtime pointer. CallAsync constructs ConnectionStore, whose RelayDatabase opens/initializes SQLite before reading the secret and connecting to the host. It does not call RuntimePaths.DemandActive first. All non-RelayException failures become invalid_request with raw exception text. Therefore a migrated path becomes misleading SQLite Error14 instead of a migration/reconnect error. Failure can occur before the updated host sees the request; changing only that host cannot retrofit old executable behavior.

### Generation drift

Actual raw MCP comparison against the same healthy current host: facade91A756... and facade4F47... both announce version1.0.1 and25tools. The former has an old guide and omits archiveRequested/ArchiveDiagnostic from discovery, because it deserializes into an older typed DTO; the latter preserves them. Tool count/SemVer alone cannot prove a current connection. The repository and user registration currently point to4F47... and the var scope, but already-running facades remain pinned to previous paths. The one previously mentioned AP project config path was absent; this audit does not claim an exhaustive scan of all possible project overrides.

### Codex capability, measured rather than assumed

Official OpenAI documentation describes config/mcpServer/reload as reloading configuration and queuing refresh for loaded threads; mcpServerStatus/list can inspect one thread/server. Installed0.166.0-alpha.5.1 schema confirms reload takes null params and has no per-server selector. Source: [Codex App Server](https://learn.chatgpt.com/docs/app-server). No general restart/reconnect action is exposed among this chat's available Codex tools.

A fresh diagnostic app-server accepted reload and discovered the current25tools. This proves the installed backend method, not a reload of the desktop's existing threads. The actual desktop app-server runs with stdio. Its default external control socket is absent; app-server proxy and daemon version fail with socket error10050. No internal IPC injection, debugger attachment, UI takeover, Codex restart or daemon/listener installation was attempted. An independently started app-server cannot refresh the desktop process's threads.

Two isolated protocol tests used a separate local Codex home, a fictional MCP and ephemeral in-memory protocol threads; no user conversation or model turn was created/executed. In the loaded fixture, tools initially audit_v1; sending MCP tools/list_changed and resources/list_changed did not refresh the tool inventory within6seconds without a model turn (it remained audit_v1), although an explicit resource read returned guide2. Changing only the isolated MCP configuration and calling reload changed the SAME loaded fixture inventory to audit_v3 and its guide to3. This establishes hot reload for an accessible backend. It does not establish next-model-turn behavior or support in every client/configuration.

The second isolated test stopped only its verified fixture child. Resource read failed Transport closed, with no new PID. Reload with UNCHANGED configuration started a new fixture PID and resource reading succeeded. Closing a child is not a reconnection procedure; explicit reload is effective even without a config edit.

During the authorized real-connection test, three exact legacy BEFE... MCP facades were identity-checked and closed; Codex/app-server, current relay host and native agent sessions were untouched. No legacy replacement process appeared in follow-up inventories. The chat's advertised24tool surface and delivered SQLite failure remained stale. This does not establish which transport/cache delivered the post-termination error, and the audit does not attribute it to an already-dead PID. This corroborates that closing processes alone does not prove a refreshed client catalog. The still-running91A... facade was left in place as a working but outdated comparison.

## Correction plan, awaiting implementation GO

### Lot1 — classify failure before touching storage

- Guard every MCP entry/request against retired scope, missing/invalid runtime descriptor, missing credential, incompatible generation, unavailable host and actual database failure BEFORE constructing any SQLite-backed object. Factor protected-secret file reading away from ConnectionStore's database initialization; authenticate/authorize at the host. A read/discovery call must not create an old data directory/database.
- Return stable machine-readable codes such as relay_migrated, connection_outdated, relay_unavailable and storage_unavailable. Default content is a readable explanation plus the precise recovery action; structured error data carries code/current generation/confirmed outcome and optional diagnostic reference. Raw exception/stack/path details belong to explicit diagnostics. Keep genuine invalid_request for invalid arguments.
- Migration redirects require an owned validated migration record, preserved explicit connection UUID and a binding/credential present at the destination. Never fall back to another principal, use administrator credentials, add grants, copy old secrets back or execute work to verify the move. Missing/unverified identity means a clear failure.
- Verify tombstone/missing file/access denial/unavailable host and independent AP identity preservation. A read failure cannot trigger task replay. Existing frozen legacy binaries need a one-time client refresh to receive this protection.

### Lot2 — identify the actual loaded contract

- Introduce a deployment/build generation and a digest/revision covering schemas, public DTO contract and packaged documentation. Keep product SemVer separate. Include the facade/host generations and compatibility in a bounded authenticated handshake/status response.
- Preserve added host fields across the transport using a raw/opaque JSON envelope or explicitly versioned compatible DTOs. Do not deserialize/re-serialize new host responses through frozen facade DTOs and silently lose fields.
- Record connection-instance IDs, preserved binding ID, client metadata, last-seen generation and update state at the host, with bounded lease/expiry. No OS PID inventory as cross-platform authority. Expose simple current/outdated/offline diagnostics in general connection monitoring; keep technical paths behind details. No rights arise from client names or instance metadata.
- Verify same1.0.1/different-generation detection, preserved unknown fields, no secret disclosure and retained scoped authorization.

### Lot3 — stable MCP front door, live host contract

- Replace per-build executable registrations with one retained native entry point for a specified installation/data scope/connection identity. Use existing runtime-pointer/ownership/rollback principles, without adding a global install or requiring Python in portable distributions. Keep the bootstrap compatible/retained while processes use it; do not overwrite a locked executable.
- A startup-only launcher is insufficient: it selects a version only when a client reconnects. Keep the stdio session in a small broker and route descriptors/resources/requests to the qualified active host/generation. Do not leave tool definitions and guides pinned to the broker's own package. Permit documented offline diagnostics without claiming a live host.
- At an upgrade, stop admitting NEW calls on the obsolete path, let admitted calls finish and switch only at a safe boundary. Persist uncertainty for a broken in-flight call; never resend messages/commands/transfers automatically. New requests may be retried only when non-execution is confirmed and the caller chooses it. Respect frame limits, authentication, project scope, rollback and old-generation leases.
- Notify schema/resource changes only when supported, while retaining an explicit client-refresh fallback. Do not claim notification acknowledgement means the model sees the new tool set. Keep compatible old descriptors until refresh, or return a precise outdated error; never silently accept schema drift. Streamable HTTP would still need client catalog refresh and adds endpoint/auth migration, so it is not the first corrective step.
- Verify one continuing stdio client through a host/generation change, current docs/discovery, safe in-flight failure, rollback and cold start without rewriting client settings every build.

### Lot4 — controlled client refresh and connection actions

- Implement a client-connector port with capability discovery (reload supported/reachable, deferred until idle, unsupported). Codex adapter calls config/mcpServer/reload ONLY on the actual owning backend through a supported configured control channel. Do not start a separate server and report the desktop refreshed. Respect that this installed method reloads configuration globally, not one named server.
- When the desktop exposes no reachable control endpoint, report that limitation truthfully and provide the one supported manual recovery step; do not install a daemon, enable remote control, modify startup, attach a debugger or restart the whole app without explicit authorization. A future client plugin/official bridge may make that supported channel available, and is a separately qualified integration.
- A Close connection action drains a registered broker connection with an explanation and no privilege revocation. Offer it only with an explicit statement that the client may need to reconnect. Do not use blind process termination as the update mechanism.
- Success requires checking the SAME client/thread: generation/contract, current tool inventory, resource version and actual scoped list_projects. New raw MCP diagnostics alone are insufficient. AP membership/workspaces/permissions stay untouched.

### Lot5 — one update command and regression path

- Compose preparation, health check, safe host switch, stable-pointer update, stale-connection detection, optional owning-client refresh and same-client validation in one idempotent command. Retain backups, a precise completion/pending-user-reload result and rollback; no false all-up-to-date result based solely on healthy host/file edits.
- Test old launch/retired tombstone, same-SemVer drift, wrong/absent identity, inaccessible host, dynamic catalog updates, dropped stdio, both refresh-supported/unsupported clients and no replay of ambiguous mutations. Keep paid/native-agent calls unnecessary. Reuse the audit fixtures, turning only useful scenarios into maintained tools/tests.
- Align agent/human guides and tool descriptions with loaded-generation diagnostics, pending refresh and concrete recovery. No new project/team methodology. Qualify an existing external project client before closing this correction.

### Order and estimated scope

Lot1 then Lot2 are the immediate safety/usability fix. Lot3 is the durable update architecture; Lot4 bridges the unavoidable client boundary; Lot5 closes delivery. Initial engineering estimates: Lot1 20–40min, Lot2 20–40min, Lot3 45–90min, Lot4 30–60min (supported channel prerequisite may require separate integration), Lot5 20–40min. These are estimates, not measurements or a promise that the current desktop exposes a hidden API. Use a fresh scoped tracked objective for the implementation when available; account for token usage there rather than reusing the blocked historical counters.

## Artifacts and preservation

Local audit fixtures/results: local/apps/agent-relay/qualification/mcp-refresh-audit/{generation-comparison.json,isolated-refresh.json,isolated-close.json,proxy-before.json,proxy.stderr.log,fresh-native.json,retired-scope-readonly.json,preservation-result.json}. These contain observations, not operator-validated evidence. Scripts/schema dumps and private process/config details remain ignored local artifacts. Seven protected project/profile/review/connection files retain their pre-audit hashes. No source correction, registration edit, project/permission change, host/Codex restart, model/native-agent call, version/tag or release was made. Only the explicitly described obsolete facade closures and isolated diagnostics were performed. The current host remains healthy; existing desktop chats still require a real client-side refresh, not a claim of repair from this audit.

## Completion

Audit and correction plan complete at21:07UTC /22:07Europe/Paris on01/11/2026, approximately10minutes after the first timed checkpoint; initial investigation preceded it. Final current-host/fresh-scoped25tool discovery passed. Shared/document verification and whitespace checks passed. Scoped token consumption is unavailable because tracked goal creation was refused by the unrelated historical unfinished objective; its145775tokens/901seconds were not attributed to this audit or marked complete. Only this note/index are staged for the dedicated audit branch, then integration push under the standing delivery rule. No app source/runtime change exists to deploy. Implementation awaits a distinct explicit GO.
