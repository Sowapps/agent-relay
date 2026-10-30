#requires -Version 7.2
<#
Utility: Verify the documented bootstrap using isolated local relay data and no native agent.
Usage: Build first, then pass a fresh qualification name. The helper stops only its recorded owned host.
#>
[CmdletBinding()]
param([string] $Name = 'bootstrap-docs-20261030')
$ErrorActionPreference='Stop'
if($Name -notmatch '^[a-z0-9-]+$'){throw 'Use a simple qualification name.'}
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$base=Join-Path $root ('local/apps/agent-relay/qualification/'+$Name)
if(Test-Path -LiteralPath $base){throw 'Use a fresh qualification name; existing artifacts are preserved.'}
$data=Join-Path $base 'data';$reports=Join-Path $base 'reports'
$main=Join-Path $base 'workspaces/main';$peer=Join-Path $base 'workspaces/peer'
New-Item -ItemType Directory -Path @($main,$peer) -Force | Out-Null
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context=New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
$example=Join-Path $root 'apps/agent-relay/doc/using/examples/new-project-request.ps1'
& (Join-Path $PSScriptRoot 'export-mcp.ps1') -DataDirectory $data
$runtime=Get-Content -LiteralPath (Join-Path $data 'mcp-runtime.json') -Raw | ConvertFrom-Json
function Apply-Request([string] $Path){Invoke-AppDotnet $context @($runtime.assembly,'admin','--data-dir',$data,'--input',$Path) | Out-Null}
$first=Join-Path $base 'requests-first'
& $example -DataDirectory $data -Name 'Bootstrap first' -MainWorkspace $main -PeerWorkspace $peer -OutputDirectory $first -LocalOnly
Apply-Request (Join-Path $first 'configure-project.request.json')
$firstContext=Get-Content -LiteralPath (Join-Path $first 'example-context.json') -Raw | ConvertFrom-Json
$second=Join-Path $base 'requests-second'
& $example -DataDirectory $data -Name 'Bootstrap second' -MainWorkspace $main -PeerWorkspace $peer -OutputDirectory $second -LocalOnly
Apply-Request (Join-Path $second 'configure-project.request.json')
$settings=@{version=1;profiles=@(@{id='docs-native';name='Synthetic native binding';distribution='Unavailable';user='nobody';root='/srv/agent-relay-docs-profile';client='claude';commands=@();allowLocalWork=$false})}
$settings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $data 'relay-config.json') -Encoding utf8
$native=Join-Path $base 'requests-native'
& $example -DataDirectory $data -Name 'Bootstrap native schema only' -MainWorkspace $main -PeerWorkspace '/srv/agent-relay-docs-workspace' -ProfileId 'docs-native' -OutputDirectory $native
Apply-Request (Join-Path $native 'configure-project.request.json')
$saved=Get-Content -LiteralPath (Join-Path $data 'relay-projects.json') -Raw | ConvertFrom-Json
if(@($saved.projects).Count -ne 3 -or $firstContext.projectId -notin $saved.projects.id){throw 'Additive project example removed earlier configuration.'}
if(@($saved.endpoints | Where-Object {$_.adapterId -eq 'wsl-agent' -and $_.bindingId -eq 'docs-native'}).Count -ne 1){throw 'Native schema example lost its binding.'}
Apply-Request (Join-Path $first 'connection.request.json')
$pointerPath=Join-Path $data 'mcp-runtime.json'
$pointer=Get-Content -LiteralPath $pointerPath -Raw | ConvertFrom-Json -AsHashtable
$pointer.connectionId=$firstContext.connectionId
$pointer | ConvertTo-Json | Set-Content -LiteralPath ($pointerPath+'.pending') -Encoding utf8
Move-Item -LiteralPath ($pointerPath+'.pending') -Destination $pointerPath -Force
try{
    & (Join-Path $PSScriptRoot 'publish-local.ps1') -DataDirectory $data -ReportDirectory $reports
    $runtime=Get-Content -LiteralPath (Join-Path $data 'mcp-runtime.json') -Raw | ConvertFrom-Json
    & (Join-Path $PSScriptRoot 'render-interface-check.ps1') -Name ($Name+'-ui') -DataDirectory $data
    $probe=Join-Path $PSScriptRoot 'check-bootstrap-mcp.py'
    $python=(Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
    & $python -X utf8 -B $probe --data $data --expected-project $firstContext.projectId --foreign-project ($saved.projects | Where-Object {$_.id -ne $firstContext.projectId} | Select-Object -First 1).id --output (Join-Path $base 'mcp')
    if($LASTEXITCODE -ne 0){throw 'Fresh scoped MCP bootstrap checks failed.'}
    & $python -X utf8 -B (Join-Path $PSScriptRoot 'check-mcp-docs.py') --dotnet $runtime.dotnet --assembly $runtime.assembly --output (Join-Path $base 'docs')
    if($LASTEXITCODE -ne 0){throw 'Packaged documentation checks failed.'}
    @{passed=$true;projectsPreserved=3;nativeProfile='Synthetic schema only; no WSL/client/model started';setup='Existing qualified private toolchain; no installation performed';scope=$data;connection=$firstContext.connectionId} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $base 'result.json') -Encoding utf8
}
finally{
    if(Test-Path -LiteralPath (Join-Path $reports 'host.json')){
        $owned=Get-Content -LiteralPath (Join-Path $reports 'host.json') -Raw | ConvertFrom-Json
        $actual=Get-CimInstance Win32_Process -Filter ('ProcessId='+[int]$owned.pid)
        if($actual){
            if($owned.data -ne $data -or $actual.ExecutablePath -ne $owned.dotnet -or -not $actual.CommandLine.Contains($owned.assembly) -or -not $actual.CommandLine.Contains($data)){throw 'Owned-host cleanup identity differs; process preserved.'}
            Stop-Process -Id $owned.pid -ErrorAction Stop
            Wait-Process -Id $owned.pid -Timeout 10 -ErrorAction SilentlyContinue
        }
    }
}
Write-Output "BOOTSTRAP_DOCS=PASS $base (owned verification host stopped)"
