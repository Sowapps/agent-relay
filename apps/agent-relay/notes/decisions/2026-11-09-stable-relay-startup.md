# Shared stable relay startup

**Utility:** records ownership and sequencing for on-demand startup and retained stable upgrades.  
**Usage:** review before changing host readiness, the MCP route or the sign-in preference.

Project agents use a shared stable installation. Development uses a separate test scope. The selected worker owns cold startup because the retained MCP launcher can outlive the worker version. It verifies the local participant credential before launching, waits for a read-only authenticated handshake, then forwards the original operation once. A scope lock coordinates startup, and the existing host lock owns tray uniqueness. Deployment and storage maintenance markers take priority over automatic startup.

Routine updates reuse the existing deployment transaction with the stable data directory and retained launcher. The Windows sign-in entry already derives its name from the data directory, so it follows the same runtime pointer without a registry rewrite. Moving the data for every update would break that preference and could strand a loaded Codex route.

A one-time change of data scope is a separate authorized startup preference transfer. It uses the same Windows preference implementation as the settings UI. The earlier full storage migration disables the source launch while it moves owned storage and keeps its own raw rollback receipt. The startup-only transfer has no storage move and verifies the target before disabling the source.

The [operator procedure](../../doc/developing/development.md#pilotage-ap-stable-operation) owns execution steps. The [open issues register](../../../../notes/open-issues.md) tracks the remaining rollout checks.
