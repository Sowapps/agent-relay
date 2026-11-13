# U1 stable MCP contract and compatibility

**Utility:** retains the exact point plan and checkpoint for the authorized development contract lot.  
**Usage:** resume here together with the conditional U1–U5 delegation; no live configuration or stable deployment is authorized.

## Authorization and start

The operator delegated progression through U5 after ten minutes without steering. The recorded initial deadline 13/11/2026 13:33:11 Europe/Paris elapsed without new input. U1 started at 13:33:34 Europe/Paris on codex/stable-mcp-contract from main ff3a612, preserving the delegation/index record. The scheduling revision permits development independently of initial-client activation; AR-CLIENT-09/AR-CFG-10 remain mandatory activation/deployment gates. No real agent/model/WSL call, client restart, settings edit, global install, release/tag or stable effect.

The overall rollout goal still reports blocked with historical 66,276 tokens / 1,081 seconds. No false completion/reactivation is claimed. Available account quota at start: 25% used in the weekly Codex window. This is a shared-account percentage, not exact task tokens; do not attribute other chats' usage to this lot. Private raw snapshot: local/apps/agent-relay/qualification/autonomous-u1-2026-11-13/usage-before.json. No reset credit is used.

## Point plan before implementation

1. Preserve the existing McpToolCatalog as the single definition/validation/routing owner, exposing versioned operation descriptors rather than introducing a second dispatch table. Typed aliases remain compatible routes. Review exact Core/ProjectCoordinator policy seams before claiming filtered discovery or revoked-grant behavior.
2. Define the small fixed generic MCP envelope and separate transport schema identity, operation revision/schema identity, package/build identity and documentation digest. New operations are invoked through the already-loaded generic surface; unknown operation/revision and malformed arguments refuse before effects. No arbitrary shell/reflection dispatch or caller-created authority.
3. Reuse current argument validation and ProjectAction translations. Plan how legacy named calls resolve current definitions, and how generic calls select an explicit supported revision. Capability discovery is not an authorization grant; the host must check current binding/project/session/workspace policy on every admission.
4. Define transport compatibility manifest/version and migration expectations for U2/U3, without implementing cooperative C# stream handover or replacing the updater in U1. Do not retain application assemblies in a claimed minimal transport. Bump development product metadata to an appropriate numbered RC before changing any published MCP contract; no version/tag publication.
5. Qualify nominal existing/new operation resolution, stale/unknown revision, malformed payload, stable transport schema despite operation-registry changes, and actual revoked-project policy through useful existing fixtures. Keep schema/policy validation owned once. Build/test only the changed C# seam, not native model scenarios.

Immediate checkpoint: current catalog has 26 typed tools, one schema/route registration owner and private recursive validation. McpIdentity currently hashes tools + DTOs + documentation into a single ContractHash, and McpFacade rejects stale full catalog hashes before operational routes. McpBroker already replaces its worker but caches that full digest; this behavior will be refactored in U2, not solved by another reload test.

Next: finish the bounded source review of discovery/dispatch policy and write the concrete contract choices before editing. Persist decisions, checked artifacts and exit/deadline at lot completion; then wait the authorized ten minutes before U2 unless new steering arrives.

## Concrete contract choice before code

Use a small AgentRelay.Protocol C# library with only framework JSON/crypto dependencies for stable envelope schema/validation and wire limits; U2's transport can reference it without application/SQLite assemblies. Infrastructure keeps the single operation declaration/ProjectAction translation owner and consumes this library. Introduce a versioned registry resolver around those declarations; legacy named tools select their latest supported revision while generic invocations select an explicit ID/revision. Do not copy routes or permission policy into protocol/transport.

Fixed generic surface: get_connection_status, discover_operations, invoke_operation and the existing retained get_result shape. Generic arguments are an opaque JSON object at the wire boundary and MUST pass the selected operation's own existing schema before route construction. Unknown operation/revision and extra malformed fields refuse with no effects. Result access still calls the existing project policy; arbitrary actor IDs/shell dispatch are never accepted.

