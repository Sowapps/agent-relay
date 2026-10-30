#requires -Version 7.2
<#
Utility: Generate reviewable additive project/connection requests using existing administrative contracts.
Usage: Supply explicit existing workspaces and an authorized data directory. This example writes requests only; it never applies them, starts WSL or grants itself authority.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $DataDirectory,
    [Parameter(Mandatory)] [string] $Name,
    [Parameter(Mandatory)] [string] $MainWorkspace,
    [Parameter(Mandatory)] [string] $PeerWorkspace,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [switch] $LocalOnly,
    [string] $ProfileId
)
$ErrorActionPreference = 'Stop'
$data = [IO.Path]::GetFullPath($DataDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $MainWorkspace -PathType Container)) { throw 'MainWorkspace must be an existing, explicitly authorized Windows folder.' }
if (Test-Path -LiteralPath (Join-Path $output 'configure-project.request.json')) { throw 'Request output already exists; use a fresh review directory.' }
if ($LocalOnly) {
    if ($ProfileId) { throw 'LocalOnly and ProfileId are mutually exclusive.' }
    if (-not (Test-Path -LiteralPath $PeerWorkspace -PathType Container)) { throw 'LocalOnly requires an existing Windows peer folder.' }
    $adapter = 'local-files'; $binding = 'local'; $peerLocation = [IO.Path]::GetFullPath($PeerWorkspace)
}
else {
    if (-not $ProfileId -or -not $PeerWorkspace.StartsWith('/')) { throw 'Native mode requires an explicit existing ProfileId and absolute Linux workspace.' }
    $settings = Get-Content -LiteralPath (Join-Path $data 'relay-config.json') -Raw | ConvertFrom-Json -AsHashtable
    $profiles = @($settings.profiles | Where-Object { $_.id -eq $ProfileId -and $_.client -in @('codex','claude') })
    if ($profiles.Count -ne 1) { throw 'ProfileId must identify an existing supported native technical profile; do not guess it.' }
    $adapter = 'wsl-agent'; $binding = $ProfileId; $peerLocation = $PeerWorkspace.TrimEnd('/')
}
$configurationPath = Join-Path $data 'relay-projects.json'
$configuration = if (Test-Path -LiteralPath $configurationPath) { Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json -AsHashtable } else { @{ version=1; projects=@(); endpoints=@(); workspaces=@(); memberships=@() } }
if ($configuration.version -ne 1) { throw 'Unsupported central project configuration version.' }
$projectId = [guid]::NewGuid().ToString('D'); $mainId = [guid]::NewGuid().ToString('D'); $peerId = [guid]::NewGuid().ToString('D')
$mainWorkspaceId = [guid]::NewGuid().ToString('D'); $peerWorkspaceId = [guid]::NewGuid().ToString('D'); $connectionId = [guid]::NewGuid().ToString('D')
$configuration.projects = @($configuration.projects) + @{id=$projectId;name=$Name;enabled=$true}
$configuration.endpoints = @($configuration.endpoints) + @{id=$mainId;name='Principal - '+$Name;adapterId='local-files';bindingId='local'} + @{id=$peerId;name='Participant - '+$Name;adapterId=$adapter;bindingId=$binding}
$configuration.workspaces = @($configuration.workspaces) + @{id=$mainWorkspaceId;endpointId=$mainId;name='Principal workspace';location=[IO.Path]::GetFullPath($MainWorkspace);concurrentWrites=$false} + @{id=$peerWorkspaceId;endpointId=$peerId;name='Participant workspace';location=$peerLocation;concurrentWrites=$false}
$mainRights = if ($LocalOnly) { @('history.read','message.receive') } else { @('history.read','message.send','message.receive','session.manage','file.read','host.inspect') }
$peerRights = if ($LocalOnly) { @('message.receive') } else { @('message.send','message.receive') }
$mainAccess = @(@{workspaceId=$mainWorkspaceId;read=$true;write=$false;replace=$false},@{workspaceId=$peerWorkspaceId;read=$true;write=$false;replace=$false})
$configuration.memberships = @($configuration.memberships) + @{projectId=$projectId;endpointId=$mainId;role='main';capabilities=$mainRights;workspaces=$mainAccess;commandIds=@()} + @{projectId=$projectId;endpointId=$peerId;role='secondary';capabilities=@($peerRights);workspaces=@(@{workspaceId=$peerWorkspaceId;read=$true;write=$false;replace=$false});commandIds=@()}
New-Item -ItemType Directory -Path $output -Force | Out-Null
@{operation='configure_projects';projectsConfiguration=$configuration} | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $output 'configure-project.request.json') -Encoding utf8
@{operation='connection';connection=@{id=$connectionId;endpointId=$mainId;projectIds=@($projectId);revoke=$false}} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $output 'connection.request.json') -Encoding utf8
@{projectId=$projectId;mainId=$mainId;peerId=$peerId;mainWorkspaceId=$mainWorkspaceId;peerWorkspaceId=$peerWorkspaceId;connectionId=$connectionId;localOnly=[bool]$LocalOnly} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'example-context.json') -Encoding utf8
Write-Output "Review requests in $output. No application configuration or connection has been applied."
