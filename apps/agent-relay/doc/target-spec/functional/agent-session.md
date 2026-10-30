# Agent Session — Functional Target

**Utility:** states the endpoint, delegation and human-control behavior the relay must provide.  
**Usage:** read before framing or reviewing agent interaction.

Agent Relay connects authorized agent endpoints without assigning their roles from their product or operating system.

**Technical counterpart:** [Agent session technical target](../technical/agent-session.md).

## Endpoint roles

The two ends of an exchange are neutral. Main / Principal, Secondary / Secondaire, reviewer and observer roles are assigned within a relay project. Codex and Claude Code are compatibility objectives for either end; a role
is not bound to Windows or WSL (evidence `2026-10-16-relay-core-and-endpoints-are-neutral`).

Project membership and workspace authorization are owned by the [configuration target](configuration.md#project-membership).

## MVP client profiles

The local operating profile supports a Windows Codex principal communicating with either a Codex or a Claude Code agent in WSL. Both WSL client profiles are required. Product-wide role neutrality is preserved (evidence `2026-10-28-mvp-supports-local-work-and-both-agent-clients`).

## Agent message replies

A message addressed to an authorized WSL agent reaches that agent, and the message it produces in reply returns through the relay to the Windows pilot. Direct command output is not substituted for
an agent-produced reply (evidence `2026-10-28-mvp-separates-wsl-commands-and-agent-replies`).

Command execution independent of an agent is a separate capability owned by the [system I/O target](system-io.md#direct-command-execution).

## Agent-initiated messages

A WSL agent can originate a message addressed to the Windows agent without attaching it as a reply to a Windows request. The relay records its origin and destination and makes it retrievable by
the destination. Message content does not automatically execute a command (evidence `2026-10-28-wsl-can-initiate-messages-but-not-relay-commands`).

The [configuration target](configuration.md#mvp-directional-capabilities) owns the directional command restriction. Message transport does not imply automatic activation of an inactive client chat.

## Durable exchanges

An authorized pilot can delegate a task to an authorized worker. Tasks, messages, states, results and errors remain available across WSL interruption and application restart. Persistence guarantees
are owned by the [system I/O target](system-io.md#durable-storage) (evidence `2026-10-16-relay-runtime-is-manual-and-durable`).

## Human control

The operator can take over the WSL worker manually. Human tmux access is preserved. tmux is an acceptable first communication adapter; it is not a requirement for all endpoint technologies
(evidence `2026-10-16-human-tmux-access-is-preserved`).

## Execution capacity

An authorized principal can name a session, continue it, open another and choose independent parallel work. Configured admission limits and workspace permissions remain authoritative; one conversation cannot silently accept overlapping tasks (evidence `2026-10-29-go-authorizes-v1-correction-plan`).

## External effects

Delegation alone grants no permission to send mail, deploy, push, delete or publish. An action with such effects requires explicit authorization corresponding to that action
(evidence `2026-10-16-external-effects-require-specific-authorization`).


## Retained monitoring and session discussion

A human can navigate from a relayed project to its tasks, activity, messages and sessions, then follow a session’s retained requests, results, replies, independent reports and errors in chronological order. The relay must not invent a complete native transcript or a correlation between unrelated projects/sessions. Unknown context and partial history remain explicit.

History state filters are distinct from period/participant/text filters. Default all-history navigation and clear visible/matching/loaded counts make retained work discoverable. Empty filtered views explain the active restrictions and offer reset. Clickable controls provide perceptible hover and keyboard focus feedback.

Marking an alert as examined changes monitoring attention only: its actual outcome remains retrievable in project activity. The action explains where to find it and offers a direct history path. A later operation revision becomes attention-worthy again. Review never grants native approval or implies resolution, rollback or replay.
