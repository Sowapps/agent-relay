#requires -Version 7.2
[CmdletBinding()]
param([string] $Name = 'native-conformance')
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a simple qualification name.' }
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
Build-App $context -Offline
$python = (Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
& $python -X utf8 -B (Join-Path $PSScriptRoot 'prepare-conformance.py')
if ($LASTEXITCODE -ne 0) { throw 'Comparison preparation failed.' }
$fixture = Join-Path $root 'local/apps/agent-relay/qualification/conformance/reference-fixtures.json'
$cases = Join-Path $root 'local/apps/agent-relay/qualification/conformance/comparison-cases.json'
$output = Join-Path $root ('local/apps/agent-relay/qualification/' + $Name)
$tests = Get-AppAssembly $context $context.Application.Manifest.TestProject
Invoke-AppDotnet $context @($tests, 'monitor-conformance', $fixture, $cases, $output)
Write-Output "Native own-surface captures and layout checks: $output"
