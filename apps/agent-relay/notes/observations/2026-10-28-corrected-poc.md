# Corrected one-response POC

**Date:** 2026-10-28, Europe/Paris morning.  
**Source:** native Codex MCP events, correlated Claude output and restart checks.  
**Validation:** observations awaiting Florent's validation; not evidence.

## Verdict

The limited POC passed: Windows Codex called the relay through MCP, one short Claude response was imported, and the same result remained available after fresh relay processes and a WSL distribution
stop/restart. Recovery made no additional Claude request. This is not qualification of the full MVP.

## Scope

Florent authorized the corrected POC with a persistent location, separate workspace and data, instruction isolation and no access to the home project tree
(evidence `2026-10-28-corrected-poc-excludes-the-home-project-tree`). Only the single-response experiment was executed. There was no multi-message, interactive takeover or busy-input campaign.

The run identifier was `c49fcdff-388d-4295-bd46-f9e29d3bcb26`. Ubuntu-24.04 ran the worker as `sowapps`. A dedicated directory under `/srv` contained workspace and data subdirectories and the tmux
socket. Root was used only to create/chown and remove that exact run directory under the system-owned parent. No client configuration file, service or startup mechanism was installed.

## Isolation before inference

Existence checks found the user instruction file at `/home/sowapps/.claude/CLAUDE.md`. The checked parent chain for the selected location had no CLAUDE.md, CLAUDE.local.md, AGENTS.md or checked
instruction files beneath an ancestor's `.claude` directory. The project tree was not read or written.

The installed Claude CLI reported version 2.1.312 and supported safe mode. The actual launch used safe mode, disabled built-in tools, strict MCP configuration, disabled slash commands, no Chrome
integration, no session persistence and a minimal replacement system prompt. The checked managed-settings file was absent. Native authentication remained with the client.

These checks establish the ancestry and launch controls; the complete inference request was not intercepted to inspect its context. Safe-mode exclusion follows the installed client's advertised
contract. The user's instruction document was not copied, modified or passed into the prompt.

Before inference, an independent distribution restart confirmed that the worker source and request still existed with matching identities and SHA-256 hashes. PID 1's start time changed. No model
was invoked by this preflight.

## Live exchange

The Windows pilot used a per-process stdio MCP definition with approvals restricted to the two POC tools. Its recorded calls were submit_task and get_result, both for this run's request identifier.
There was no approval refusal or pilot retry in this run.

The worker was a one-shot print-mode CLI invocation inside a dedicated tmux session. It returned:

```text
RELAY_OK c49fcdff-388d-4295-bd46-f9e29d3bcb26
```

Claude reported one agentic turn; one exclusive invocation marker existed. The process exited zero in 2.46 seconds, with no stderr. The relay imported a completed result rather than inferring
completion from terminal activity. Its payload SHA-256 was `3b838aa8658402fc873155be304e341e5829ac5e05cb871edccdf9ee1b1a0446`.

## Recovery checks

A new relay process read the committed Windows result. The distribution was then terminated and its stopped state confirmed from the running-distribution list. Another new relay process read the
same result while WSL was stopped.

After restarting the distribution, the Linux result bytes had the same hash and response. The invocation marker count remained one. A further relay process returned the unchanged committed
result. Repeating submission returned duplicate=true and dispatched_again=false. No worker or model was restarted by those checks.

The test covers recovery after a completed result. It does not cover a crash during inference, a result written but not imported, every storage transition or exactly-once execution.

## Consumption

Claude's response reported model `claude-opus-5-5`, one turn, 2 ordinary input tokens, 736 cache-creation input tokens, zero cache-read tokens and 36 output tokens: 774 tokens across those categories.
The client's cost estimate was USD 0.006616. This is a client estimate at listed rates, not a subscription bill or a measured percentage of remaining quota.

The separate Windows pilot reported 56,209 input tokens, including 36,864 cached tokens, and 181 output tokens. That is 19,345 uncached input tokens plus 181 output tokens. Its metrics are recorded
separately from the parent tracked objective, not silently added to or substituted for that objective's counter.

The parent objective was created before work and closed complete after the run, recovery, cleanup and documentation checks. The closure tool reported 26,714 tokens and 566 seconds, or 9 minutes
26 seconds. No token budget was set because Florent did not specify one. These goal counters and the subprocess metrics above are distinct measurements, not a claimed billing total.

## Cleanup and retained data

The dedicated tmux socket was confirmed session-free, and the exact Linux run directory was removed after its parent, name, owner and resolved location were checked. Ubuntu-24.04 was left
restarted. The only distribution stops were those in the authorized preflight and recovery tests; these stop the distribution's processes, not merely the POC session.

The ignored Windows run directory retains its configuration, isolation/preflight records, native JSONL and summary, result, SQLite store, recovery report and cleanup report. Product source and
current interface documentation remain under their normal repository locations. No generated result is promoted to evidence without operator validation.

## Remaining boundary

The corrected harness uses persistent run files and a persistent socket instead of relying on temporary-directory survival. The reason fresh files disappeared in the earlier temporary-directory
test is still not established; the successful run does not retrospectively explain it.

The [POC reference](../../doc/developing/poc-reference.md) describes the implemented experimental interfaces. Tray lifecycle, multi-project permissions, interactive agent control and additional
client combinations remain unimplemented or unqualified. SQLite and stdio remain POC mechanisms, not selected MVP architecture. Further work requires its own explicit GO.
