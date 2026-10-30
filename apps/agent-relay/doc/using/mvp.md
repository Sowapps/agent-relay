# Setup and monitoring

**Utility:** provides the human entry point for the current application.  
**Usage:** use this functional reference for monitoring and intervention; your usual agent can handle the authorized setup.

## Start and configure

For first use, the [accompanied-start guide](getting-started.md) explains what to ask your agent and how project/installation/data/workspace paths relate. With a downloaded archive, use its Initialize-Relay.cmd and Start-AgentRelay.cmd; Windows startup/direct MCP does not require a development SDK. For an explicitly selected source checkout, run `./dev.ps1 run -App agent-relay -Offline` only after qualifying its development toolchain. Startup never installs prerequisites implicitly. Keep the host running for MCP. Closing the window hides it; Quitter exits the host, while native sessions have their own lifecycle.

For setup, open Réglages → Outils d’administration et de diagnostic. Modifier les projets declares relayed projects, participants, existing workspaces and explicit permissions. Console technique provides the manual/binding surface; its Paramètres → Profils techniques declares distribution, Linux user, native client/executable and configured direct command IDs. Check native account login before authorized model work. These are administrator actions, not permissions a participant can grant itself through MCP.

Select the intended project/principal in Console technique, then Paramètres → Préparer la connexion MCP de ce projet. This creates a scoped connection reference. For the portable archive, rerun its initializer with the selected data scope to export the direct executable fragments. For a built source checkout, `apps/agent-relay/tools/export-mcp.ps1 -DataDirectory <explicit-data-directory>` exports the development launcher/client fragments. Register using the client's supported settings/lifecycle, without implicit global trust or startup changes. An enabled entry confirms registration only: the agent must actually call list_projects.

For the agent-facing workflow read [MCP quickstart](agent-quickstart.md); for session/transfer detail read [sessions and MCP](sessions-and-mcp.md). A participant uses discovered UUIDs and grants, not an inferred workspace or application directory. Existing legacy history is read-only and grants no project authority.

## Find work and errors

The current interface is in French; the labels below identify its actual controls. Vue générale means Overview, Synthèse means Summary, Tâches means Tasks and Activité means Activity.

General contains the work stream and points to verify. Select a project for its Summary, Tasks, Activity, Messages and Sessions. Histories open on **Tout l’historique**. **Tous les états** controls state, independently of period, participant and text. Additional type/state filters stay in Plus de filtres. List summaries distinguish shown/matching/loaded counts; tab badges describe the loaded project context, with a plus sign if earlier pages remain. They are not a promise that every row matches a chosen filter.

If a list is empty, its message explains whether filters hide records or no records are loaded. Effacer les filtres restores all-history/all-state/all-participant context. Charger plus reads earlier bounded pages; it does not call a model. Sessions use the date of last retained activity when available, never an invented creation date. Sessions without a retained date appear under all-history.

Examiner opens an operation drawer with readable outcome, actual workspace and optional technical detail. Copy preserves the displayed content. Native session inspection, interruption and manual operations are explicit actions; simply navigating is passive. Unknown work is never automatically replayed.

The alert’s cross has the title **Marquer comme examiné. Retrouver cette opération dans le projet → Activité → Tout l’historique (état : Tous les états).** The drawer offers the same action as an explicit button. It removes that exact revision from points to verify; the failed/unknown outcome remains in ordinary history with an examined annotation. The success message offers **Retrouver dans l’historique**. Later, open the project → Activité → Tout l’historique and Tous les états. An evolved operation becomes an alert again; review never approves native work or resolves execution.

## Follow a session discussion

Open project → Sessions → Conversation, or Voir la conversation from a session/operation/message detail. The page shows the session name, participants, workspace, retained requests/results, correlated replies and independent messages in chronological order. Open an event for its full retained operation/message detail. Load earlier exchanges when indicated.

This is Agent Relay history. Direct human/native-client conversation outside the relay is not imported automatically. Unknown context, incomplete pages and read errors remain visible. Native inbox import is an explicit MCP/technical action; the monitoring page does not silently perform it, inspect authentication or prompt agents.

## Updates and boundaries

Installation, data and workspaces remain distinct. Portable updates retain an explicitly selected backed-up data scope; the development publisher has a separate guarded route. Neither grants global trust nor reloads another client's connection. Startup behavior depends on the distribution and remains a deliberate choice.

Closing a client, hiding the window, cancelling a wait or interrupting work does not establish that external effects were undone. Inspect the retained state before a new attempt. The [limits register](limits.md) collects current boundaries, practical handling and future options; publication/version decisions require their own authority.
