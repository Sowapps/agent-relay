[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidateSet('doctor', 'build', 'test', 'run')] [string] $Action,
    [Parameter(Mandatory)] [string] $WorkspaceRoot,
    [Parameter(Mandatory)] [string] $ManifestPath,
    [switch] $Offline
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $WorkspaceRoot 'tools/Workspace.psm1') -Force
$application = Resolve-WorkspaceApplication $WorkspaceRoot 'agent-relay'
$status = Get-DotnetStatus $WorkspaceRoot $application
if ($Action -eq 'doctor') {
    # Toolchain qualification is separate from application implementation.
    $status | Add-Member -NotePropertyName Ready -NotePropertyValue ($status.QualificationStatus -eq 'qualified' -and $status.ApplicationStatus -eq 'source-present')
    $status | Add-Member -NotePropertyName ToolchainReady -NotePropertyValue ($status.QualificationStatus -eq 'qualified')
    $summary = "SDK .NET 10: $($status.SdkStatus); debugger: $($status.DebuggerStatus); application: $($status.ApplicationStatus); qualification: $($status.QualificationStatus)"
    $status | Add-Member -NotePropertyName Summary -NotePropertyValue $summary
    return $status
}
if ($status.SdkStatus -ne 'available') { Stop-WorkspaceAction 'SDK .NET 10 not found. Run ./dev.ps1 doctor -App agent-relay.' 2 }
$solution = Resolve-ChildPath $application.Root $application.Manifest.Solution
if (-not (Test-Path -LiteralPath $solution)) {
    Stop-WorkspaceAction 'Agent Relay application sources are not implemented; prototypes are not a production build.' 3
}
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $WorkspaceRoot $ManifestPath
switch ($Action) {
    'build' { Build-App $context -Offline:$Offline }
    'test' { Test-App $context -Offline:$Offline }
    'run' { Start-App $context -Offline:$Offline }
}
exit 0
