# Qualification Reference

**Utility:** describes the four-case experimental harness and its directional interfaces.  
**Usage:** read before inspecting qualification code or interpreting its records.

## Implementation boundary

The [qualification source](../../poc/qualification/README.md) exercises native interactive tmux clients and separate Windows/WSL MCP endpoints. It does not implement the tray, production
authentication or a general command service. Its fixed route and directory-listing fixture are qualification restrictions, not a claim that all command forms are supported.

## Windows interface

The stdio facade has four tools. All return JSON in MCP text content; tool failures set isError true.

### execute_command

The input has program (string), arguments (string array) and workspace (string). This harness accepts only ls, a single -1 argument, and fixture. It runs the native program in the configured
workspace without an AI agent. Its result includes a fresh nonce, cwd, uid, stdout, stderr, returncode, request_id and wsl_model_invocations set to zero. Identity, location and nonce are checked
independently of the outer WSL exit code. A committed command result is reused for the run.

### send_agent_message

The input has client, either codex or claude. The tool addresses only the configured session. The controller types the one fixed qualification request literally, waits proportionally to its length
and sends Enter separately. An exclusive prompt marker prevents another call from silently creating another user exchange. Its acknowledgment is submission, not proof of a reply.

### receive_messages

The tool takes no arguments. It imports each client's outbox and reads recognized assistant output from its own native transcript. The response has replies, indexed by client, and inbox, a list
of message envelopes. Replies contain assistant_text, usage and mcp_initialized. Retrieval never types a new prompt and never executes a message body.

### read_inbox

The tool takes no arguments and returns inbox from the durable Windows database. It contacts neither WSL nor any model, including after the Linux fixture is removed.

## WSL interface

Only send_message is advertised and implemented. Its only argument is text, a string of at most 500 characters. Unknown tools, additional properties and command parameters are rejected at runtime.
The sender is bound by the endpoint's launch configuration, not supplied by the model. The destination is the configured Windows pilot.

The message envelope has message_id, sender, recipient, text and in_reply_to. All are strings except in_reply_to, which is null for an independently addressed message in this qualification. A
successful send returns sent=true and the envelope. A per-client exclusive marker permits one successful outbound message. The WSL endpoint contains no command-execution implementation.

This is a capability boundary in a controlled harness, not isolation against hostile processes running as the same Linux account. Messages are not converted to instructions granting authority.

## Native clients and telemetry

Both clients run in a real tmux PTY, with no initial prompt. They have isolated configuration/history directories and only the explicit message endpoint. Claude's inherited instruction files and
memory are excluded; Codex uses a minimal replacement instruction file. Native authentication is reused by reference. Managed-policy behavior is not bypassed.

The inspector reads only isolated transcript files. Codex assistant text comes from response_item records with assistant output_text; token_count records supply native totals. Claude assistant
text and usage come from assistant messages, deduplicated by message ID for usage aggregation. The first parser does not recognize every installed Codex user/tool record shape. Its recognized
record counters are not assertions about user-turn count. Controller prompt markers and server-side message records provide separate evidence.

One controller prompt can cause multiple native API messages when the agent uses its tool. That is one user exchange, not a promise of one inference request. Client metrics and the parent goal
counter are distinct measurements and are not silently merged into a billing total.

## Cleanup

Cleanup records the dedicated tmux server's process tree, stops that server and terminates only still-live tracked descendants with unchanged process-start identities. It verifies those processes
are no longer active before removing the exact run directory. Credential links are not followed during directory removal. Small test diagnostics and the Windows database remain in ignored local
storage; the observation report is the versioned review surface.
