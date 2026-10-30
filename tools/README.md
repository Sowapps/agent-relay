# Shared tools

`../dev.ps1` is the main entry point. `Workspace.psm1` resolves application ownership and prerequisites.
`verify.ps1` checks documentation, routing, installation recovery and environment restoration.
`install-toolchain.ps1` installs locked publisher archives locally. `qualify-dotnet.ps1` runs isolated console/debugger and Windows-desktop fixtures under `qualification/`.
`tests/qualification-record.ps1` additionally checks successful, stale, failed and interrupted report handling; it requires the installed toolchain.

See the [command reference](../doc/operating/entrypoints.md) and [toolchain gate](../doc/operating/toolchain.md).
The [setup procedure](../doc/operating/setup.md) documents reproduction and incident handling.
Fixtures and reports belong in ignored `local/`. These tools do not implement relay business behavior.
