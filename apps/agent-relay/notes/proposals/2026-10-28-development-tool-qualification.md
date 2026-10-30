# Development tool qualification proposal

**Date:** 2026-10-28  
**Status:** historical proposal; the installation and qualification lot was authorized and executed.

Read the [measured outcome](../../../../notes/observations/2026-10-28-isolated-dotnet-qualification.md) for actual results and remaining boundaries.

## Intended result

Establish whether this Windows machine can build, test and debug the selected .NET solution before creating the application.
Use isolated tools and fixtures; leave user and machine configuration unchanged.

## Tools and installation boundary

Download the Windows x64 SDK 10.0.403 ZIP from the official
[Microsoft binary page](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-10.0.403-windows-x64-binaries-zip).
Verify its SHA-512 against official release metadata before extraction into `local/toolchains/dotnet/10.0.403/`.
Record URL, published checksum, measured checksum and version. Configure its executable through the ignored local toolchain manifest.

Use upstream netcoredbg 3.2.0-1092 Windows x64 ZIP as the first debugger candidate. Verify the published checksum captured in the
[workspace observation](../../../../notes/observations/2026-10-28-multi-app-preparation.md) and extract under `local/toolchains/netcoredbg/3.2.0-1092/`.
This route avoids adding an IDE and exposes a protocol an agent can drive directly. Its .NET 10 compatibility must be demonstrated locally.

No global PATH or registry changes, service, startup task, WSL operation or AI-model call is part of this lot.
Downloads and execution of those isolated tools are the installation actions for which this proposal requests authorization.

## Acceptance

1. Compile the existing probe in Debug with the application's SDK pin; execute its JSON round trip and nominal self-test.
2. Drive a real debugger: launch, bind a breakpoint in `MakeReply`, stop, inspect `input.Id` and `input.Body`, step, continue and confirm the result.
3. Record exact SDK/runtime/debugger versions, process exits and debugger transcript. Re-running inventory must not treat a mismatched old report as qualification.
4. Before implementing the Windows application, qualify a minimal desktop message loop and notification icon separately; a console test cannot prove the UI dependency.

Create a narrow debugger adapter only against the installed candidate and exercise it against the real probe.
If the candidate fails, retain its diagnostic and investigate the Microsoft-supported route. Installing an IDE or changing the application technology needs a separately stated scope.

## Subsequent boundary

Application implementation follows only after the relevant gates pass. Named-pipe transport and SQLite persistence each need a small real smoke check before their application integration.
Reuse the existing POC's measured messaging constraints; do not repeat paid model qualification while qualifying Windows development tools.
