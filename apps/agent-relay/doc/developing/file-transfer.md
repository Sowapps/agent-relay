# File transfer implementation and limits

**Utility:** records the actual transport, costs and limits so future work does not rediscover them.  
**Usage:** consult before changing transfer behavior or selecting a transport for another endpoint.

## Current transport

The Windows MCP tool submits durable work through the pipe host. FileTransferService resolves a named session and invokes fixed Python helpers through wsl.exe --exec for Linux inspection/finalization. File bytes are streamed by C# FileStream through `\\wsl.localhost\<distribution>\...`, not encoded into MCP messages.
The UNC path is a Windows filesystem access mechanism. WSL's p9rdr.sys redirector connects to its Plan9 filesystem server; WSL2 uses an internal Hyper-V socket. Agent Relay does not create that socket, mount a drive or serve SMB. See the [WSL architecture reference](https://wsl.dev/technical-documentation/plan9/).

The explicit copy buffer is1MiB; managed/OS buffering is additional. Upload writes a Linux sibling temporary file, calculates a streaming hash and asks the Linux helper to verify size/SHA-256 before publication. Download hashes the Linux source first, copies into a Windows temporary file and verifies bytes/hash before moving it to the requested path.
Linux publication uses a hard link when replacement is forbidden and os.replace when explicitly allowed, followed by directory synchronization and a durable Linux receipt. The Windows service stores the confirmed transfer receipt in SQLite before returning success. If publication precedes receipt persistence and the host stops, completion can remain unknown; it is never automatically recopied.

## Limits and costs

| Area | Current behavior | Consequence |
| --- | --- | --- |
| Scope | One file between explicit project endpoint/workspace artifacts, using the selected session's native workspace and a local-files endpoint | No directory transfer or arbitrary endpoint paths |
| Size |256MiB default, configurable1byte..4GiB through transfers.maximumBytes in relay-config.json | An application policy, not an established WSL limit; raising it requires workload qualification |
| Time |180seconds default, configurable30..3600 through transfers.waitSeconds; helper hashing shares this deadline | Size alone does not guarantee acceptance; this removes the independent20second hashing cap |
| Memory |1MiB explicit streaming buffer | No whole-file payload allocation; do not describe this as total process memory usage |
| Integrity | Linux performs an additional full read for SHA-256 | Verification has measurable disk/CPU cost |
| Disk | Full destination temporary file; existing target retained until publication | Require free space for the temporary alongside an existing destination |
| Many files | Per-file operation, helper startup, metadata access, flush/hash/receipt | High fixed cost; no batching or whole-directory manifest |
| Parallelism | One relay operation per session, shared with messages | Concurrent sessions are possible, not multiple copies within one session |
| Feedback | Durable byte/total/phase progress, at most twice per second, plus cancel_operation | No measured rate/ETA promise; cancellation is not rollback |
| Interruption | No offset/chunk resume; partial file may remain | inspect_transfer and explicit cleanup_transfer address only the retained operation's temporary; final files are preserved, active session/helper cleanup is refused |
| Authorization | Bound project/caller checks source read, destination write and explicit replacement grants | Separate from native local-work permission; application data/installation paths are protected |
| Identity | Explicit helper user; UNC access checks the distribution's default filesystem user matches that endpoint | A mismatch is refused; it needs a separately qualified helper-owned transport |
| Portability | Neutral artifact contract; installed byte adapter uses WSL UNC paths | Another machine needs an adapter and its own transport qualification |

Workspace path checks reject traversal and symbolic links/reparse points. They are application checks, not isolation against arbitrary same-user processes or a complete defense against concurrent external path replacement.

## Qualification boundary

The model-free acceptance scenario covers a binary file round trip, project identities and SHA-256; deterministic checks cover receipt scope and path refusal. It does not establish bulk throughput, large-file performance, low-disk behavior or transfer resumability. A matching durable Windows receipt can reconcile unknown work without recopying. A publication-before-receipt gap remains unknown when independent confirmation is unavailable.
Native integration is a reason to retain this transport as the WSL default candidate, not proof of optimal throughput. Windows/Linux filesystem crossings have costs; [Microsoft's interop guidance](https://learn.microsoft.com/en-us/windows/dev-environment/wsl-interop#performance-considerations) distinguishes native and cross-filesystem workloads.
No transport replacement is justified solely by these theoretical limits. The [V1 correction plan](../../notes/proposals/2026-10-29-v1-correction-plan.md#transfer-evolution-triggers) retains alternatives and the measurements that would justify them.
