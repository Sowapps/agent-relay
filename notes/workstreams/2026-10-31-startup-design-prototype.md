# Windows startup design and prototype

**Utility:** retains the authorized design, review artifact and checks for the startup preference.  
**Usage:** review the prototype before authorizing application implementation; this record does not authorize startup registration.

## Scope and objective

Florent: « Go concevoir et Maquette ». Objective: design a user-toggleable Windows-login startup preference and update only the isolated HTML prototype. No application implementation, registry write, shared MCP modification, installation, commit or publication.

The native goal creation was attempted and refused because the unrelated historical integration objective remains unfinished/blocked (145775 tokens, 901 seconds). It was not falsely completed. This dated record tracks the scoped objective; a separate task token count is unavailable. Work began approximately 08:44 UTC (09:44 Europe/Paris); completion timing is recorded below.

## Proposed experience

- Settings → Common owns one “Démarrage avec Windows” card. A labeled switch immediately changes the preference; no redundant Save button or confirmation dialog.
- Disabled by default. Status is readable as Activé/Désactivé, independent of color. The switch supports keyboard activation, visible focus and hover feedback.
- Explain that login starts the relay quietly in the notification area, without showing the main window or starting any agent/task. Existing project permissions remain authoritative.
- The preference belongs to the current Windows user and this relay installation/data scope, never to an individual project. Multiple deliberate data scopes may remain distinct; the same scope must not gain duplicate startup entries or tray hosts.
- Disabling affects future logins only and does not quit the running relay. Quitting the relay does not disable future startup. Closing its window retains the existing tray behavior.
- Production should show an in-progress state while saving, disable repeat submission, then re-read the actual registration before confirming success. A failure preserves the last confirmed state with Retry and diagnostic access. If actual state cannot be read, show unavailable/unknown rather than claiming Disabled.

## Implementation proposal, not applied

Existing `StartupRegistration` already owns a per-user Run entry keyed by the data directory, but refuses the portable distribution and otherwise depends on Python. The clean settings renderer currently displays status only. Reuse that ownership boundary rather than adding another independent startup mechanism.

Prefer the existing per-user Run mechanism with a validated persistent native executable/launcher, explicit absolute data path, correct argument quoting and hidden tray startup. Do not depend on a temporary extraction directory, a build-output directory or a content hash that can be removed during updates. Keep startup identity separate from versioned payload identity. Source-development installations need a validated durable launch path as well; do not silently use an unqualified runtime.

A scheduled task adds permissions, lifecycle and maintenance complexity without a current requirement. A Startup-folder shortcut is a feasible alternative but does not remove the durable-path/update problem; retaining the existing Run adapter is the smaller change.

On updates, refresh an enabled registration only after the new runtime is qualified, preserving the scope and rollback behavior. Moving/deleting a portable installation can break any absolute startup path: document disable-before-move/re-enable-after-move and detect a stale registration on manual launch. Windows can independently disable startup; implementation must distinguish its effective state where supported, or explicitly disclose that the UI confirms Agent Relay registration only.

Future implementation checks: enable/disable and read-back; denied write without false success; paths with spaces; stable portable and source launch routes; hidden start; same-scope duplicate prevention; different-scope isolation; upgrade and stale-path behavior. Real Windows-login qualification must be reported separately from simulated browser behavior and must not restart the user's computer without authorization.

## Prototype and verification

Artifact: `local/apps/agent-relay/prototypes/interface-v1/agent-relay-maquette.html`; maintainable owners remain `prototype.js`, `prototype.css`, `scenarios.js`. Baseline copies are under `startup-review/`.

Exterior scenarios: Démarrage désactivé, Démarrage activé and Échec du réglage. The failure scenario arms the next switch action to fail; Retry then succeeds. Data is fictitious, memory-only and reset on refresh. No browser storage or Windows API is involved.

Observed browser checks passed: initial disabled; click enables; Space disables; simulated failure retains disabled and exposes recovery; Retry enables; navigation retains simulated state; reset restores disabled; repeated keyboard activation works with focus restored. No warning/error was returned by the page log check. Compact preset and normal preset were visually inspected (available host width constrained the actual frame to 937 pixels). Screenshots are under `startup-review/`; these are observations, not operator acceptance.

Preview uses an owned temporary HTTP server on loopback port 8768 because the previous port was unavailable from the browser. No automatic startup was installed. File-protocol navigation was refused by browser policy; the ordinary HTTP preview was used instead. Only the prototype tab was controlled.

Status: scoped design/prototype complete; application implementation requires a separate GO.

Completion: 08:51 UTC (09:51 Europe/Paris), approximately seven minutes. Separate goal consumption remains unavailable because goal creation was refused. No quota/reset action was performed. Preview: http://127.0.0.1:8768/agent-relay-maquette.html (temporary owned server). No application or Windows change, commit or publication.

Operator refinement: « Général me semble mieux », followed by « Ok, adapte ». Renamed only the prototype settings tab from Commun to Général and aligned its review instructions. No application change.
