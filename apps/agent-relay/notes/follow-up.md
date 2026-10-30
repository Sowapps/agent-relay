# Project follow-up

**Utility:** tracks dated work and unresolved decisions without changing the product contract.  
**Usage:** read when resuming the investigation or preparing the next authorization.

## 2026-10-28 — Documentation and WSL checks

Latest status: [evening delivery](observations/2026-10-28-evening-mvp-v1.md) supersedes the outstanding-work statements in the older dated sections below. The18-tool version is published; actual current-chat MCP discovery and renewed Claude authentication remain open gates.

The repository was empty, including no Git metadata, when inspected. No repository was initialized. The documentation now separates the agreed target, exact operator evidence, unvalidated
observations and design proposals. No relay, MCP server, hook, installer or client configuration was implemented.

The supplied documentation, evidence and tmux guides were read, along with the toolkit's canonical target-domain registry. The owner organization and selected Linux user are Sowapps and
`sowapps`, respectively. No assumption is made about ownership of unrelated repositories.

The [observation report](observations/2026-10-28-wsl-and-tmux-checks.md) records the environment and two disposable shell probes. The authorized distribution was stopped and restarted; both test tmux
servers are gone. No real agent was launched and no inference turn was requested. Observations await Florent's validation; they have not been written as evidence.

Documentation checks cover relative links, local section anchors, index reachability, file headers, documentation line limits and evidence-reference resolution. The project contains Markdown
files only. External knowledge-base references require WSL access; their target files were read directly during this session.

## Outstanding work

Validate or correct the reported observations. Choose the first pilot/worker pair, disposable Linux workspace and model-usage budget. Review the
[POC proposal](proposals/2026-10-28-feasibility-poc.md) before authorizing its temporary implementation and client integration. Then arbitrate the open
[MVP mechanisms](proposals/2026-10-28-mvp-framing.md#acceptance-evidence-and-remaining-qualifications) against the measured outcome.

Before any future interruption, check its authorization window. The morning's permission is not a recurring test policy. Do not transform the draft task states, transport, database or result
protocol into target requirements without an explicit decision.

## 2026-10-28 — Authorization correction and limited POC

Florent clarified that information and availability never replace an explicit GO. The earlier documentation/tests were started without the required separate GO. The agent working rules now carry
that boundary (evidence `2026-10-28-work-starts-only-after-explicit-go`).

Florent subsequently authorized only the tracked, one-response POC, with no new Claude call during recovery (evidence `2026-10-28-a-go-authorizes-only-the-one-response-poc`). Its
[outcome report](observations/2026-10-28-one-response-poc.md) records working native MCP calls but no recovered response, missing Linux state, an explicit unknown outcome and cleanup. The objective
is not achieved; a new real-client attempt requires a new GO. No MVP work is authorized.

## 2026-10-28 — Model-free storage verification

Florent authorized only storage verification (evidence `2026-10-28-go-is-limited-to-model-free-storage-checks`). The
[storage report](observations/2026-10-28-linux-storage-checks.md) records that the temporary probe disappeared between tool calls, while the persistent temporary and user-directory probes survived
both tool calls and the confirmed distribution restart. All probe directories were removed. No auxiliary model was invoked and no POC configuration was changed.

The goal tool could not create a separate objective while the POC objective remained unfinished; its original counters were not relabeled as consumption for this check. A new actual-agent attempt
still requires a new GO, and the storage measurements remain observations until validated by Florent.

## 2026-10-28 — Corrected POC

Florent authorized a corrected one-response run with a persistent location outside the home project tree and instruction isolation checked before inference
(evidence `2026-10-28-corrected-poc-excludes-the-home-project-tree`). The original inconclusive experiment's objective was closed as a completed experiment with a negative verdict; that closure did
not establish a successful round trip. A new objective was created for this corrected run.

The [corrected POC report](observations/2026-10-28-corrected-poc.md) records successful native MCP calls, one Claude response, matching results after relay/distribution restart, duplicate-dispatch
prevention and cleanup. No further model request occurred during recovery. The new objective was closed complete with 26,714 tokens and 566 seconds reported by its tool. Measurements await
Florent's validation; no MVP development is authorized by this result.

## 2026-10-28 — MVP proposal preparation

Florent authorized preparation only, with questions required wherever product intent is unclear (evidence `2026-10-28-mvp-preparation-requires-explicit-arbitrations`). The
[MVP proposal](proposals/2026-10-28-mvp-framing.md) now separates observations from interactive-client qualification, records pending first-round questions and compares concrete implementation
candidates. No code, test, client configuration or worker invocation is part of this workstream.

Florent selected project-authorized local modifications/commands and both Codex and Claude WSL profiles. Those decisions were recorded together in the functional target
(evidence `2026-10-28-mvp-supports-local-work-and-both-agent-clients`). He then corrected the framing: direct WSL commands and actual agent replies are separate capabilities; broad agent-lifecycle
questions are not prerequisites for this MVP (evidence `2026-10-28-mvp-separates-wsl-commands-and-agent-replies`).

The proposal now has three concrete acceptance cases: a direct listing without a WSL AI agent, a real Codex WSL reply and a real Claude WSL reply. It defines available-session preconditions and
explicit failure/no-replay behavior rather than treating unanswered lifecycle questions as accepted defaults. The technical bundle and workstream sequence are recommendations for review only.

## 2026-10-28 — Four-case qualification

Florent added WSL-originated messages, with no relay command capability for that endpoint, then gave GO for the extended qualification
(evidence `2026-10-28-wsl-can-initiate-messages-but-not-relay-commands`). The target and proposal carry that fourth case and the directional restriction.

The [qualification report](observations/2026-10-28-four-case-qualification.md) records a direct listing without a WSL AI agent, native interactive Codex and Claude replies, an independently addressed
message from each client, negative command-capability tests and durable Windows inbox reads after Linux cleanup. There was one controller prompt per client. The six tracked test processes and the
exact Linux fixture root were cleaned. Production desktop integration and broader telemetry/recovery remain outside these measured results.

The qualification objective was closed complete with 62,246 tokens and 1,302 seconds reported by the goal tool. The application-building workstreams still require a separate explicit GO.


## 2026-10-29 — Interface conformance correction

The scoped GO and local-only WebView2 approval were applied. [Correction observations](observations/2026-10-29-interface-conformance.md) record native conformance, real monitoring, retained failure/copy/drawer checks, complete regression tests, MCP continuity and guarded qualification deployment. Operator visual acceptance remains distinct from these automatic observations. No global runtime install, agent request or normal-data rollout occurred.
