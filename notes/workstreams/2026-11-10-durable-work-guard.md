# Durable work guard

**Utility:** records the C# maintenance guard scope and isolated qualification.  
**Usage:** consult before changing the idle check or its Python callers.

GO covers the common check, migration and deployment on copies. The branch starts from `0180430`. Stable installation and WSL sessions remain outside the scope. The goal API still refuses a new objective under WS-GOAL-01.

The C# work store owns the durable active predicate used by the read-only maintenance command. Migration and deployment use the prepared runtime to call it. An initial deployment can explicitly allow a missing database. An established source, a corrupt database and a live host's admission barrier keep their own safety checks. The earlier stable rollout tool also consumes the shared deployment helper, so its call signature follows the same guard.

## Qualification

- Offline application suite and workspace verification passed. The C# build reported zero warnings and zero errors.
- Storage migration on a synthetic project rejected `pending`, kept the owner running after refusal, recovered from injected failure, and preserved history and permissions on the successful move. Local receipt `local/apps/agent-relay/qualification/storage-df33be0c13354a22906276678c568386/result.json` (SHA-256 `45955E74864987A381AFD6404C2CB4CA0865720B2FEB79DD8BE1B12DC2A6FB59`).
- Deployment on a fictional configured scope rejected `running`, a late request, and a damaged or required missing database. The scoped MCP still worked after rollback and after a successful update. The update took 5.890 seconds, and the deliberately failed update with recovery took 8.078 seconds. Local receipt `local/apps/agent-relay/tests/deployment-600f3b8b0c4a4ef490ec9914cef38c6c/result.json` (SHA-256 `8D7DCE6E6A18F285EF8050C1E788CFC09880BF1101682F1ADC1433AA40351AB6`).
- The existing cutover caller passed on a fictional project copied from a historical verification archive with the current guard. Local receipt `local/apps/agent-relay/tests/ap-b8401a25/s/v1.1.0-d2ab67a5030643babfe5b6930d4afdbe/result.json` (SHA-256 `A6E67644AAAA2BC6CEEBCC45051D514B8C0F8A3332D3A8AA9083F315A690F9CF`). No model or WSL calls were needed for these rehearsals.

The WSL session adapter synchronization workflow has its own query. AR-MAINT-06 tracks choosing its guard runtime for earlier scopes and exercising the change on a fictional WSL target.
