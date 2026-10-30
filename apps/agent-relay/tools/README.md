# Application commands

**Utility:** describes reusable application commands and bounded verification tools.  
**Usage:** run from the checkout root with the documented explicit data scope; commands do not grant authorization.

`app.ps1` is the application's command driver selected by the shared workspace registry.

`release.ps1 -Action Lock|Package|Verify|Tag|Publish` owns the versioned portable release workflow. Package chooses a fresh artifact directory; Verify/Tag/Publish take that exact Directory. See [release workflow](../doc/developing/releases.md). External publication requires explicit authorization; Tag/Publish refuse dirty or changed source, unmatched checks and existing divergent tags/releases.
It provides read-only diagnostics and invokes the shared application build/test/run composition in AppCommands.psm1.
RuntimeStaging.psm1 verifies and reuses content-addressed executable copies so active windows do not lock compiler outputs.
relay.ps1 sends/reads through the same client used by the Windows UI; application-smoke.py tests real tray/client processes.
See [shared commands](../../../doc/operating/entrypoints.md).

`start-interface-check.ps1 [-Name native-interface] [-Restart]` starts only explicitly owned synthetic monitoring qualification data after a build. Restart verifies exact executable/assembly/data identity. It does not start a native agent or enable automatic startup.
`render-interface-check.ps1 [-Name native-interface] [-DataDirectory <existing-relay-data>]` reads the running relay and renders the production embedded presentation to persistent images/checks. No desktop capture or computer input occurs. The normal UI contains no simulation tools. See [Windows interface](../doc/developing/windows-interface.md).

`check-interface-conformance.ps1` prepares the immutable-reference fixtures, builds offline and captures 15 production C# views at two client sizes. `prepare-conformance.py` preserves the reviewed prototype hash and keeps fake data outside production assets. `webview-qualification/` retains the isolated runtime prerequisite check.

The test entry point `AgentRelay.Tests.dll monitor-review <empty-data-directory> <capture-directory>` seeds only explicit synthetic data, tests the actual acknowledgement bridge/store and reopens the window to verify durable review with unchanged journal history. It never invokes an agent or WSL.

`generate-icon.py` regenerates the multi-size ICO from its SVG source with Pillow; normal builds consume the committed ICO. See [Windows interface](../doc/developing/windows-interface.md).

`export-mcp.ps1 -CodexProject` refreshes the managed project-local connection after a build, backs up its previous fragment and refuses unrelated settings; it never changes global trust.
`codex-mcp-check.py` inspects native Windows app-server configuration and tool discovery without creating a conversation or starting a model turn.
Use `--qualification-home` for a private trust fixture and `--runtime-fragment` for process-only server overrides. Reports explicitly identify those overrides; they do not establish the desktop conversation's live connection.

`python -B apps/agent-relay/tools/check-poc-layout.py`, from repository root, checks Python syntax and migrated paths without launching a native process, WSL or an agent.


`AgentRelay.Tests.dll monitor-usability <empty-data-directory> <capture-directory>` tests actual passive session pagination behind other-session activity and all project history contexts. It seeds owned synthetic data, never agents or WSL.

`check-mcp-docs.py --dotnet <private-dotnet> --assembly <built-Windows-dll> --output <owned-report-directory>` creates a fresh documentation-only stdio client, reads the packaged guides, validates the24tool inventory and template arguments against live schemas, and refuses unknown resource paths. Native Linux message documentation is AST/schema-checked only; the script does not bypass its Linux root guard or invoke WSL. Templates are never sent as operational calls.


`check-bootstrap-docs.ps1 [-Name <fresh-qualification-name>]` follows the canonical bootstrap example on new private data: additive project creation, a native-binding schema example, a scoped connection, guarded publication, own-control UI rendering, fresh MCP discovery/read/docs and foreign-project refusal. It validates and stops only its exact recorded test host. Existing live data, WSL/model clients and global settings are untouched. Requires an already qualified/built toolchain; this check does not pretend to install prerequisites on a clean machine. `check-bootstrap-mcp.py` is its documentation-only scoped reader.
