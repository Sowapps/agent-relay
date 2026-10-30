# Operation architecture

**Utility:** indexes the production operation boundary and durable execution rules.  
**Usage:** read before changing dispatch, persistence or endpoint integration.

## Production ownership

[Projects, participants and adapters](projects-and-adapters.md) owns current neutral use cases, central policy, protected caller scope, session/workspace separation, receipts, concurrency and protocol limits. The [local exchange reference](application-architecture.md) describes the deterministic echo regression path, which cannot substitute for a native agent response.
Core owns WorkService, pending intent, admission, task/attempt correlation and state transitions. Infrastructure owns SQLite schema4, MCP/pipe translation and concrete endpoint/file/native-session adapters. Windows owns composition and project monitoring. The WSL message-only MCP stamps trusted project/sender/recipient/session identities and durably indexes messages before acknowledging them.

## Execution semantics

Persist pending intent before acceptance and running before dispatch. Complete only with a verified result; nonzero command exits are failures. Unfinished work is recovered as unknown, with no automatic replay. A repeated UUID returns its original record only for identical content.
ProjectJournal and native SQLite inbox indexes bound retained reads end to end. A full result remains individually addressable. Filtered pages, native import cursors and durable events have one implementation per owner. File streaming/integrity behavior belongs to the [transfer reference](file-transfer.md).

## Reusable tools and checks

Reuse AppCommands, RuntimeStaging, LocalProcess, RelayJson, PipeFrames, BoundedLines and StdioRpc. The ordinary suite exercises actual Core/SQLite/pipe authorization, non-platform adapter use cases, duplicate/busy/uncertain/recovery states, exact text and Unicode page bounds without WSL/model calls.
v1-acceptance.py separates model-free preparation/local checks from explicit paid agent/continuation phases. Exclusive markers prohibit automatic paid retries. A standalone MCP client's success does not establish tools are loaded in the current desktop conversation.
