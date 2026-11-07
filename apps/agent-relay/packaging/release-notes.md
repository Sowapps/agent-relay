# Agent Relay v1.1.0

Agent Relay connects a Windows MCP client with agents and project workspaces in WSL. This stable release includes the fixes exercised on the 1.1.0 release candidates.

- Codex WSL receives delegated requests through a single bracketed paste and submission. Unconfirmed work stays in the history with a clear recovery status and no automatic replay.
- MCP clients can inspect the active version, contract and catalog status. A retained launcher follows the selected runtime, and a client with an outdated catalog receives a readable recovery action.
- General settings separate project lifecycle and MCP connections. Each project's own view shows its participants, workspaces and permissions. Archival stops new admissions and waits for admitted work to finish.
- The Windows interface keeps open diagnostics stable during updates, presents scoped history and offers an explicit cleanup action for older technical data.

The Windows x64 ZIP includes .NET and the English and French getting-started guides. The interface uses WebView2. Native agent work requires the documented WSL, tmux and signed-in client. Verify the ZIP against SHA256SUMS.txt and follow the portable release guide.
