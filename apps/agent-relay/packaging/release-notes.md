# Agent Relay v1.1.2

Updates replace all managed application, documentation, MCP and startup-launcher components in the existing installation. Repeating a completed update preserves the running host and installed files. Projects, permissions, credentials, history and the startup preference are retained.

Recovery uses a separately retained, integrity-checked tool and coherent snapshots. Status, resume and rollback do not require the original download. Interrupted rollback finishes its own admission barrier before another update starts. New work is never overwritten with an old database after admission reopens.

Update failures have a stable code, diagnostic reference and retrievable details. MCP connection status exposes a historical error summary. A mapped client binary can cause safe rollback; client catalog reload remains explicit and unconfirmed clients are never reported current. No agent request is replayed automatically.

This package targets Windows x64 and includes .NET. See the portable release guide for prerequisites and recovery. Verify the ZIP against SHA256SUMS.txt.
