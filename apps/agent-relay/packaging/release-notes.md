# Agent Relay v1.0.0-rc.2

Documentation refresh; application behavior is unchanged from v1.0.0-rc.1.

- A clearer user README presents supported Windows/WSL use cases, client/agent/relay roles and existing-client remote access, with two self-contained prompts naming the private repository.
- Accompanied setup explains relay projects, installation/data/workspace paths, approvals and the existing agent-client/private-access prerequisites.
- The limits guide collects29boundaries with practical handling and possible future improvements or deliberate tradeoffs.
- Startup text, agent guides and MCP-served documentation are aligned. The current graphical interface is explicitly identified as French.
- The archive now includes the current README with links adapted to the extracted layout, alongside the current starting/limits guides. No new MCP tools, resources, adapters, permissions or dependency versions are introduced.

The Windows x64 ZIP includes .NET. WebView2 remains required for the graphical interface; native WSL work additionally needs the documented Python/WSL/tmux/client prerequisites and account login. The prerelease is unsigned. Nothing is installed globally or registered to start automatically.

Local qualification checks the exact extracted archive, isolated startup, scoped MCP and updated resource contents, project preservation and restart. It does not install prerequisites on a clean machine or repeat paid native-agent tests. This preparation does not authorize publication; the archive and checksum are presented for review first.
