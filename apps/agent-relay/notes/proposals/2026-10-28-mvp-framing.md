# MVP proposal

**Creation:** 2026-10-28 — Codex.  
**Utility:** defines a reviewable MVP with separate direct-command and agent-message paths.  
**Usage:** read before approving an implementation workstream.

## Preparation scope

This document preparation permits no code, test, installation, client configuration or agent invocation (evidence `2026-10-28-mvp-preparation-requires-explicit-arbitrations`). The implementation
choices below are a concrete recommendation for review, not a claim that the product exists or that implementation is authorized.

## Product scope

The MVP exposes two independent capabilities from Windows: execute an authorized command on WSL without a WSL AI agent, and send a message to a Codex or Claude WSL agent and retrieve its reply.
A directory listing is the first simple command case; it is not a substitute for the message test (evidence `2026-10-28-mvp-separates-wsl-commands-and-agent-replies`).

Both WSL client profiles belong to the MVP. Project permissions may allow local file changes and commands; the product is not limited to read-only agents
(evidence `2026-10-28-mvp-supports-local-work-and-both-agent-clients`). One relay project, two endpoints and one active request are used for the first qualification. The model remains extensible.

The [product target](../../doc/target-spec/README.md) owns the settled requirements. The [glossary](../../doc/developing/glossary.md) fixes French/English terminology. In this document, an agent is
named directly as Codex or Claude rather than asking the operator to reason about an undefined worker lifecycle.

## Four observable acceptance cases

### Direct WSL command

From the Windows pilot, request a directory listing in the authorized Linux workspace. The relay executes the command directly and returns its request identifier, stdout, stderr and execution
outcome. No WSL Codex or Claude process participates in this case. A running agent session is therefore not a prerequisite for direct commands.

The execution response carries the request identifier and a fresh correlation marker, the verified Linux user/workspace, output streams and the program's return code. The outer wsl.exe exit
code is diagnostic information, not the sole proof that the intended command ran. Missing confirmation or a timeout produces an explicit unverified/unknown outcome rather than a false success.

### Codex WSL reply

Send a short message from Windows to the configured Codex WSL session, asking for a short reply containing the request identifier. Codex produces the reply; the relay records it with its origin
and returns it to the Windows pilot. A command echo, a helper's acknowledgment or a fabricated response is not an agent reply.

### Claude WSL reply

Repeat the same message/reply case with the configured Claude WSL session. Qualify this profile separately instead of inferring its support from Codex. Keep the request short and the model-call
budget explicit in the implementation/qualification GO. No such model call is authorized by this preparation.

### WSL-initiated message

The WSL agent calls its message capability to address a new message to the Windows pilot, independently of a reply identifier. Windows retrieves it with its own origin and message identity. The
WSL endpoint exposes no relay command capability; direct and forged command requests are refused. This fourth case is an operator requirement
(evidence `2026-10-28-wsl-can-initiate-messages-but-not-relay-commands`). It does not claim automatic activation of an inactive Windows conversation.

## Recommended technical construction

### Application and core

Use a Windows user-session application with a tray icon and a small status/configuration/result window, opened manually by default. Preserve the agreed opt-in Windows sign-in launch capability.
The proposed host is C#/.NET 10 with Windows Forms; the business core and application ports are platform-neutral assemblies. There is no separate Windows service.

