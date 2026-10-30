# System I/O — Technical Target

**Utility:** states the decided separation between persistence, lifecycle and endpoint execution.  
**Usage:** read before selecting a store, startup mechanism or runtime supervisor.

Runtime mechanisms satisfy lifecycle and durability requirements without entering the neutral business core.

**Functional counterpart:** [System I/O functional target](../functional/system-io.md).

## Persistence boundary

Durable data is not held only in WSL process memory or terminal history. Persistence is accessed through a business-neutral port. Platform-specific storage and process details remain outside the
core (evidence `2026-10-16-relay-runtime-is-manual-and-durable`) (evidence `2026-10-16-relay-core-and-endpoints-are-neutral`).

## Runtime boundary

Windows lifecycle and WSL execution are separate adapter responsibilities. The domain model does not depend on a Windows service, a scheduler, tmux or a particular agent runtime
(evidence `2026-10-16-relay-core-and-endpoints-are-neutral`).
