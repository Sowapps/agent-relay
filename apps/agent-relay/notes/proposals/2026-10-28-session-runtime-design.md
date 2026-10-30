# Session runtime implementation design

**Date:** 2026-10-28  
**Status:** technical implementation decisions inside the globally authorized evening scope.

## Session ownership

Keep configured profiles as permission templates. Persist session identity/name/template/root in SQLite separately; session creation cannot grant file/command permissions beyond its template.
Each created session receives its own deterministic /srv/agent-relay-session-UUID root, native home/workspace, private tmux socket and client conversation. Reuse the qualified provisioner/controller rather than copying protocol implementations or placing state in /tmp.
One root per session adds a small directory setup cost but avoids cross-session context, environment and terminal-input collisions, preserves human tmux access and allows safe exact-root stop. A client name remains codex/claude; the user-facing session name is independent persisted metadata.
Creation persists intent before provisioning. A repeated explicit session identifier must recover that same root rather than allocate another one. Permission changes require a new/restarted session, not mutation of an active native context.

## Work routing and concurrency

Add optional SessionId to requests/messages; old records retain null. Resolve the configured template and session root at the trusted host boundary. Native outbox identity is stamped with the session by the importing host, not accepted from agent text.
Replace the single running task with per-session admission and a configurable host concurrency ceiling, initially4. Different sessions may work concurrently; a given native terminal receives only one operation at a time. Identical operation IDs remain idempotent; busy/conflicting IDs never dispatch twice.
Inspect/list/state requests remain concurrent while mutations and profile changes retain appropriate guards. Stop/interrupt applies only to the stored dedicated session; uncertain external effects remain unknown and are never automatically replayed.

## Retrieval and storage

Migrate additively from schema2: session/transfer documents, optional metadata on old work/messages. Preserve SQLite durability and all existing rows.
Use stable database-row cursors and bounded queries for history/inbox; provide session/profile/type/state/text filters. Inbox incremental cursors belong to the consumer, not a global implicit read flag.
Recover all pending/running rows after host restart, not merely the last200 displayed operations. UI and MCP use the same records/correlation IDs.

## File transfer boundary

Stream single files between each session's explicitly exposed Windows workspace and its Linux workspace. Keep installation files and durable data separate from both workspaces. Relative paths only; reject traversal, symbolic links/reparse points and existing destinations unless overwrite is explicit. Initially cap a file at256MiB to keep cancellation and disk usage bounded.

Use the same per-session work admission as model messages, preventing relay-managed work from modifying paths during a transfer. Native preflight/finalization checks the Linux path again; publish through a sibling temporary file and verify SHA-256 before an atomic rename/link. Store a durable receipt before confirming completion. An interruption without a receipt remains unknown and never triggers automatic recopying. These checks are application boundaries, not an OS isolation claim against arbitrary other processes running as the same user.

Uploads require the template's local-work authorization; downloads only read the assigned workspace. The WSL agent's MCP still exposes messages only. No folder sync, arbitrary Windows roots or implicit overwrite.

## Acceptance checks

Focused local tests: old schema compatibility, named-session metadata persistence/idempotent creation intent, same-session busy versus independent-session parallelism, filtered cursor retrieval and no replay after interruption.
Native short acceptance: two distinct Codex sessions/continued context and a short Claude exchange; messages carry the correct session; only dedicated fixtures, no unrelated session or /home/sowapps/projects. Preserve the paid-call budget and quota cap.
Current-chat first-class MCP discovery is still a separate open gate; no standalone success may be relabeled as that proof.