.NET 10 is an LTS version in the [official support policy](https://dotnet.microsoft.com/en-us/platform/support/policy), and
[NotifyIcon](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/notifyicon-component-overview-windows-forms) provides the tray integration. This stack recommendation replaces no product
requirement and does not authorize installing its SDK. The POC supplies observed contracts and fixtures, not a production codebase to copy unchanged.

### MCP and local interface

Expose separate proposed tools for command execution, agent messaging and result retrieval. A client-launched stdio shim forwards them through an account-restricted Windows named pipe to the tray
application, which owns the durable store. The shim can be another mode of the same executable; it must not own a second relay instance or start the tray implicitly.

Named-pipe access requires an explicit restricted descriptor, not the default ACL; the
[Windows security reference](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights) describes the mechanism. Endpoint/project capabilities remain necessary in
addition to the account boundary. No TCP listener or browser is required by this proposed first implementation.

### Durable data and WSL adapter

Use one local Windows SQLite database owned by the application, plus a durable Linux result area outside the workspace. Keep configuration and results in the data directory, application files in
the installation directory, and working files in the workspace. Keep credentials separately protected. Never use a temporary directory for data required across restarts.

A Windows-to-WSL adapter executes direct commands and communicates with the selected agent profile through a dedicated tmux socket/session. The Linux helper can use Python 3 without adding an
agent-runtime dependency to the core. Direct command execution does not consume a WSL model turn. Socket, workspace and durable data locations are distinct configured resources.

For qualification, use a dedicated persistent location outside the home project tree. The existing exclusion of `/home/sowapps/projects` remains in force until explicitly changed. Before an agent
call, verify instruction ancestry and the selected client's isolation controls. Real-project instruction loading is a later project-configuration choice, not inferred from the isolated test.

## Defined operating boundary

The three acceptance cases assume WSL and the selected agent session are available for that case. The MVP joins the explicitly configured agent session. A missing session or unavailable WSL
produces an unavailable result; the relay does not secretly start another client, pick the newest conversation or replace Codex with Claude. Automatic start/stop/restart policy is outside this
first exchange qualification; it remains part of the wider product target.

The selected agent is not owned by the relay's lifecycle. Closing the relay does not introduce a feature that kills or manages that independently launched session. The relay closes cleanly and
retains recorded exchanges. Agent exit-policy questions do not block this MVP's definition.

A submitted request is recorded before its acknowledgment. One active request is allowed; an additional active submission returns busy rather than silently creating an execution queue. Repeating
an identifier returns the existing record instead of executing again. Command output and an agent reply retain different types and origins.

A completed result remains readable after relay and WSL restart. If execution cannot be established after a cutoff, the record says outcome unknown; no automatic replay is permitted. There is no
promise of exactly-once execution, recovery of an unsaved response or automatic resumption of an inactive Windows chat. Result retrieval uses the client's normal tool calls.

Project routing, workspace selection and permitted programs/arguments are explicit configuration. Direct commands carry a program and arguments, not an unrestricted shell string. Client tool
permissions apply to agent work. The relay does not claim that a working directory or same-account credential alone confines arbitrary code against all host files. External and destructive effects
still require their corresponding authorization; the directory-listing acceptance case grants none.

Human tmux access remains available, and automated message injection has one controlled path. The MVP does not claim concurrent human/automatic control has been qualified. No automatic purge of
messages/results, cloud synchronization, unattended recovery or multi-agent scheduling is included.

## Proposed implementation workstreams

Each workstream needs its own explicit GO, with its model-call and environment-change scope. None is started by this document.

1. Qualify direct command execution and the two actual tmux client profiles on dedicated fixtures. Establish receipt, reply correlation, permissions and an explicit unverified outcome. Do not
   substitute print-mode success for interactive-session qualification; stop and report if a profile cannot satisfy the contract.
2. Implement the neutral core, project capabilities and durable store. Verify request identifiers, denied routes, single-active-request behavior and result/uncertainty persistence with deterministic
   fixtures before spending model quota.
3. Implement the stdio/pipe connection and the two distinct WSL paths. Verify a direct listing without a WSL agent, then real Codex and Claude replies under the qualified profiles.
4. Add the Windows tray, minimal configuration/results surface and lifecycle integration. Verify manual opening, opt-in sign-in launch and clean closure without adding agent orchestration.
5. Run the four acceptance cases, completed-result restart checks, explicit failure cases and cleanup. Record actual consumption and remaining limits before declaring this MVP delivered.

## Acceptance evidence and remaining qualifications

The [corrected POC](../observations/2026-10-28-corrected-poc.md) observed one Claude print-mode reply and recovery of a completed result. It did not qualify direct-command tooling, both interactive
client profiles, the tray, project permissions or interruptions during execution. Those checks are named gates above, not hidden claims of readiness.

For each case, retain request identity, configured origin/destination/workspace, tool events, observed output, permissions and final state. A successful direct listing proves only that configured
command case. A short message reply proves only its qualified client/profile. Normal completed-result recovery does not prove safe re-execution of an interrupted command.

Florent's selected capabilities are recorded in the target. The technical bundle and workstreams in this document remain a proposal for review. No further operator questionnaire about agent
lifecycle is required to understand or approve the four concrete acceptance cases.