Operation names/input schemas are public protocol metadata, just like current tools/list; real project contexts/rights remain filtered through Application.Discover and admission still revalidates policy. Do not introduce a second coarse capability-to-operation authorization table merely to decorate discovery as allowed. U1 supplies contracts/registry; actual host discovery and generic entry wiring belongs to U2's facade/transport integration, and no claim of live scoped generic discovery is made yet.

Compatibility metadata separates wire protocol/schema, operation registry and documentation hashes while retaining the existing legacy full ContractHash/generation. Plan development version 1.2.0-rc.1 for these additive contract changes; stable 1.1.1 and the staged 1.1.2 archive remain untouched. No public tool cache/reconnect is assumed. The C# handover/update pipeline is explicitly deferred to U2/U3.

## Implemented and qualified

AgentRelay.Protocol is a framework-only C# library: stable four-tool envelope, bounded schema validation, canonical schema identity and revision-history checks. Infrastructure retains the single declaration/route registry and current use-case authorization. There are 26 legacy tool declarations, of which 25 translate to project operations; the connection-status tool is not a project action. No duplicated dispatch/policy table was introduced. CallId is explicitly correlation only, distinct from retained business RequestId. Typed legacy validation now calls the same validator. Program exposes read-only mcp-contract and mcp-operations exports before initialization. The development product is 1.2.0-rc.1; nothing was tagged or installed.

Normal locked offline build passed with zero warnings/errors (7.94 seconds); a final build after canonical-hash correction passed. Normal and win-x64 self-contained lock refreshes used the existing private SDK/cache and introduced only the new project edge, no third-party dependency or package-version change. Protocol assembly references were inspected: no application/SQLite dependency. Metadata exports against an absent private data path created no directory/database and their operation hash agreed.

Retained runs:

- local/apps/agent-relay/tests/u1-contract-aff4e51417da4eae88a66a324e7e39e2: initial contract and existing legacy-contract checks PASS.
- local/apps/agent-relay/tests/u1-final-966500add7464bf1ae9604006a10abeb: locked build, contract and legacy checks PASS; identity/operations JSON exports.
- local/apps/agent-relay/tests/u1-canonical-c9e23a11adbe44c58f2c32660c99bef4: final contract PASS and exports. Checks cover unchanged wire/new operation, old/new revision routes, schema whitespace/order invariance, malformed/unknown/stale revision refusal before routing, forged/reused revision rejection and actual Core permission revocation with one in-memory inspector call. No native/model calls.

Final exported transport protocol 1, wire hash ea7504522c9d20f4f198a985990d5751a956464eac0704c9630b7560e5437949; registry hash 7fced511325c847089eda4311731335527116d978cc2c1fdf56de48aeedc13e6. These are observations, not operator-validated evidence. Canonicalization sorts object names, preserves array order/numeric forms, and is deliberately not a general JSON Schema/canonicalization claim.

Remaining boundaries: generic facade/transport integration U2; persistent handover and update transaction U2/U3; live registration/client activation still gated by AR-CLIENT-09/AR-CFG-10. A declarative protocol is not proof that installed clients expose it. Newly found release Lock ignores Offline; tracked AR-TOOLS-11 for the U4 CLI owner. Existing isolated toolchain invocation provided an offline workaround, not a second lock-policy implementation.

Account observation at 14:01:17 Europe/Paris: 26% weekly used, versus 25% at start. Difference is one percentage point of a shared-account window, not exact per-lot tokens. Historical goal counters remain unchanged and are not attributed to U1. Duration to that checkpoint: approximately 28 minutes. No reset credit used.

Next: commit/push U1 and integrate main under standing source-delivery authorization, without deployment. Record completion/deadline in the delegation record; after ten minutes without steering refine U2's point plan against actual stream/process ownership before coding.
