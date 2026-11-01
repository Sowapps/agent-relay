# Projects and settings

**Utility:** explains general settings, project settings and safe archival without reorganizing the user's agents.  
**Usage:** use the general directory to find/open projects and change their lifecycle; open the project itself for its configuration context.

## Two distinct settings views

The left navigation's Réglages opens Réglages généraux. Général contains installation-wide preferences such as Windows startup and compact presentation. It never selects a project based on the last page visited. Projets lists every project with its current Actif or Archivé status, including archived projects absent from the left navigation.

This directory offers Ouvrir le projet and Archiver/Réactiver. It contains no project configuration form. Open the project, then its Réglages tab, to see its own participants, environment connections, granted workspaces and explicit permissions. The name/breadcrumb and active project navigation identify that scope. Configuration is mainly prepared by an authorized agent through administrative tools; relay roles do not grant rights by themselves. The directory is not a second settings editor.

## Archive and reactivate

Actif means available in normal navigation and eligible for explicitly authorized operations. It does not mean an agent is currently working. Archivé hides that entry from normal navigation and blocks new relay operations while preserving identities, settings, permissions and complete retained history. Use Réglages généraux → Projets → Ouvrir le projet to consult archived work; its banner clearly indicates the archived state. No project files are moved or deleted.

Archiver opens a confirmation. Before committing, the relay locks admission for that project, checks every retained operation/session page and verifies session state through the installed adapter. Ongoing, approval-waiting or unconfirmed work blocks archival, even when older than the visible page. A session must be closed or confirmed not started; an open, uncertain or unverified session blocks the change. Close/interrupt/resolve work separately with appropriate authority. Archival never kills a client, grants approval or replays a request. Failed verification or timeout is not success; refresh the state and inspect the reason. Other projects can continue independently.

Réactiver restores normal visibility and eligibility using the existing permissions. It does not reopen sessions or restart old work. State is retained across relay restarts. The two states are reversible; there is no project deletion or “mis de côté” state.

## Agents and limits

MCP list_projects includes the archived projects authorized for that connection. History reads remain available under existing rights, without contacting native clients for an archived project. New work is refused by the server, regardless of UI/client version. An agent may use set_project_state only with an explicitly granted project.manage capability; it cannot grant that capability to itself. Its expected_state protects against stale decisions. See the [tool contract](mcp-tools.md#active-and-archived-projects) for the exact behavior.

This lock coordinates the selected relay host and its administrative routes. It does not stop unrelated editors, manual terminal input or another relay installation operating the same physical workspace. Do not alter configuration files by hand during active administration. Unsupported adapters or unresolvable old work can prevent archival: preserve the uncertainty rather than hide it. A future explicit reconciliation/administrative disposition workflow or cooperative workspace lease could address such cases without silently reclassifying outcomes or interrupting external activity.
