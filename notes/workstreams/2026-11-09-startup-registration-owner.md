# Windows startup entry ownership

**Utility:** records the authorized removal of duplicate startup registry logic and the migration checks.  
**Usage:** consult before changing the startup entry owner or the storage rollback procedure.

GO covers development, isolated tests and documentation. Branch `codex/startup-registration-owner` starts from `f6e93a6`. The goal API still refuses a new objective under WS-GOAL-01. The relevant inspection found one Python registry owner, in `migrate-storage.py`, and one test caller. Other startup tooling already invokes C# commands. The multi-stage migration still needs its orchestration across owned Windows data, WSL fixtures and Codex configuration.

The prepared C# executable now owns the startup entry read, guarded write and exact rollback value/type. Python calls the C# command at the existing transaction boundaries. The same retained-launcher identity is checked before any entry is suspended or restored. Unexpected registry entries require operator review. Rollback preserves a concurrently changed entry rather than forcing the saved value over it.

The native storage qualification passed on a fictional scope with startup disabled. A second path simulated the startup entries and injected a failure after enabling the destination and retiring the source files. Rollback restored the source files, the exact entry, and the previous Codex configuration. The C# integration test separately exercised a retained expandable-string entry, rejection of a foreign entry and a concurrent change. The workspace verifier and full .NET integration/process smoke passed. No real Windows startup preference, WSL runtime, model or stable deployment was changed.

Retained proofs:

- `local/apps/agent-relay/qualification/storage-b36ca06b810c488383ec85ab35446eed/result.json`, SHA-256 `d2ec8aee2d4be28913a211f85a49244f3aba526950cd5d453f9d74335bfb78a7`.
- `var/apps/agent-relay/migrations/4cc4b0f98bc841e6a833cd671552e4c7/result.json`, SHA-256 `2bb49bbd78930efc6eb88391fbb684201c21817fe54d4dff1b6256ce5843ec3c`.
- `local/apps/agent-relay/tests/3e325df3fbe541ec8f4d6d46207b1f6a/integration.log`, SHA-256 `64adc306695ba310861482f40d47eaabd51e6d5572edbb31a285b78d3f0f66b7`.

Self-review: the earlier inspection missed a second registry owner because it stopped at the normal startup path. When developing Storage and Cleanup, inspect maintenance callers as well as UI callers before assigning rules. The preview and deletion should share the same C# protection policy.
