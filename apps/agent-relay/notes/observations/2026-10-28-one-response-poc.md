# One-response POC outcome

**Date:** 2026-10-28, Europe/Paris morning.  
**Source:** the explicitly authorized POC run and its native-client, MCP and persistence diagnostics.  
**Validation:** observations awaiting Florent's validation; not evidence.

## Verdict

The POC is inconclusive. Native Windows Codex discovered and called the experimental stdio MCP tools, but no Claude response was recovered. The full round trip and response recovery objective
remain unmet. No second worker invocation was attempted after that uncertainty.

The scope was narrowed to one short Claude response and restart checks without another request (evidence `2026-10-28-a-go-authorizes-only-the-one-response-poc`). It did not authorize the broader
idle/long/busy input or human-takeover experiments from the earlier proposal.

## Run identity and mechanisms

The run identifier is `5e06cd2b-310a-4ba0-a0d1-a95770f26250`. Windows run data is under the local experiment directory described by the [POC reference](../../doc/developing/poc-reference.md).
The Linux workspace and tmux socket carried that same unique identifier. Source is retained for review; global client configuration files were not edited.

The installed Windows caller reported `codex-cli 0.164.0-alpha.11.1`; the Linux client reported Claude Code `2.1.312`, with tmux `3.4`. Windows Python reported `3.11.9`, Linux Python `3.12.3`, and
Windows SQLite `3.45.1`. No software was installed or updated.

Claude was configured for print mode under tmux, safe mode, no built-in tools, strict MCP configuration, disabled slash commands, no Chrome integration, no session persistence, low effort, one
agentic turn and an estimated USD 0.05 cap. Its only requested response was `RELAY_OK` followed by the run identifier. This was not an interactive CLI qualification.

## Local checks

All three Python sources compiled. A cached-result test forbade any WSL call and confirmed that a committed Windows result remained readable, duplicate submission did not dispatch, and another
request identifier was rejected. It used fabricated local test data, not a Claude result. A failed initial shell quoting attempt did not execute its Python test; the corrected stdin-based test passed.

Preparation created the Linux workspace and confirmed no ancestor project instruction file. A read-only preflight found no managed-settings file at the checked Linux location. The CLI's safe
mode was used to exclude discovered customizations while retaining native authentication, rather than using bare mode, which has different authentication behavior.

## Native MCP attempts

The first native pilot failed before dispatch because its non-interactive approval policy could not prompt for the tool. The database contained zero tasks, so no worker launch was attempted by
that pilot. Its exact tool error was:

```text
MCP tool call requires approval, but approval policy is never
```

The runner then used per-process approvals for only the two already-authorized POC tools, as described by the
[official MCP configuration reference](https://learn.chatgpt.com/docs/extend/mcp?surface=cli). No global approval policy or client file was changed. One explicit diagnostic retry of the Windows
pilot was performed; the first attempt's logs were retained separately.

The second pilot's actual MCP tool events were:

```text
submit_task(request_id=5e06cd2b-310a-4ba0-a0d1-a95770f26250)
  state=dispatched; result_confirmed=false
get_result(request_id=5e06cd2b-310a-4ba0-a0d1-a95770f26250)
  status=outcome-unknown; retry_allowed=false
```

Both protocol calls completed, but their successful transport did not establish successful task execution. The native process exited zero while returning an unknown task outcome.

## Missing Linux state

After the bounded result wait, the Linux workspace, result file, invocation marker and dedicated tmux session were absent. The response was unavailable, and no surviving marker established
whether Claude had actually been invoked. The correct count is one launcher attempt and an unknown number of actual Claude invocations, bounded by that one wrapper attempt; it is not a confirmed
one-response run.

A subsequent mount check reported the filesystem containing `/tmp` as `ext4`, mounted at `/` from `/dev/sdc`. Directory timestamps for both `/tmp` and `/var/tmp` were from the subsequent startup.
These observations do not establish the cause of the workspace's disappearance. No claim is made that all WSL temporary files always disappear or that another path would solve the issue.

The initial launcher accepted tmux success without checking that its worker source still existed, and its timed-out result read did not persist the unknown state. Both defects were corrected in
the source: launch now verifies the files in the same WSL invocation and requires a correlation marker; a timed-out result read commits outcome-unknown. The corrected launcher was not exercised
with Claude, to avoid another possible model call.

The recorded timed-out task was reconciled from dispatched to outcome-unknown, retaining the original native events. Its missing response was not replaced with a synthetic success.

## Restart and duplicate checks

A fresh relay process read the reconciled unknown outcome and rejected duplicate dispatch. A distribution stop was confirmed from the running list. The same outcome was read while WSL was
stopped, then again after a fresh WSL response confirmed the `sowapps` identity. No worker was started during these checks.

This validates persistence of the uncertainty record, not persistence of a Claude response. The success-case result recovery gate remains open.

## Consumption

The tracked objective started at 09:03 local time. Its checkpoint reported 65,361 tokens and 800 elapsed seconds, with status blocked. These are the goal tool's values, not a bill or a provider
quota percentage. The final user-facing report supplies a later checkpoint if available.

The native pilot JSONL reported:

| Attempt | Input tokens | Cached input tokens | Output tokens |
|---|---:|---:|---:|
| Approval refusal | 41,574 | 25,472 | 114 |
| Dispatch and read | 156,313 | 149,504 | 424 |
| Total | 197,887 | 174,976 | 538 |

Cached input is part of input, not an additional count. The native pilots therefore reported 22,911 uncached input tokens plus 538 output tokens. Those subprocess metrics are recorded separately
from the parent goal counter; no billing total or deduplicated aggregate is inferred.

Claude returned no usage data. Its token consumption, subscription quota impact and estimated cost are unknown. The configured budget is not evidence that consumption was zero or that a response
was generated. The main avoidable overhead was the first Windows approval refusal; model prompts also carry substantial native-client context despite the tiny test request.

## Cleanup and retained artifacts

The runner confirmed the Linux workspace absent and the dedicated tmux socket had no session. No global MCP configuration, startup registration, service or persistent agent was created. The
distribution was left restarted after the state-recovery check.

The Windows run directory retains the database, native JSONL and stderr, summaries, failure reconciliation and cleanup record for review. It is ignored by Git. Small synthetic offline-store test
directories and Python bytecode are disposable; the report is the durable review surface.

## Next decision

The separately authorized [storage checks](2026-10-28-linux-storage-checks.md) compare candidate locations without another model call. Their results do not retrospectively establish a successful
real-agent exchange. Do not proceed to MVP implementation or another Claude request without a new GO for its specific scope. No client switch or automatic retry is implied.
