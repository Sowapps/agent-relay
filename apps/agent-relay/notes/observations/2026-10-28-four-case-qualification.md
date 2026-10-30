# Four-case qualification

**Date:** 2026-10-28, Europe/Paris.  
**Source:** actual direct-command output, isolated native interactive client records, WSL message endpoint records and Windows SQLite inbox.  
**Validation:** observations awaiting Florent's validation; not evidence.

## Verdict and authorized scope

The four cases passed in the qualification harness: direct WSL listing, a Codex WSL reply, a Claude WSL reply, and independently addressed WSL-to-Windows messages with command requests refused.
There was one controller prompt per client. The experiment was authorized by evidence `2026-10-28-wsl-can-initiate-messages-but-not-relay-commands`.

This is qualification of controlled protocol paths, not delivery of the Windows tray application. No WSL distribution shutdown or access to the existing home project tree occurred in this run.

## Run and isolation

The run identifier was `15949c77-6f89-4425-a7a0-dfb25bd7e8b3`. The Linux fixture root was under `/srv`, with separate client workspaces, data and native config directories, a dedicated tmux socket,
and a two-file direct-command fixture. All test files belonged to `sowapps`; root was used only to create/chown and remove the exact run directory.

No ancestor instruction file was found in the selected parent chain. Native credentials were reused through references without printing or copying their values. Native login-status checks
confirmed both isolated configurations were authenticated. No user config file, startup mechanism or software installation was changed.

Codex CLI 0.153.0 used its isolated CODEX_HOME, a minimal instruction file, read-only sandbox, disabled shell tool and its explicit message endpoint. Claude Code 2.1.312 used an isolated
CLAUDE_CONFIG_DIR, excluded inherited instruction names, disabled memory, plugins, hooks, slash commands and built-in tools, and its explicit message endpoint. Custom MCP had to remain available,
so Claude did not use the earlier print-mode POC's safe-mode launch. Its only permitted qualification tool was the relay message sender.

Both clients ran as genuine interactive tmux programs, without a positional startup prompt. Their MCP initialization was observed before the controller typed anything. Their configuration marked
only the generated fixture directory trusted. No existing agent session was selected or modified.

## Direct execution

The Windows MCP test client requested ls -1 in the fixture. No WSL agent had been started for this case. The result had a fresh checked nonce, the exact fixture cwd, uid 1002, empty stderr and
returncode zero. Its stdout was:

```text
alpha.txt
beta.txt
```

The first reader reported the Python wrapper's cwd rather than the child command's cwd, which correctly failed the location assertion. That reporting bug was corrected and the read-only listing
repeated. There was one successful command record; repeated retrieval reused it. No model turn was needed for this case.

## Interactive exchanges and originated messages

The controller sent one fixed short prompt per client, literally, with proportional wait and Enter in a separate tmux call. An exclusive per-client prompt marker prevented silent resubmission.
No additional prompt was sent during inspection, retrieval, denial checks or cleanup.

The native assistant output records contained:

```text
CODEX_REPLY 15949c77-6f89-4425-a7a0-dfb25bd7e8b3
CLAUDE_REPLY 15949c77-6f89-4425-a7a0-dfb25bd7e8b3
```

Each agent also called its native MCP send_message tool. The server committed a new message addressed to windows-pilot with in_reply_to=null. Codex produced message
`cc0a6e33-7c42-4d44-80da-eb9ce31200f8`; Claude produced message `c20a18c8-772e-4d33-877b-ea83caea0e7e`. The sender came from endpoint configuration, not the text supplied by the model.

The Windows inbox imported both envelopes, retaining origin, destination, independent identity and text. Repeated collection did not create duplicates. Origination was exercised by a deliberate
instruction in the single test prompt; this does not claim autonomous unsolicited behavior or the automatic waking of an inactive Windows chat.

## Directional refusal and durable retrieval

The WSL endpoint advertised only send_message. A deterministic MCP client, with no model, tried execute_command and a forged command property on send_message. Both were refused, and the negative
fixture wrote no outbound message. The WSL message server contains no command-execution implementation.

After receiving the two messages, the Windows command table still contained only the committed direct listing. Message text was never executed. Following Linux cleanup, a new Windows MCP server
process read both unchanged messages from its durable inbox without contacting WSL or any model.

The boundary is scoped to the exposed relay capability. It does not claim a hostile same-account process cannot access host files or that an agent loses its separately authorized local tools.

## Consumption

Codex reported 20,211 input tokens, including 9,856 cached input tokens, and 118 output tokens: 20,329 total in its native counter, with zero reported reasoning output tokens. The displayed native
model was gpt-6-astra at low effort; no model was explicitly selected for the run.

Claude reported 4 ordinary input tokens, 1,298 cache-creation input tokens, 1,050 cache-read tokens and 138 output tokens across two assistant API messages: 2,490 tokens across those categories.
One user exchange included a tool call followed by the final reply; it was not a promise of one inference request. No billing or quota percentage is inferred from those counters.

The parent objective closed complete with 62,246 tokens and 1,302 seconds, or 21 minutes 42 seconds. This is a separate measurement; native-client counters are not silently merged into a billing
total. No additional native Windows pilot model was launched for this lot.

## Audit limitation

An audit initially equated recognized native user-record count with controller prompt count. The installed Codex record shapes were not fully recognized, yielding zero recognized user records and
no recognized function_call entry despite its actual assistant output and server-side message record. The assertion therefore failed; it was not an observed second exchange or a missing reply.

No model was rerun to repair that parser. Inspection counters are labeled recognized records rather than submitted turns. One controller prompt is evidenced by the exclusive dispatch markers and
the controlled send path; a successful message is evidenced by the endpoint's exclusive marker and committed outbox. A complete native Codex user/tool event decoder remains necessary for broader
telemetry and interruption handling. The final cross-check uses those independent records and passed all four functional cases.

## Cleanup

Cleanup tracked six processes belonging to the dedicated tmux tree, stopped that server and terminated only still-live tracked descendants with unchanged process-start identities. None remained
active. The exact fixture root was removed after parent, name, resolved location and ownership checks. Credential references were removed without following their targets.

The ignored Windows run directory retains direct-command output, message records, SQLite state, per-client diagnostics, denial results, cleanup confirmation and the qualification summary. It does
not retain credential values. The documented observations remain the versioned review surface.

## Coverage boundary

The Windows side used a JSON-RPC stdio test client directed by this Windows Codex conversation. These new tools were not installed in the desktop application's MCP configuration. Earlier POC work
covered a native Windows Codex MCP caller, but that is not substituted for production integration of this expanded interface.

The [qualification reference](../../doc/developing/qualification-reference.md) describes the implemented harness. A tray application, project-wide authorization, arbitrary commands, concurrent
human control, crash-during-turn recovery and inactive-chat notifications are not qualified here. Only the named directory-listing case and the observed client versions/profiles are demonstrated.
