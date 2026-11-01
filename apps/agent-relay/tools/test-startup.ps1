#requires -Version 7.2
<#
Utility: Qualifies the startup UI/native bootstrap against an explicitly prepared self-contained payload.
Usage: Build tests first, then pass a local qualified payload; creates/removes only an isolated per-user Run entry. Never enables the operator's data scope or restarts Windows.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$PayloadDirectory)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$payload=[IO.Path]::GetFullPath($PayloadDirectory)
$owned=[IO.Path]::GetFullPath((Join-Path $root 'local/apps/agent-relay'))+[IO.Path]::DirectorySeparatorChar
if(-not $payload.StartsWith($owned,[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath (Join-Path $payload 'hostfxr.dll'))){throw 'Supply a self-contained payload under application local storage.'}
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context=New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
$tests=Get-AppAssembly $context $context.Application.Manifest.TestProject
$directory=Join-Path $root ('local/apps/agent-relay/qualification/startup-'+[guid]::NewGuid().ToString('N'))
$probe=Join-Path $directory 'probe';New-Item -ItemType Directory $probe -Force | Out-Null
Get-ChildItem -LiteralPath $payload | Copy-Item -Destination $probe -Recurse
foreach($name in @('AgentRelay.Tests.dll','AgentRelay.Tests.deps.json','AgentRelay.Tests.runtimeconfig.json')){Copy-Item -LiteralPath (Join-Path (Split-Path $tests -Parent) $name) -Destination $probe -Force}
Invoke-AppDotnet $context @((Join-Path $probe 'AgentRelay.Tests.dll'),'monitor-startup',(Join-Path $directory 'test data'),(Join-Path $directory 'render'))
# The passing scenario has released its bounded host and removed its Run entry. Keep reports,
# fixtures and captures; discard only this invocation's bulky native test runtimes.
foreach($target in @($probe,(Join-Path $directory 'test data/startup-launcher'))){
    $resolved=[IO.Path]::GetFullPath($target)
    if(-not $resolved.StartsWith([IO.Path]::GetFullPath($directory)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Qualification cleanup escaped its own directory.'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
Write-Output "Startup qualification: $directory"
