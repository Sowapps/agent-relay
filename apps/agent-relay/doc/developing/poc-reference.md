# Limited POC Reference

**Utility:** describes the experimental relay's implemented boundaries and MCP contract.  
**Usage:** read before inspecting the POC source or interpreting its outputs.

## Scope

The [POC source](../../poc/README.md) contains a stdio server, a WSL worker wrapper and an explicit-step runner. It supports one configured request identifier and one fixed Windows-to-WSL route.
It does not implement the full [product target](../target-spec/README.md).

## Persistence

Run data is allocated under `local/apps/agent-relay/poc/<run-id>/` at repository root, separate from product workspaces and source. The server uses an experimental SQLite store with synchronous
commits. It commits dispatch intent before launching the worker; a repeated request does not dispatch again. An uncertain attempt is not retried automatically.

The isolated Linux run directory is allocated under `/srv/agent-relay-poc-<run-id>/`. It contains separate workspace and data subdirectories and a dedicated tmux socket. Its owner is `sowapps`;
root is used only to create and remove this exact run directory under the system-owned parent. No service or global client configuration is installed.

The wrapper records an exclusive invocation marker before calling Claude. It stores the result with a flushed file, atomic replacement and a directory flush. The server imports a correlated
result into the Windows store; subsequent reads can run while WSL is stopped.

The experiment does not establish storage guarantees against hardware failure, directory loss or a crash at every transition. Linux runtime data is removed during cleanup.

## Worker boundary

The wrapper runs Claude in print mode under tmux with safe mode, built-in tools disabled, strict MCP configuration, no session persistence and no Chrome integration. It requests one literal short
response, limits agentic turns to one and applies a client-estimated budget of USD 0.05. That estimate is not a subscription-quota measurement or a guaranteed billing limit.

The workspace has no ancestor project instruction files. Safe mode excludes automatically discovered customizations. No hook or permission bypass is installed. The wrapper has a process timeout;
a failed invocation is recorded rather than automatically repeated.

This is a one-shot worker wrapper, not qualification of Claude's interactive input handling, an interactive agent takeover or conversation resumption.

## MCP tools

Both tools receive one required string property, `request_id`, which must match the run configuration. Additional input properties are excluded by their JSON schemas. There is no arbitrary prompt,
path, endpoint, shell command or credential parameter.

### submit_task

The tool commits a task, launches its worker and returns a JSON object as text content. `request_id` identifies the request; `state` is dispatch-intent, dispatched, completed or failed. On a first
successful dispatch, `result_confirmed` is false. On a duplicate, `duplicate` is true and `dispatched_again` is false. Dispatch acknowledgment is not completion acknowledgment.

### get_result

The tool returns an existing Windows result without contacting WSL, or waits for the Linux result for a bounded interval before importing it. A pending read can still be delayed by one underlying
WSL command timeout. It never starts a worker.

The normal result contains these properties:

- `request_id`: string identifying the request.
- `source`: string, linux-result-import or windows-durable-store.
- `status`: string, completed or failed.
- `worker_invocations`: integer, one for this wrapper.
- `returncode`: integer exit status of the Claude process.
- `duration_seconds`: number measuring that process's elapsed time.
- `response`: string or null containing the client result.
- `usage`: object or null containing the client's aggregate token usage.
- `model_usage`: object or null containing the client's model-specific usage.
- `cost_usd_estimate`: number or null, the client's estimated cost.
- `num_turns`: integer or null, the client's reported agentic turns.
- `session_id`: string or null, the client-generated identifier.
- `stderr_tail`: string containing at most the last 1,000 diagnostic characters.
- `result_sha256`: string identifying the imported JSON payload.

A wrapper exception returns `request_id`, `status`, `worker_invocations` and an `error` string; the server adds `source` and the hash upon import. A timed-out result read returns `request_id`,
`status` set to outcome-unknown and `retry_allowed` set to false. An unknown or out-of-scope request is an MCP tool error, with text content and `isError` true.

## External response fields

The wrapper consumes the Claude print-mode JSON response. At its root, `is_error` is a boolean indicating client failure; `result` is the response string; `num_turns` is an integer; `session_id`
is a string; `total_cost_usd` is a number. `usage` and `modelUsage` are optional objects retained verbatim for diagnostics; the wrapper does not depend on their nested properties.

The [official CLI reference](https://code.claude.com/docs/en/cli-reference) owns the client behavior. Extra response fields are not used. The observed usage properties are recorded in the dated
report rather than treated as a stable provider-independent schema.

## Native pilot boundary

The runner invokes Windows Codex CLI with a per-process MCP definition. Its two tool approvals are scoped to this experiment's tools. It creates no user or project MCP configuration file, and
does not register the server into an already-running desktop conversation. Authentication remains with the native client.

The pilot JSONL consumer reads `type` as an event discriminator. A `turn.completed` event supplies the `usage` object. An `item.completed` event supplies the `item` object, retained for inspection.
Other properties are not used to drive execution. A native process exit code alone does not establish successful MCP tool execution; actual tool events and the stored result must be inspected.

## Limits

The stdio harness is a minimal experiment, not a general MCP implementation or a secured multi-project service. SQLite and stdio are experimental mechanisms, not accepted MVP architecture choices.
There is no cancellation, automatic restart, human-control arbitration, task queue, tray interface, event subscription or production authentication layer.
