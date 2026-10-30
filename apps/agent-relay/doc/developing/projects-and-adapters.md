# Projects, participants and adapters

**Utility:** describes the production authorization, neutral application and adapter boundaries.  
**Usage:** read before adding a frontend, endpoint adapter, project rule or migration.

## Identity and authority

RelayProject, Endpoint, Workspace and Session use stable UUIDs. Names are presentation metadata. Membership assigns main/principal, secondary/secondaire, reviewer or observer roles within a project; a role grants no capability by itself. Central relay-projects.json explicitly lists capabilities, workspace read/write/replace and command identifiers. The caller cannot acquire rights by placing configuration in its workspace.

Each MCP connection names a protected credential reference, bound to one endpoint and a set of projects. Windows DPAPI protects the random token on disk; SQLite retains its hash and scope. The facade never defaults to the administrative credential. Every pipe request authenticates the caller; the use case checks current membership before accessing results, messages, sessions or execution. Revocation denies subsequent requests. It cannot undo effects already started.

The local administrative UI and maintenance command can configure projects/connections. Operational actions explicitly select the acting participant and use the same project policy as MCP. The administrator is trusted; same-account processes and operating-system administrators are outside the application authorization boundary. Project rights do not replace authorization for mail, push, deployment, deletion or publication.

## Dependency direction

Core owns RelayApplication, ProjectPolicy, neutral scopes/artifacts/session/read contracts and WorkService. Its session, journal, host-inspection, recovery, executor and store ports contain no WSL, tmux, Windows, Codex or Claude types. A non-platform memory adapter exercises the actual use cases in deterministic integration tests.

Infrastructure translates MCP/pipe requests into those use cases and implements SQLite, protected credentials and adapters. ScopedSessions implements the first native-session adapter. wsl-agent, wsl-command and local-files are installed adapter bindings, not business roles. Another machine requires an implementation of the same ports plus its authentication and transport qualification; this architecture does not claim an SSH product is delivered.

Windows composes the runtime and provides ProjectWindow for monitoring/configuration. The legacy local echo and legacy profile coordinator remain regression/read-compatibility paths; production agent connections cannot use them to bypass project scope.

The local monitoring frontend has a separate passive source adapter. Authenticated administrative observe_* routes expose bounded retained project facts without native side effects. Participant connections cannot enter those routes; their existing scoped MCP tools remain authoritative. Core monitoring projections know identities and states, while adapter-provided descriptors carry connector/environment labels and supported actions. See [Windows interface](windows-interface.md) for presentation and cursor ownership.

## Workspace and native session

A real existing workspace is independent of the private session runtime. The latter stores helpers, private native configuration, tmux socket, logs and durable message indexes under its owned /srv/agent-relay-session-UUID root. There is no implicit workspace copying or synchronization. Linux user/distribution/client and optional nativeExecutable belong to the technical binding. A changed physical context requires a new session.

Native local-work authorization checks both participants and workspace grants. File transfer write permission is a separate capability. Native approvals remain active. Real project instructions may apply to real work; isolated qualification does not silently inherit unrelated home instructions. Authentication reuses protected native account references: Codex uses its auth-file reference; scoped Claude reads the existing private OAuth credential at launch into its environment, without writing a token into launch.py or project files. After shared Claude credentials change, explicitly reopen its idle relay session to refresh that environment. Authentication metadata cannot prove a paid request will succeed.

## Durability, concurrency and recovery

Schema4 adds central connection scopes, import cursors, task state events and prepared command dispatches to the existing durable tables. WAL/FULL transactions retain pending intent before dispatch. An operation has a request, task and attempt identity; retrying the same UUID returns retained intent, while changing its content is a conflict. No automatic model or command replay occurs.

Startup marks unfinished rows unknown, including older retained rows. Scoped get_result can reconcile a matching Windows transfer receipt or a command receipt whose prepared nonce, scope, executable/arguments, user and workspace match. Inbox import can resolve a correlated late message. Publication before receipt persistence remains unknown if no authoritative receipt is available. Cancellation requests interruption; it never promises rollback.

One relay operation runs per session. Independent sessions use the configurable host capacity (default4,1..64). Shared-workspace operations use adapter resource leases unless concurrentWrites is explicitly enabled. These leases coordinate the relay process, not unrelated terminals or applications; avoid simultaneous manual input into an active relay conversation.

## Protocol and retrieval

The stdio facade identifies itself as agent-relay2.0 and implements JSON-RPC2.0 with protocol2025-11-25. Unsupported methods/arguments fail explicitly. Input is read in bounded chunks and checked against a1MiB UTF-8 frame budget; named pipes use the same byte ceiling. MCP schemas require explicit project/session/artifact identities and reject unknown fields. Large full details can be refused; filtered previews remain available.

ProjectJournal applies project/endpoint/session/task/type/state/date filters in SQL, with content/label search rather than accidental raw JSON matches. Pages have stable row cursors, count limits and a400KiB payload budget for Unicode expansion. Native inbox imports use per-session SQLite cursors, deduplication and rotating bounded session batches. One consumer does not mark messages globally read. Native WSL capability remains messages only; received text never becomes a Windows command.

## Maintenance and qualification

The publication tool stages and checks a copied configuration/database before stopping an exactly identified idle owned host. It retains an offline snapshot and failed migrated files, confirms the new authenticated pipe, and publishes the stable pointer only after health succeeds. Rollback restores the old database and protected credential set together; schema4 cannot simply be opened by an older runtime. Quiesce clients before replacing an old host: its active-work guard is an observation, not a cross-version transactional drain protocol.

Build/tests, standalone MCP, actual native responses, current-desktop tool availability and visual acceptance are separate checks. None substitutes for another. Run-specific results and remaining qualification gaps belong in notes, never in a claim that compilation proves full acceptance.
