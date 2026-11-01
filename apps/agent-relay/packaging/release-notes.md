# Agent Relay v1.0.1

Windows x64 package for project-scoped agent collaboration between Windows and WSL, with a shared monitoring and diagnostic interface.

- Delegate from a Windows MCP client to supported Codex or Claude agents in WSL; name and reuse sessions or run independent sessions in parallel.
- Receive replies and independent agent reports, run authorized direct commands without a model turn, inspect environments and transfer individual files between authorized workspaces.
- Follow project activity, retained messages, session exchanges, results and errors in the Windows application. Its current interface is in French.
- Start with your usual agent using self-contained prompts, explicit project/workspace/permission guidance and preserved user instructions and approvals.
- Enable or disable optional Windows sign-in startup in Réglages → Général. The relay starts quietly in the notification area without starting agents/tasks; the setting is off by default and errors remain readable/retryable.
- Start in English or French with the included README.md / README.fr.md, current guides and documented boundaries. Five public MCP resources provide operational documentation.

This maintenance release adds the reviewed startup preference and its retained native launcher, updates the startup guides and includes the French starting README. Existing projects, permissions and MCP tools remain unchanged. Dependency versions are unchanged.

The ZIP includes .NET. WebView2 is required for the graphical interface; native WSL work additionally needs the documented Python/WSL/tmux/client prerequisites and account login. The package is unsigned. No global installation, automatic startup or client settings changes occur implicitly.

Verify SHA256SUMS.txt, extract the complete ZIP to a persistent writable folder and start with README.md or START-HERE.txt. Local qualification covers the extracted executable, isolated startup, scoped MCP, updated documentation, project preservation, restart and internal graphical rendering. It does not claim prerequisite installation on a clean Windows machine or repeat paid native-agent tests.
