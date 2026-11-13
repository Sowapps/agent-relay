# Design guide

**Utility:** defines how to assign responsibilities, share knowledge and evolve maintainable applications in this workspace.  
**Usage:** read before designing a capability or changing a rule shared by several components; apply the accompanying [conventions](conventions.md).

## Understand before dividing

For a significant change, establish the target before deriving intermediate migration steps. Use the [workflow's target and point-planning method](workflow.md#authorization-and-design) to challenge it and refine each point immediately before execution. Existing decomposition is evidence to inspect, not a constraint requiring preservation when it prevents the intended behavior.

Start with the user capability, its inputs, effects, failures and authority. Identify the rules that must remain true, their existing owners and the consumers affected by a change. Select a design because it solves those needs, not because a pattern or framework is available. Record significant alternatives and consequences before implementation. A small reversible change needs proportionately little design.

Services own coherent capabilities at their level of abstraction. A session service may create, inspect, reopen and close sessions; single responsibility does not mean one method or primitive operation. A strategy owns a precise variation, such as one publication algorithm. A class should have a coherent reason to change, rather than an arbitrary size limit (evidence `2026-11-05-responsibility-layering`).

## Layers and dependencies

| Level | Owns | Does not own |
| --- | --- | --- |
| Entry boundary | Authentication, parsing, request shape and response adaptation | Business policy copied from use cases |
| Application service | A coherent capability, orchestration, sequencing and transaction boundaries | Platform/client syntax |
| Domain policy or strategy | Business invariant or a precise selected behavior | Transport, UI or global configuration discovery |
| Port | The capability a consumer needs, in its vocabulary | Implementation-specific types |
| Adapter/platform capability | Translation to a real client, environment, storage or external protocol | New authority or business workflow |
| Library/OS | Primitive process, network and file operations | Application decisions |

Dependencies point toward the owning policy/contracts. Composition at the outside chooses concrete collaborators. A capability can use several lower-level services without an intermediary for every call. Introduce a layer when it isolates a real responsibility, reusable capability or variation; no fixed layer count, mandatory entity service, ORM, inheritance hierarchy or DI framework is imposed.

Separate environment connections from agent connectors. WSL/SSH/local concerns and Codex/Claude/other client concerns have different reasons to change. Supporting a new implementation requires actual integration and qualification, not just an interface declaration. Keep project authority independent of both.

## One owner per knowledge

A path layout, format, limit, state rule, permission or algorithm has one authoritative definition. Consumers call that owner or consume a versioned projection generated from it. Do not copy a constant into another component to save a dependency decision (evidence `2026-11-05-single-knowledge-owner`).

Cross-language boundaries may require small language facades around one packaged definition and a shared example corpus. Keep platform-specific validation local: Windows reparse checks and Linux ownership checks are different responsibilities. Do not add a cross-machine call merely to reuse a pure calculation. Fixture values and documentation examples are projections, not competing policy owners.

Repeated validation is appropriate at trust boundaries or different execution times: grants can be revoked between admission and execution. Reuse the underlying policy while preserving those checks. Similar code with different invariants is not automatically one abstraction. Share a capability at the lowest level that owns its meaning; avoid generic Utils containers and speculative frameworks.

## SOLID and separation of concerns

Use SRP to identify cohesive change reasons; OCP to isolate an actual behavioral variation; LSP to preserve port promises, failures and cancellation; ISP to expose what a consumer needs; DIP to prevent business dependencies on external mechanisms. These principles reinforce one another but do not require one interface per class or one service per method.

An adapter can coordinate closely related steps. Extract substantial independently changing behavior, such as message import/correlation versus command dispatch/recovery. Composition chooses collaborators; it must not be scattered through use-case methods. Preserve observable behavior during extraction and avoid unrelated formatting changes.

## Lifecycle, effects and recovery

Design complementary operations together: open/close, acquire/release, stage/publish, enable/disable, migrate/rollback and dispatch/reconcile. Specify shared identity, invariants, effects, durable checkpoints and uncertain outcomes. Acknowledgement is not confirmed completion; interruption is not rollback. Never retry uncertain external work automatically.

Use typed expected failures at boundaries, with stable code and recovery meaning; do not infer authority or execution state from human prose. Retain bounded technical diagnostics separately. Define UTF-8 ownership, serialization and timestamps at process/protocol boundaries rather than relying on machine defaults.

## References that help maintenance

Changing one operation must reveal the non-obvious partners that must be reviewed. Open and close reference each other and the common identity owner; publication references receipt/recovery where their atomicity differs. Explain the shared invariant, not just the file name (evidence `2026-11-05-maintenance-references`).

Use symbol-resolving XML see/seealso in C#, docstring @see in Python/JS, and help links for PowerShell entry points. Prefer stable symbols/owners over line numbers. Ordinary calls and accessors need no repetitive references. References neither replace factorization nor prove behavior; remove or update them when ownership changes.

## Presentation and configuration

Separate retained facts, presentation projections, reusable components and actions. One component owns each display behavior. Prototype UI changes first with fictional data, then promote the same components. Background updates preserve user context; permissions and effects remain server-owned.

Configuration values need an owner, lifetime, validation and explicit override policy. Keep installation, application runtime, developer outputs and user workspaces distinct. A user-selected output is not an internal application prerequisite. Secrets and local operational payloads do not belong in source history.

## Verify the seam, not just the halves

Test the nominal capability and the important refusal/failure that could lose data, bypass rights or conceal uncertainty. When two consumers share a rule, test their agreement using the owner's examples: creation/closure, publication/recovery, catalog/dispatch. Mocks can check policy but do not establish a real process/platform integration. A build does not prove a rendered UI or paid-agent exchange.

Use the cheapest relevant check and broaden only after a failure or material change. Keep native/model/installation authorization explicit. Do not add dozens of implementation-mirroring tests, formatter dependencies or a repository-wide rewrite for a small fix.

## Decisions and continuation

Current behavior and rules live in doc; dated reasoning and rejected alternatives live in notes; operator decisions are stored once in evidence. A significant decision records context, status, selected approach, alternatives, consequences, owning code and what supersedes it. An unresolved candidate never becomes a settled target by being documented.

Before interruption, persist authorization, branch/commit, completed checks, retained result paths, unresolved risks and next action. At resume, inspect these records and actual repository/goal state. Conversation memory is never a prerequisite (evidence `2026-11-05-context-independent-recovery`). Use the [workflow](workflow.md) for that handoff and delivery.
