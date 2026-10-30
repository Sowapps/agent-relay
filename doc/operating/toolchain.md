# Toolchain qualification

**Utility:** distinguishes installed tools from demonstrated development capability.  
**Usage:** consult before authorizing .NET implementation or preparing missing tools.

## Discovery

Agent Relay targets .NET 10 and pins SDK 10.0.403 with patch roll-forward inside its own `global.json`.
PowerShell's embedded runtime is not a compiler SDK. Discovery checks PATH, environment roots, common locations and SDK registry entries.
The local configuration `local/toolchains/dotnet.psd1` contains explicit `DotnetPath` and `DebuggerPath` values. Discovery installs nothing.

## Gate

The isolated console probe must compile, execute a message round trip and pass its nominal self-test.
Full qualification also requires a real breakpoint in `MakeReply`, inspection of message values and continuation to expected output.
Portable PDB generation does not prove debugging capability.

The runner drives netcoredbg using Debug Adapter Protocol: source breakpoint, value inspection, step, continuation and exact output/exit verification.
It also compiles a Windows Forms fixture and verifies a message loop plus successful shell notification icon addition and removal.
The icon exists briefly for the test and is removed in a finally block. These are fixtures, not an Agent Relay application.

The report records versions, platform, all checks and a fingerprint of the selected executables, SDK pin, fixture sources and qualification scripts.
Doctor accepts a successful report only while its fingerprint and required checks match. Changed inputs are stale; interrupted JSON is invalid and can be replaced by rerunning qualification.
Report publication uses a temporary file and same-volume replacement. A portable PDB or an installed executable alone never makes the gate green.

Debugging requires an execution context that permits launching and controlling the target process. Restricted contexts can fail despite a working SDK.
Use the permitted execution context for the qualification command; keep the failed trace rather than diagnosing compatibility from that failure alone.

## Tool preparation

Read the [isolated setup and recovery procedure](setup.md). The pinned lock uses official
[Microsoft SDK metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) and
[netcoredbg release metadata](https://github.com/Samsung/netcoredbg/releases/tag/3.2.0-1092).
Install only after authorization covering installation. A successful development-tool qualification does not authorize application development or qualify SQLite, MCP or WSL integration.
