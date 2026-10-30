# Setup and monitoring

**Utility:** provides the human entry point for the current application.  
**Usage:** start/configure the relay, connect an agent through MCP, then follow retained work.

## Start and configure

With the private development toolchain/dependencies already qualified, run `./dev.ps1 run -App agent-relay -Offline` from the workspace root. Startup does not install .NET, WebView2, WSL or native clients. Keep the host running for MCP. Closing the window hides it; Quitter exits the host, while independently owned native sessions remain separate.

For setup, open Réglages → Outils d’administration et de diagnostic. Modifier les projets declares relayed projects, participants, existing workspaces and explicit permissions. Console technique provides the manual/binding surface; its Paramètres → Profils techniques declares distribution, Linux user, native client/executable and configured direct command IDs. Check native account login before authorized model work. These are administrator actions, not permissions a participant can grant itself through MCP.

Select the intended project/principal in Console technique, then Paramètres → Préparer la connexion MCP de ce projet. This creates a scoped connection reference. After a build, `apps/agent-relay/tools/export-mcp.ps1 -DataDirectory <explicit-data-directory>` exports the stable stdio launcher/client fragments for those data. Register the generated fragment using the client’s supported settings and reconnect through its actual supported lifecycle. No global settings, trust or automatic startup are changed implicitly. Do not assume a restart control exists in every client version. An enabled entry confirms registration only: the agent must actually call list_projects.

For the agent-facing workflow read [MCP quickstart](agent-quickstart.md); for session/transfer detail read [sessions and MCP](sessions-and-mcp.md). A participant uses discovered UUIDs and grants, not an inferred workspace or application directory. Existing legacy history is read-only and grants no project authority.

## Find work and errors

General contains the work stream and points to verify. Select a project for its Summary, Tasks, Activity, Messages and Sessions. Histories open on **Tout l’historique**. **Tous les états** controls state, independently of period, participant and text. Additional type/state filters stay in Plus de filtres. List summaries distinguish shown/matching/loaded counts; tab badges describe the loaded project context, with a plus sign if earlier pages remain. They are not a promise that every row matches a chosen filter.

If a list is empty, its message explains whether filters hide records or no records are loaded. Effacer les filtres restores all-history/all-state/all-participant context. Charger plus reads earlier bounded pages; it does not call a model. Sessions use the date of last retained activity when available, never an invented creation date. Sessions without a retained date appear under all-history.

Examiner opens an operation drawer with readable outcome, actual workspace and optional technical detail. Copy preserves the displayed content. Native session inspection, interruption and manual operations are explicit actions; simply navigating is passive. Unknown work is never automatically replayed.

The alert’s cross has the title **Marquer comme examiné. Retrouver cette opération dans le projet → Activité → Tout l’historique (état : Tous les états).** The drawer offers the same action as an explicit button. It removes that exact revision from points to verify; the failed/unknown outcome remains in ordinary history with an examined annotation. The success message offers **Retrouver dans l’historique**. Later, open the project → Activité → Tout l’historique and Tous les états. An evolved operation becomes an alert again; review never approves native work or resolves execution.

## Follow a session discussion

Open project → Sessions → Conversation, or Voir la conversation from a session/operation/message detail. The page shows the session name, participants, workspace, retained requests/results, correlated replies and independent messages in chronological order. Open an event for its full retained operation/message detail. Load earlier exchanges when indicated.

This is Agent Relay history. Direct human/native-client conversation outside the relay is not imported automatically. Unknown context, incomplete pages and read errors remain visible. Native inbox import is an explicit MCP/technical action; the monitoring page does not silently perform it, inspect authentication or prompt agents.

## Updates and boundaries

The development publisher prepares a copied data set, replaces only an exactly identified idle host, retains rollback material and confirms health. It does not grant global trust or reload another client’s MCP connection. Installation directory, data directory and workspaces remain distinct. Automatic startup is opt-in and disabled by default.

Closing a client, hiding the window, cancelling a wait or interrupting an operation does not establish that external effects were undone. Inspect the retained state before a new attempt. See the [file-transfer reference](../developing/file-transfer.md) for actual transport, limits and partial-file handling. Version tags, installers and GitHub releases are separate delivery decisions.
