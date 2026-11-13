# Owning Codex refresh continuation

**Utility:** retains the actual control-channel boundary and the implemented diagnostic correction.  
**Usage:** resume the unfinished stable rollout from this checkpoint; do not repeat the isolated protocol tests already indexed in the previous qualification.

## Authorization and boundary

Operator: « Fais la suite », after the qualification and proposed owning-client refresh integration. Continue the existing update scope. No Windows UI access, Codex restart, daemon/listener installation, user project edits, model call, task replay, connection rewrite, tag or publication. Branch `codex/owning-client-refresh-diagnostics` from main `dd6c05d`.

The existing rollout objective still returns blocked with unchanged historical 66,276 tokens / 1,081 seconds. Those counters do not measure this continuation. The installed version/client completion remains unfinished, so the goal is not falsely completed. Checkpoint time 13/11/2026 09:22:31 Europe/Paris; initial reads preceded this checkpoint.

## Actual inspection and decision

Recovered the previous dated qualification first. No repeated synthetic model turn or live MCP closure was needed. Passive identity inspection found the actual desktop Codex backend PID 16604, executable bin/9e1da79431da526b/codex.exe, launched as app-server without --listen. Installed CLI documents stdio as the default. Its expected app-server-control socket is absent, and this chat's available Codex tools have no MCP reload action.

The [official app-server reference](https://learn.chatgpt.com/docs/app-server) still documents config/mcpServer/reload and separate listener transports. Starting another listener/backend cannot refresh this existing process. No supported way to add a control listener to the already-running stdio backend was established. Do not claim that an integration has made the current desktop remotely reloadable. Do not inject internal IPC, attach a debugger, change app startup or take UI control as a workaround.

## Concrete correction

client_refresh.py previously caught every request exception and returned a generic state without retaining the cause. It also prepared its reports directory outside that catch. It now reports a precise failure phase and primary exception type; preparation errors enter the same failure path. Expected absent capability has code client_refresh_unsupported and an explicit missing-executable/missing-socket reason, without claiming that all possible control channels are absent.

pilotage_stable.py passes the existing complete_update.report_error capability into this adapter. That shared C#-owned diagnostic path retains the exception and exposes its identifier/details reference through the existing clientRefresh receipt; no duplicate error-storage policy or product service was introduced. Adapter failure before acknowledgement stays unreachable; failure after accepted reload stays reload_pending. Diagnostic persistence failure is explicit and does not erase the primary phase/type.

## Checks and retention

The existing test-client-refresh.py fixture now also checks failed reload, failed post-reload catalog check, retained diagnostic reference and diagnostic-storage failure. Existing current/stale contract, named server and missing-control checks still pass. py_compile for the two production adapters and git diff --check pass. Zero model calls, no installed stable mutation. The test is an adapter fixture, not a real desktop refresh test. Raw test output is retained under local/apps/agent-relay/qualification/owning-client-refresh-2026-11-13/.

## Delivery and next action

Commit/push and integrate the tested tooling/documentation correction under standing delivery. No portable application payload change and no rebuild/version increment/deployment is justified for this tooling correction. Stable remains the previously observed 1.1.1; 1.1.2 is qualified but its full deployment still awaits release of mapped clients. Do not restart the expired standby blindly or describe the deployment as complete.

The remaining boundary is actual client lifecycle access, not another untested reload algorithm. If the current client exposes no reload action, completing the mapped-broker update requires a convenient actual client closure/reopen, already documented; no automatic closure is authorized. A future supported Codex control endpoint can use the existing adapter and same-client verification. Self-improvement: report this capability boundary at preflight rather than promising an automatic reload from an isolated test, and retain any reachable-control failure through the shared diagnostic owner.
