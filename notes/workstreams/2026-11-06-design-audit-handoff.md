# Agent Relay design audit handoff

**Utility:** records the disposition, rationale and checks for the eleven audit points.  
**Usage:** start here when resuming this audit or preparing a separately authorized qualification of its merged changes.

## Scope and disposition

The 2026-11-06 GO covers A11 and the final review of A01–A11. A11 closes the handoff and links the design decision register to the workstream records. This lot contains documentation only. Deployment is excluded. The objective service could not start a new objective because an unrelated unfinished objective already occupied its slot. This record is the fallback checkpoint.

| Point | Decision and scope | Verification record |
| --- | --- | --- |
| A01 | Make the design guide operational. Choose responsibility levels before introducing services, strategies or adapters. | [Foundations](2026-11-05-design-foundations.md) |
| A02 | Share the native layout definition across languages and retain explicit compatibility for earlier layouts. | [Foundations](2026-11-05-design-foundations.md) |
| A03 | Compose environment and agent adapters separately so environment transport and agent behavior can evolve independently. | [Foundations](2026-11-05-design-foundations.md) |
| A04 | Cross-reference complementary lifecycle, dispatch and recovery operations where the relationship carries an invariant. This was a focused pass. | [Foundations](2026-11-05-design-foundations.md) |
| A05 | Centralize session name rules and MCP registration, schema and dispatch. Preserve the existing wire contract for this refactor. | [Tool registry](2026-11-05-mcp-tool-registry.md) |
| A06 | Distinguish typed outcomes at the MCP boundary, preserve the identity of uncertain work and handle UTF-8 explicitly. The qualification covered refusal before execution and an unconfirmed reply without replay. | [MCP outcomes and publication](2026-11-06-mcp-errors-publication.md) |
| A07 | Use an explicitly targeted publication plan with repository and visibility checks. Qualify the plan locally for allowed and refused cases. | [MCP outcomes and publication](2026-11-06-mcp-errors-publication.md) |
| A08 | Reuse the shared native layout fixtures and retain the model-free lifecycle check. The record separates the earlier WSL lifecycle observation from the current fixture checks. | [A08–A10](2026-11-06-audit-a08-a10.md) |
| A09 | Establish language conventions and fix the concrete pipe client discrepancy. Apply the conventions incrementally as code is touched. | [A08–A10](2026-11-06-audit-a08-a10.md) |
| A10 | Share exact process ownership checks across host migration and deployment. Qualify Windows argument parsing against a controlled process inventory. | [A08–A10](2026-11-06-audit-a08-a10.md) |
| A11 | Keep decisions and their reasons discoverable in Git. Record the source and installed checkpoints separately so a new agent can resume safely. | This handoff and the [design decision register](../../apps/agent-relay/notes/decisions/README.md) |

## Resume from Git

1. Read the [workspace rules](../../AGENTS.md), the [application rules](../../apps/agent-relay/AGENTS.md) and the relevant verification record above. The current [design guide](../../doc/developing/design-guide.md) owns the design rules.
2. Use `main` for source. The A01–A10 delivery checkpoints are `b07c929` (foundations), `f892897` (A05), `9abe57c` (A06/A07) and `054524e` (A08–A10). At handoff the local qualification instance reported `1.1.0-rc.4`. A07 changed the publication tooling and A08–A10 are present in Git beyond that installed checkpoint. The local version alone therefore does not identify the latest audited source.
3. No implementation remains in the agreed audit scope. For a subsequent candidate, obtain a separate GO and qualify the exact package in isolation with a new MCP connection. Check discovery, one structured error and one model-free session lifecycle. Decide on deployment and publication separately.
4. If handling the prior Pilotage AP incident, use its [incident record](2026-11-05-ap-missing-reply.md). Treat the original uncertain request as non-replayable and verify the version of any native helper already copied into an existing session.

## Handoff verification

Verified from an isolated Git source export. The documentation checks passed for links, anchors, indexes and evidence references. The table contains each audit point exactly once and all four delivery checkpoints are ancestors of the audited source. Earlier runtime observations are recorded in their respective workstreams.
