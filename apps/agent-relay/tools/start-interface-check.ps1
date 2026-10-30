#requires -Version 7.2
[CmdletBinding()]
param([string] $Name = 'native-interface', [switch] $Restart)
$ErrorActionPreference = 'Stop'
if ($Name -notmatch '^[a-z0-9-]+$') { throw 'Use a simple qualification name.' }
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'RuntimeStaging.psm1') -Force
$context = New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
Import-Module (Join-Path $PSScriptRoot 'RuntimeStaging.psm1') -Force -Global
$directory = Join-Path $root ('local/apps/agent-relay/qualification/' + $Name)
$data = Join-Path $directory 'data'
New-Item -ItemType Directory -Path $directory -Force | Out-Null
if ($Restart) {
    $metadata = Join-Path $directory 'host.json'
    if (-not (Test-Path -LiteralPath $metadata)) { throw 'No owned qualification host metadata exists.' }
    $previous = Get-Content -LiteralPath $metadata -Raw | ConvertFrom-Json
    if ($previous.syntheticRetainedFacts -ne $true -or [IO.Path]::GetFullPath($previous.data) -ne [IO.Path]::GetFullPath($data)) { throw 'Refusing to restart a different data scope.' }
    $candidate = Get-CimInstance Win32_Process -Filter "ProcessId=$([int]$previous.pid)"
    if ($candidate) {
        if ($candidate.ExecutablePath -ne $previous.dotnet -or -not $candidate.CommandLine.Contains($previous.assembly) -or -not $candidate.CommandLine.Contains($data)) { throw 'Recorded process identity changed; nothing stopped.' }
        Stop-Process -Id $candidate.ProcessId -ErrorAction Stop
    }
}
if (-not (Test-Path (Join-Path $data 'qualification.json'))) {
    $tests = Get-AppAssembly $context $context.Application.Manifest.TestProject
    Invoke-AppDotnet $context @($tests, 'monitor-fixture', $data)
}
try { $lease = [IO.File]::Open((Join-Path $data 'host.lock'), 'OpenOrCreate', 'ReadWrite', 'None'); $lease.Dispose() }
catch { throw 'This qualification data is already owned by an instance; use its Quitter action or the explicit -Restart check.' }
$assembly = Get-StagedApplication (Get-AppAssembly $context $context.Application.Manifest.RunProject) (Join-Path $root 'local/apps/agent-relay/run')
Import-Module (Join-Path $root 'tools/ToolchainEnvironment.psm1') -Force -Global
$start = [Diagnostics.ProcessStartInfo]::new($context.Status.DotnetPath)
$start.UseShellExecute = $false; $start.CreateNoWindow = $true
foreach ($argument in @($assembly, 'tray', '--data-dir', $data, '--show', '--window-title', 'Agent Relay · Interface qualification')) { $start.ArgumentList.Add($argument) }
$process = Invoke-IsolatedToolchain $context.Status.DotnetPath $context.CacheRoot { [Diagnostics.Process]::Start($start) }
if ($process.WaitForExit(500)) { throw "Qualification host exited immediately ($($process.ExitCode))." }
[ordered]@{ pid = $process.Id; assembly = $assembly; data = $data; dotnet = $context.Status.DotnetPath; windowTitle = 'Agent Relay · Interface qualification'; syntheticRetainedFacts = $true; nativeCalls = 0 } |
    ConvertTo-Json | Set-Content (Join-Path $directory 'host.json') -Encoding utf8
Write-Output "Interface qualification launched (PID $($process.Id)); use Quitter in its settings to close it."
