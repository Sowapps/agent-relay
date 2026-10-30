# Local application architecture

**Utility:** explains ownership and the implemented first local exchange boundary.  
**Usage:** read before extending the core, persistence, transport or Windows UI.

## Boundaries

This reference owns the deterministic local exchange path. Read [MVP architecture](mvp-architecture.md) for the external command/agent workflow used by the application window.

The core owns neutral message/reply contracts, route validation, correlation and idempotent exchange orchestration.
Its persistence and responder ports contain no Windows, SQLite, pipe, WSL or AI-client types.
Infrastructure owns SQLite and the current-user named-pipe protocol. Windows owns composition, tray/status UI and client commands.
The deterministic demo responder is an infrastructure adapter behind the core's responder port.
The first endpoint is an explicitly labelled local echo demonstration, never an AI-agent reply.

Use one concrete owner for each serializer, frame codec, pipe-name calculation, argument parser and tool environment.
No dependency-injection framework, ORM, generic repository or event bus is needed for this slice.

## Exchange and persistence

Validate identity, route and bounded nonempty text before handling. An existing request identifier returns its original correlated exchange only if content matches.
Conflicting reuse fails explicitly. Serialize one active exchange; a concurrent submission returns busy instead of introducing an implicit queue.
Commit request and reply together in one SQLite transaction before acknowledgment. The local responder is deterministic and has no external side effect.
This atomic local exchange does not establish safe replay or exactly-once execution for future external commands/agents.

## Transport and lifecycle

Use one length-prefixed UTF-8 JSON request/response per connection, bounded in size and time. Both ends require CurrentUserOnly; the host requests FirstPipeInstance.
The pipe name derives from the canonical data-directory identity. There is one host per data directory; a second host fails rather than replacing it.
Malformed frames and unknown operations produce explicit failures and do not terminate the listener.
The host closes its listener and tray icon on exit. The client never silently starts a host or resubmits a request after a timeout.
The pipe accepts independent bounded clients concurrently; the local exchange core independently rejects overlapping echo execution. Production session admission is owned by [Projects, participants and adapters](projects-and-adapters.md).

## Data and tests

Development commands select `local/apps/agent-relay/data/` explicitly. Test data uses a unique persistent child of the application's local test directory.
Keep the data directory separate from source/installation and workspaces. Package versions and NuGet integrity locks are retained with the application sources.
Integration checks exercise the actual core, SQLite adapter, pipe host and client rather than duplicating these implementations in mocks.

## Development tools

`AppCommands.psm1` owns toolchain selection, the isolated environment, pinned restore, build, output discovery and test/run composition.
`RuntimeStaging.psm1` owns a verified content-addressed runtime copy shared by normal launch and process tests.
Running that copy leaves compiler output files unlocked; rebuilding does not replace an already running instance or change its version silently.
`relay.ps1` and the Windows UI both use the same application pipe client. Transport framing, pipe naming and JSON serialization each have one implementation.
