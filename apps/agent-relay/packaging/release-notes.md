# Agent Relay v1.0.0

Windows x64 package for project-scoped agent collaboration between Windows and WSL, with a shared monitoring and diagnostic interface.

- Delegate from a Windows MCP client to supported Codex or Claude agents in WSL; name and reuse sessions or run independent sessions in parallel.
- Receive replies and independent agent reports, run authorized direct commands without a model turn, inspect environments and transfer individual files between authorized workspaces.
- Follow project activity, retained messages, session exchanges, results and errors in the Windows application. Its current interface is in French.
- Start with your usual agent using self-contained prompts, explicit project/workspace/permission guidance and preserved user instructions and approvals.
- Read the included README, starting guides and 29 documented boundaries with practical handling and possible evolution options. Five public MCP resources provide the existing operational documentation.

Application behavior is unchanged from the tested RC2 candidate. This final package updates the version and stable delivery wording; no new tools, adapters, permissions or dependency versions are introduced.

The ZIP includes .NET. WebView2 is required for the graphical interface; native WSL work additionally needs the documented Python/WSL/tmux/client prerequisites and account login. The package is unsigned. No global installation, automatic startup or client settings changes occur implicitly.

Verify SHA256SUMS.txt, extract the complete ZIP to a persistent writable folder and start with README.md or START-HERE.txt. Local qualification covers the extracted executable, isolated startup, scoped MCP, updated documentation, project preservation, restart and internal graphical rendering. It does not claim prerequisite installation on a clean Windows machine or repeat paid native-agent tests.
