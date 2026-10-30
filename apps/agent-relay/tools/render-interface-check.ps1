#requires -Version 7.2
[CmdletBinding()]
param([string] $Name = 'native-interface', [string] $DataDirectory)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a simple qualification name.' }
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
$directory = Join-Path $root ('local/apps/agent-relay/qualification/' + $Name)
$data = if ($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } else { Join-Path $directory 'data' }
$tests = Get-AppAssembly $context $context.Application.Manifest.TestProject
Invoke-AppDotnet $context @($tests, 'monitor-render', $data, (Join-Path $directory 'render'))
Write-Output "Own-control images and checks: $directory/render (no desktop capture or input)"
