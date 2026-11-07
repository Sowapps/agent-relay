#requires -Version 7.2
<#
Utility: render the verified portable release's own components using WebView2's internal capture.
Usage: pass the retained qualify/result.json. Inspect the generated images before recording visual acceptance.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Receipt)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$qualification = Get-Content -LiteralPath $Receipt -Raw | ConvertFrom-Json
if ($qualification.action -ne 'qualify' -or $qualification.status -ne 'passed') {
    throw 'Select a passed portable archive qualification receipt.'
}
$archive = [IO.Path]::GetFullPath($qualification.candidate.archive)
$releases = Join-Path $root 'local/apps/agent-relay/releases'
if (-not $archive.StartsWith($releases + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $qualification.candidate.sha256) {
    throw 'The archive is outside the release directory or its verified identity changed.'
}
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $root (Join-Path $PSScriptRoot '../app.psd1')
Build-App $context -Offline
$tests = Get-AppAssembly $context $context.Application.Manifest.TestProject
$case = Join-Path $root ('local/apps/agent-relay/qualification/release-render-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $case | Out-Null
Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $case 'package')
$app = Join-Path $case 'package/app'
foreach ($file in @('AgentRelay.Tests.dll', 'AgentRelay.Tests.deps.json', 'AgentRelay.Tests.runtimeconfig.json')) {
    Copy-Item -LiteralPath (Join-Path (Split-Path $tests -Parent) $file) -Destination $app
}
$output = Join-Path $case 'render'
Invoke-AppDotnet $context @((Join-Path $app 'AgentRelay.Tests.dll'), 'monitor-review', (Join-Path $case 'fixture'), $output)
Write-Output "RENDER_DIRECTORY=$output"
