#requires -Version 7.2
[CmdletBinding()]
param([switch] $Show, [switch] $CodexGlobal, [string] $DataDirectory, [string] $ReportDirectory)
$ErrorActionPreference = 'Stop'
if ($CodexGlobal) { throw 'Publish locally, then apply the reviewed final MCP fragment separately with register-codex.py. No runtime was stopped.' }
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$data = if ($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } else { Join-Path $root 'local/apps/agent-relay/data' }
$reports = if ($ReportDirectory) { [IO.Path]::GetFullPath($ReportDirectory) } else { Join-Path $root 'local/apps/agent-relay/reports/evening' }
$prepared = Join-Path $root ('local/apps/agent-relay/publishing/' + [guid]::NewGuid().ToString('N'))
$python = (Get-Command python.exe -CommandType Application | Select-Object -First 1).Source
& (Join-Path $PSScriptRoot 'export-mcp.ps1') -DataDirectory $prepared
$arguments = @('-B', (Join-Path $PSScriptRoot 'deployment.py'), '--data', $data, '--prepared', $prepared, '--reports', $reports)
if ($Show) { $arguments += '--show' }
& $python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Publication failed; inspect the deployment report and rollback status.' }
& (Join-Path $PSScriptRoot 'export-mcp.ps1') -DataDirectory $data
