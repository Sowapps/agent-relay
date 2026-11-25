# Project reactions

In the desktop application, open an existing project, then **Réglages → Réactions aux événements → Gérer les réactions**. A reaction selects any source session or one retained session of that project. Choose a destination, the explicit command environment, the project workspace, the declared command and the participant whose command grant applies. Windows and WSL are independent of the source platform.

The first event is a correlated final reply retained in the project inbox and the end of that exact native turn. The release observer currently verifies Codex WSL transcripts. If the turn cannot be verified within two minutes, Relay records `release_unverified` and allows a linked continuation without running the command. A reply on its own proves neither release nor command execution.

## Modes and results

| Mode | Exit code 2 | Other exits, execution errors or uncertain results |
| --- | --- | --- |
| Async | Recorded as a failed reaction | Recorded independently of other requests |
| Blocking | Refuses an explicitly linked continuation | Recorded. The linked continuation can proceed when the reaction settles |

Code 0 is success. A negative decision applies only to a continuation explicitly linking the source request. The source result and session history are retained. No text is interpreted as a decision. If an interrupted Windows command has no confirmed receipt, Relay retains uncertainty and does not rerun it. For WSL it can recover the existing native receipt by dispatch identity.

## MCP

- `get_project_reactions` returns the current revision, rules and eligible target combinations. `set_project_reactions` replaces the rules at that revision. Both work with existing projects and use the project-scoped `project.reactions.manage` grant. The local administrator can assign that grant without adding command rights. The editor affects future events. A queued reaction whose rule changes before dispatch is settled as reconfigured. A command already admitted retains its result.
- `list_reaction_results` takes the project ID and source request ID. Each result includes its rule, state, diagnostic and confirmed exit code when present. Get command output or the execution error using `get_result` with that reaction's ID if a command was admitted.
- Revision 2 of `send_session_message` and `execute_command` accepts optional `after_request_id`. `reaction_pending` means to inspect the source or reaction and retry the linked continuation when available. `reaction_rejected` means one blocking rule returned 2. Existing callers can keep revision 1 or omit this field.

## Declaring execution targets

The relay administrator declares executable and fixed arguments in the technical configuration `relay-config.json`. A Windows endpoint has adapter ID `windows-command` and its binding ID identifies a `windowsCommands` profile. A WSL endpoint has adapter ID `wsl-command` and its binding ID identifies an existing `profiles` entry with `client: "command"`, distribution and Unix user. In a reaction, `environment` is that explicit profile ID. In both cases the endpoint and workspace belong to the project and the selected actor has the existing command and workspace grants. The reaction editor can select a declared combination and cannot supply a shell string or executable arguments.

A local operator can disable a rule or replace it using the current revision. Updating the installation does not create demo rules, declare commands, or grant operational access to any project. The project should retain its own task, quota and approval rules.
