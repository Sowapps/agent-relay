#requires -Version 7.2
[CmdletBinding()]
param([string]$Name='storage-prototype')
$ErrorActionPreference='Stop'
if($Name -notmatch '^[a-z0-9-]+$'){throw 'Use a simple qualification name.'}
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$assets=Join-Path $root ('local/apps/agent-relay/'+$Name+'/assets')
$output=Join-Path $root ('local/apps/agent-relay/'+$Name+'/render')
New-Item -ItemType Directory -Force $assets | Out-Null
$source=Join-Path $root 'apps/agent-relay/src/AgentRelay.Windows/Presentation/Assets'
Copy-Item (Join-Path $source '*') $assets -Force
Copy-Item (Join-Path $PSScriptRoot 'cleanup-preview.js') $assets -Force
$page=Join-Path $assets 'index.html'
$html=[IO.File]::ReadAllText($page).Replace('<script src="controller.js"></script>','<script src="cleanup-preview.js"></script><script src="controller.js"></script>')
[IO.File]::WriteAllText($page,$html)
$css=Join-Path $assets 'desktop.css'
[IO.File]::AppendAllText($css,"`n"+[IO.File]::ReadAllText((Join-Path $PSScriptRoot 'cleanup-preview.css')))
Import-Module (Join-Path $PSScriptRoot '../tools/AppCommands.psm1') -Force
$context=New-AppCommandContext $root (Join-Path $PSScriptRoot '../app.psd1')
if(-not(Test-Path (Join-Path $PSScriptRoot '../src/AgentRelay.Windows/bin/Debug/net10.0-windows/win-x64/AgentRelay.Windows.dll'))){throw 'Build the application once with the normal development command; this checker never rebuilds or deploys it.'}
Invoke-AppDotnet $context @('build','prototype/checker/PrototypeCheck.csproj','--disable-build-servers','-p:UseSharedCompilation=false','-p:NuGetAudit=false','--configfile',(Join-Path $context.CacheRoot 'offline.NuGet.Config'))
Invoke-AppDotnet $context @((Join-Path $PSScriptRoot 'checker/bin/Debug/net10.0-windows/PrototypeCheck.dll'),$assets,(Join-Path $PSScriptRoot 'fixtures.json'),(Join-Path $PSScriptRoot 'checks.js'),$output)
Write-Output "Prototype-only captures: $output"
