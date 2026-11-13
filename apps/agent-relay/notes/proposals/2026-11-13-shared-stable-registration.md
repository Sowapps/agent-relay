# Shared stable MCP registration correction

**Utility:** prepares the registration correction separately from the failed Codex client restart path.  
**Usage:** review IR-01 before authorizing implementation. No current registration, stable process, permission or project file is changed by this proposal.

## Target and observed gap

Normal projects select the shared stable Agent Relay installation. Development is exposed only inside this checkout. Connection labels do not create server instances or authority. The current observed global agent_relay entry points to qualification/dev, while agent_relay_personal_assistant points to stable; this checkout also overrides agent_relay with dev. The stable maintenance helper hardcodes the project-specific alias. Retained evidence and the failed live client qualification are in the initial-reconnection workstream.

Correct the routing without changing the existing stable data path, binding, credential, project memberships, grants or session history. A shared server label is not permission to create a new global administrator or grant this binding access to every project. Future project preparation still requires explicit identity/scope checks. Never use a project-specific alias as evidence that the stable host itself is dedicated to an agent.

## IR-01 — Prepare and qualify shared stable registration

- **Subject/type:** correction of registration selection and its maintenance-tool owner; preparation and isolated qualification only.
- **Benefit:** no shared development exposure or misleading per-project stable alias; repeatable maintenance follows the actual shared entry.
- **Effort:** medium, because shared/workspace settings and the maintenance adapter must agree.
- **Risk:** low on copies; medium for later live activation, which is excluded from this preparation.
- **Confidence:** high in the observed routing defect; the live refresh outcome remains unqualified and must not inherit this confidence.
- **Satisfied dependencies:** U0 structural observations and the initial-reconnection protection baseline; existing register-codex update/CAS owner.
- **Authorization update:** implementation and copied-settings qualification approved by the subsequent « GO ! ». Live client activation remains excluded and separately gated because AR-CLIENT-09 is open.
- **Success criterion:** on copied effective settings, global agent_relay resolves to the exact prior stable data/binding, dev exists only as checkout-local agent_relay_dev, unrelated settings and rights remain unchanged, second execution is a no-op, and rollback restores the original settings. The generic maintenance procedure can inspect the shared entry and diagnose conflicting/legacy aliases without silently choosing another principal.

Evaluation uses qualitative effort/risk/confidence with the concrete bases above; no measured duration/token gain is claimed.

## Point plan immediately before implementation

1. Re-read the registration mapper and current private baseline. Make fresh copied settings; reject missing/ambiguous identities or concurrent original edits. Do not initialize another database or require any model.
2. Extend the existing registration owner only as needed for explicit named-entry migration and atomic compare-and-swap. Keep backup, exact intended key changes and a semantic check of unrelated sections. Do not duplicate its TOML rewrite rules in another script.
3. Remove the project-name assumption from the maintenance adapter, using an explicit shared-server selection and reviewed legacy-migration detection. A legacy alias is not an automatic authorization fallback. Preserve current identities and version/data checks.
4. Prepare a coupled shared/workspace change: global agent_relay takes the exact stable registration; the global dev entry and redundant project-specific stable alias disappear; this checkout retains dev under agent_relay_dev. Track each file effect and recover a partial two-file application without overwriting concurrent edits. Copies first; no actual user configuration change in this lot.
5. Run one targeted copied-settings check for normal migration, repeated no-op, conflicting/concurrently changed input, protected semantic equality and paired rollback. Retain proposal/backup/diff/result. No app regression, Codex restart, live switch or model call is justified.

## Activation boundary and relation to U1

A prepared configuration is not an actual reconnection. The previous Restart action reinitialized the local Codex backend and entered an error loop, requiring a full manual client restart. Do not repeat it, rebrand it as server-only, or claim that renaming fixes the client defect. No supported targeted actual-client reconnection is qualified.

After IR-01, present the exact copied-settings result and a live activation plan. Applying settings, proving the consuming client's new identity and passing G0 remain explicit later effects. U1 does not become ready merely because copied configuration tests pass. Do not deploy the staged application release, change startup or remove credentials as part of this correction.

IR-01 outcome: implementation and copied-settings checks are complete; current operation is documented in the registration-migration guide. Known external file edits are refused, but the existing compare-then-replace primitive cannot exclude an uncooperative writer during its final interval. Live activation therefore also requires settings-write coordination; do not report copied-file recovery as a universal concurrent-writer guarantee.
