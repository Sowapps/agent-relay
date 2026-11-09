# Stable startup and operating procedures

**Utility:** retains authorization, architecture, proofs and resume state for stable startup and process improvements.  
**Usage:** read before resuming the work or preparing a later stable deployment.

GO authorizes the process improvements, implementation and isolated qualification. Stable deployment and applying the old sign-in entry transfer remain operator-authorized steps. Pilotage AP stays on 1.1.0 during development. The branch is `codex/stable-relay-startup`, starting at `fa10341`. The tracked objective API refused creation because an unrelated historical objective is still unfinished.

Use the existing retained MCP launcher and portable runtime selection. Put cold start in the selected worker, after scoped credential verification and before project admission. One scoped lock coordinates callers, while the existing host lock enforces a single tray owner. Maintenance remains authoritative. Use the established host replacement and recovery procedure to upgrade the portable payload in the same data scope. Windows derives its sign-in preference from that scope, so routine upgrades keep the same preference. A one-time transfer from the old scope is separate.

The first portable RC rehearsal observed one tray owner for two cold clients, passive public discovery, startup refusal during maintenance, revoked binding refusal and a bounded pre-admission failure naming the recovery contact. The retained-client rehearsal switched from 1.1.0 to the RC through the same data and broker, detected the catalog update, and confirmed discovery after relisting. The .NET integration suite passed, including the in-memory preference transfer, disabled state and injected rollback. The workspace verifier passed. No model turns or live Windows settings writes were used. The routine `qualify` operation now includes the cold-start test so the check is part of the normal procedure.
