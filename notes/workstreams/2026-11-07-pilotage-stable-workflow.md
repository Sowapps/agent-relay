# Pilotage AP stable workflow

**Utility:** retain the authorization, checks and handoff for the Pilotage AP stable operator workflow.  
**Usage:** read before resuming this work or planning a separately authorized rollout.

The GO covers preparation, implementation and qualification on a copy, followed by the usual Git delivery. The current Pilotage AP registration, host, projects and sessions are outside this work. No GUI automation, Windows settings change, live notification, tag or release is needed. The branch is `codex/pilotage-stable-workflow`, starting at `dd0cf42`. The objective service rejected a new goal because an unrelated historic goal is still unfinished. There is no measured agent token count.

The operator command uses the portable verifier's exact stable archive and reuses the deployment snapshot, SQLite and host checks, scoped MCP probe and Codex registration writer. The chosen cutover point is the atomic change to one named registration, after readiness. A running old host is refused rather than forcibly stopped. The source is kept intact for recovery. After cutover, retaining new history takes priority over blindly restoring the previous pointer. Existing-chat proof is separate from the fresh MCP probe and carries a challenge tied to this installation.

The fixture reuses the fictional archive-verifier project and credential and a local Codex configuration file. It does not exercise native WSL work or real Codex chat refresh. Before a future rollout obtain deployment GO, select the verified stable archive, schedule an idle window and use the current development guide. Confirm from the owning chat and then notify Pilotage AP when it can resume.

The isolated rehearsal passed with the v1.0.1 archive SHA-256 `b0c480b91647876dfbb0b2378142f50161001cca56d217ea0cc7835d4881df97`. It covered RC refusal, a changed source, candidate shutdown on registration conflict, the successful named switch, project and rights preservation, and stale versus matching simulated chat evidence. The retained receipt is `local/apps/agent-relay/tests/ap-afd0bbae/s/v1.0.1-0c107aad6d704b3c940613db83e538ca/result.json`. Recorded phases were 5.641 s preparation, 2.187 s switch and 0.625 s confirmation, with zero model calls. The shared workspace verifier passed.
