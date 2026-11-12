# Codex reconnection and catalog qualification

**Utility:** preserves previous and repeated measurements so client-refresh claims remain recoverable.  
**Usage:** resume from this record before repeating diagnostics; raw reports are local observations, not operator-validated evidence.

## Scope and checkpoint

Operator: « Pour moi, tu as déjà testé et tu as oublié. Ce n'est pas normal, chaque résultat devrait être conservé. Qualifie ça ». Continuation of the unfinished stable rollout: recover historical proof, repeat useful isolated tests and inspect currently exposed control capability. No native model calls, business replay, Windows UI control, actual Codex restart, project edits, connection-registration changes or stable deployment.

Branch: `codex/qualify-codex-mcp-refresh`, from `7a4d831`. Started 12/11/2026 22:16:19 Europe/Paris. Existing rollout objective still returns blocked, 66,276 tokens / 1,081 seconds unchanged. Those historical counters do not measure this qualification. No delete/rename/reactivation API exists; do not falsely complete the unfinished rollout to free its slot.

## Recovered proof

The [01/11 audit](2026-11-01-mcp-refresh-audit.md) already distinguished all three cases on Codex 0.166.0-alpha.5.1: notification alone left the inventory unchanged during observation; explicit reload updated the isolated thread's tools/resources; terminating its identity-checked MCP fixture produced Transport closed until explicit reload. It also recorded actual desktop legacy-facade closures without automatic replacement. These results were preserved, not missing; the later answer failed to retrieve and communicate them precisely.

Original raw results and fixtures remain untouched in `local/apps/agent-relay/qualification/mcp-refresh-audit/`, notably `isolated-refresh.json`, `isolated-close.json`, `closed-legacy-facades.json` and `preservation-result.json`. Broker-only test harnesses and mock control-adapter tests are not proof that the actual desktop refreshed.

## Repeat observation — 12/11/2026

Installed executable: Codex CLI 0.167.0-alpha.4.1. The same historical fixtures were copied to a fresh persistent directory, with only their output root changed. They use the existing CodexRpc adapter, separate CODEX_HOME, synthetic MCP server and ephemeral protocol threads; zero model turns and no user chat. Test execution took 9.57 seconds, excluding preparation. Initial sandbox attempt failed before initialization with OS error 5 canonicalizing the isolated CODEX_HOME. Repeating outside that sandbox succeeded; it was not a client reconnect failure.

| Check | Actual result |
| --- | --- |
| Before change | Tool `audit_v1`. |
| Notification only | Still `audit_v1` after six seconds; direct resource read returned guide 2. |
| Explicit reload after fixture config change | Tool `audit_v3`, resource guide 3. |
| Close identity-checked fixture child | Resource read `Transport closed`; original marker PID 7536 unchanged. |
| Explicit reload with identical configuration | New child PID 36820; resource guide 1 read successfully. |
| Existing desktop refresh adapter | `unsupported`: configured default control socket absent. No exposed reload tool in this chat's inventory. This does not prove all conceivable supported client channels absent. |
| Live stable baseline, this chat | `get_connection_status` passed: host 1.1.1, broker 1.1.0, clientRefreshRequired=false. This is not a post-reload check of the AP chat. |
| Dev baseline, this chat | `relay_timeout` before any test closure. Separate precondition failure, not evidence of reconnection behavior. |

AP's actual chat was idle when inspected; it was neither modified nor used for a model turn. Only synthetic children were terminated. The actual desktop/backend, live MCP facades and relay hosts were left running.

## Retention and reuse

Raw results, exact copied fixture scripts, stderr and live metadata baseline: `local/apps/agent-relay/qualification/mcp-refresh-2026-11-12/`. `summary.json` contains timestamps, version, explicit test scope, assertions and SHA-256 for the individual artifacts. A portable sanitized projection is retained beside this record as `2026-11-12-codex-refresh-result.json`; it contains no credentials or business payloads. The two original fixture scripts can be rerun only in a new output directory, never over the original proof.

Current behavior and completion rule: application guide `doc/developing/codex-mcp-refresh.md`. Keep raw measurement/version/limits in dated records; consult this index before making a general uncertainty claim. Do not rerun paid work to compensate for a missing lookup.

## Next action and limits

Qualification is complete; actual-desktop automatic refresh remains unsupported through the currently exposed adapter. The rollout is not complete: stable stays 1.1.1 and the previously armed 1.1.2 worker timed out after 900 seconds without replacement. Its diagnostic is update-74ef6a965e6b4a0a under `var/apps/agent-relay/ap-stable/armed-6024e8d01379/`. Do not restart that worker blindly. Resume the existing rollout with this qualified client-boundary constraint; only claim completion after the actual consuming client and full managed installation are verified.
