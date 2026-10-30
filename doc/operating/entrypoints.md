# Main entry points

**Utility:** provides a small consistent interface across applications.  
**Usage:** run in PowerShell 7 from the repository root.

## Commands

| Command | Purpose |
| --- | --- |
| `./dev.ps1` | Help |
| `./dev.ps1 setup -App agent-relay [-Offline]` | Install or verify pinned tools locally; requires installation authorization |
| `./dev.ps1 doctor [-App agent-relay] [-Json]` | Read-only prerequisite inventory |
| `./dev.ps1 qualify -App agent-relay` | Isolated qualification report in `local/toolchains/agent-relay/` |
| `./dev.ps1 build -App agent-relay [-Offline]` | Locked restore and application build |
| `./dev.ps1 test -App agent-relay [-Offline]` | Integration checks and actual Windows process smoke |
| `./dev.ps1 run -App agent-relay [-Offline]` | Open the local application and wait for its exit |
| `./tools/verify.ps1` | Shared documentation, command routing, installation and environment fixtures |
| `./tools/audit-migration.ps1 -Snapshot local/migrations/<id>` | Compare a retained migration manifest with current files and proof hashes |

Only setup installs tools. No shared entry point starts WSL or invokes an AI client. Historical POC launchers require separate authorization.

## Results

Exit codes: `0` success, `1` failed check/action, `2` missing prerequisite or qualification, `3` application not implemented, `64` invalid selection/configuration.
Application actions stop at the first applicable guard. Shared verification does not establish application readiness.
Doctor reports SDK, debugger, implementation and qualification separately; an installed SDK cannot prove a working debugger.
Ready describes development prerequisites and application source presence, not a live relay process; the message client detects an absent host separately.
