#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('help', 'setup', 'doctor', 'qualify', 'build', 'test', 'run')]
    [string] $Action = 'help',
    [string] $App,
    [switch] $Offline,
    [switch] $Json
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'tools/Workspace.psm1') -Force
try {
    if ($Action -eq 'help') {
        Write-Output @'
Workspace commands (PowerShell 7):
  ./dev.ps1 setup -App agent-relay [-Offline]
  ./dev.ps1 doctor [-App agent-relay] [-Json]
  ./dev.ps1 qualify -App agent-relay
  ./dev.ps1 build -App agent-relay
  ./dev.ps1 test -App agent-relay
  ./dev.ps1 run -App agent-relay
  ./tools/verify.ps1

Only setup installs the locked local tools. No command starts WSL or invokes an AI client.
Exit codes: 0 success; 1 action/check failure; 2 missing qualification/tool;
            3 application not implemented; 64 invalid selection/configuration.
'@
        exit 0
    }
    if (-not $App -and $Action -ne 'doctor') {
        Stop-WorkspaceAction 'Select an application explicitly with -App.' 64
    }
    if ($Action -eq 'doctor') {
        $applications = if ($App) { @(Resolve-WorkspaceApplication $PSScriptRoot $App) }
                        else { @(Get-WorkspaceApplications $PSScriptRoot) }
        $results = @($applications | ForEach-Object {
            & $_.DriverPath -Action doctor -WorkspaceRoot $PSScriptRoot -ManifestPath $_.ManifestPath
        })
        if ($Json) { ConvertTo-Json -InputObject $results -Depth 6 }
        else {
            foreach ($result in $results) {
                Write-Output ("{0}: {1}" -f $result.Application, $result.Summary)
            }
        }
        if (@($results | Where-Object { -not $_.Ready }).Count) { exit 2 }
        exit 0
    }
    $application = Resolve-WorkspaceApplication $PSScriptRoot $App
    if ($Action -eq 'setup') {
        if ($application.Manifest.Toolchain -ne 'dotnet') { Stop-WorkspaceAction 'No setup driver exists for this application toolchain.' 64 }
        & (Join-Path $PSScriptRoot 'tools/install-toolchain.ps1') -Offline:$Offline
        exit $LASTEXITCODE
    }
    if ($Action -eq 'qualify') {
        & (Join-Path $PSScriptRoot 'tools/qualify-dotnet.ps1') -App $application.Id
        exit $LASTEXITCODE
    }
    $driverArguments = @{ Action = $Action; WorkspaceRoot = $PSScriptRoot; ManifestPath = $application.ManifestPath }
    if ($Offline) { $driverArguments.Offline = $true }
    & $application.DriverPath @driverArguments
    exit $LASTEXITCODE
}
catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    $code = $_.Exception.Data['ExitCode']
    if ($null -eq $code) { exit 1 }
    exit [int]$code
}
