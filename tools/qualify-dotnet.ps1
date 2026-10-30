#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $App,
    [string] $ToolchainConfig,
    [string] $ReportDirectory
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'Workspace.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ToolchainEnvironment.psm1') -Force
function Publish-QualificationReport {
    param([string] $Directory, $Record)
    $Record['FinishedUtc'] = [DateTimeOffset]::UtcNow.ToString('o')
    $path = Join-Path $Directory 'qualification.json'
    $pending = $path + '.pending-' + [guid]::NewGuid().ToString('N')
    $Record | ConvertTo-Json | Set-Content -LiteralPath $pending -Encoding utf8
    if (Test-Path -LiteralPath $path) {
        [IO.File]::Replace($pending, $path, [System.Management.Automation.Language.NullString]::Value)
    }
    else { [IO.File]::Move($pending, $path) }
}
$application = Resolve-WorkspaceApplication $root $App
$inventory = Get-DotnetStatus $root $application
$output = Join-Path $root ('local/toolchains/' + $App)
if ($ToolchainConfig) {
    $settings = Import-PowerShellDataFile -LiteralPath (Resolve-ChildPath $root $ToolchainConfig)
    $inventory.DotnetPath = $settings.DotnetPath
    $inventory.DebuggerPath = $settings.DebuggerPath
    $versions = & $inventory.DotnetPath --list-sdks
    if ($LASTEXITCODE -ne 0 -or -not ($versions -match '^10\.')) { throw 'Override toolchain lacks SDK 10.' }
    $inventory.SdkStatus = 'available'
}
if ($ReportDirectory) {
    $output = Resolve-ChildPath $root $ReportDirectory
    $allowed = Resolve-ChildPath $root 'local/toolchains'
    if (-not $output.StartsWith($allowed + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Qualification reports must stay beneath local/toolchains/.'
    }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$inventory | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'inventory.json') -Encoding utf8
$report = [ordered]@{
    SchemaVersion = 1
    Application = $App
    Qualified = $false
    SdkAvailable = ($inventory.SdkStatus -eq 'available')
    CompilationPassed = $false
    ExecutionPassed = $false
    NominalTestPassed = $false
    InteractiveDebuggingDemonstrated = $false
    DesktopPumpPassed = $false
    TrayAddPassed = $false
    TrayRemovePassed = $false
    Reason = ''
    Fingerprint = $null
    ProbeDirectory = $null
    SdkVersion = $null
    DebuggerVersion = $null
    PythonPath = $null
    PythonVersion = $null
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    OperatingSystem = [Runtime.InteropServices.RuntimeInformation]::OSDescription
    StartedUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
if (-not $report.SdkAvailable) {
    $report.Reason = 'SDK .NET 10 missing. PowerShell runtime is not an SDK. No software was installed.'
    Publish-QualificationReport $output $report
    Write-Output $report.Reason
    exit 2
}

$probe = Join-Path $output ('probe-' + [guid]::NewGuid().ToString('N'))
$report.ProbeDirectory = $probe
New-Item -ItemType Directory -Path $probe | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'qualification/dotnet/Probe.csproj'),
    (Join-Path $PSScriptRoot 'qualification/dotnet/Program.cs') -Destination $probe
Copy-Item -LiteralPath (Join-Path $application.Root 'global.json') -Destination $probe
# This fixture has no external packages. Prevent an implicit package download.
'<configuration><packageSources><clear /></packageSources></configuration>' |
    Set-Content -LiteralPath (Join-Path $probe 'NuGet.Config') -Encoding utf8
try {
    Invoke-IsolatedToolchain -DotnetPath $inventory.DotnetPath -CacheRoot (Join-Path $root 'local/toolchains/cache') -Action {
        # SDK resolution uses the current directory, not the project argument.
        # Execute beside the copied global.json so qualification uses the app pin.
        Push-Location -LiteralPath $probe
        try {
            $version = & $inventory.DotnetPath --version
            if ($LASTEXITCODE -ne 0) { throw 'Pinned SDK could not be selected.' }
            $report.SdkVersion = $version
            & $inventory.DotnetPath build (Join-Path $probe 'Probe.csproj') --nologo --configuration Debug `
                --configfile (Join-Path $probe 'NuGet.Config') --disable-build-servers -p:UseSharedCompilation=false
            $buildExit = $LASTEXITCODE
        }
        finally { Pop-Location }
        if ($buildExit -ne 0) { throw 'Probe compilation failed.' }
        $report.CompilationPassed = $true
        $dll = Join-Path $probe 'bin/Debug/net10.0/Probe.dll'
        $response = & $inventory.DotnetPath $dll
        if ($LASTEXITCODE -ne 0 -or ($response | ConvertFrom-Json).Body -ne 'ack:ping') { throw 'Probe execution failed.' }
        $report.ExecutionPassed = $true
        $test = & $inventory.DotnetPath $dll --self-test
        if ($LASTEXITCODE -ne 0 -or $test -ne 'NOMINAL_MESSAGE_ROUNDTRIP=PASS') { throw 'Nominal probe test failed.' }
        $report.NominalTestPassed = $true
        if (-not $inventory.DebuggerPath) { throw 'Build/run/test passed; debugger is missing.' }
        $report.DebuggerVersion = (& $inventory.DebuggerPath --version | Select-Object -First 1)
        $python = Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
        $report.PythonPath = $python.Source
        $report.PythonVersion = (& $python.Source --version)
        if ($LASTEXITCODE -ne 0) { throw 'A working Python 3 interpreter is required for debugger control.' }
        & $python.Source -B (Join-Path $PSScriptRoot 'qualification/debug_probe.py') `
            --debugger $inventory.DebuggerPath --dotnet $inventory.DotnetPath --dll $dll --source (Join-Path $probe 'Program.cs') `
            --transcript (Join-Path $probe 'debugger-transcript.jsonl') --result (Join-Path $probe 'debugger-result.json')
        if ($LASTEXITCODE -ne 0) { throw 'Real debugger qualification failed; inspect probe debugger transcript.' }
        $debug = Get-Content -LiteralPath (Join-Path $probe 'debugger-result.json') -Raw | ConvertFrom-Json
        if (-not $debug.Passed) { throw 'Debugger reported unsuccessful qualification.' }
        $report.InteractiveDebuggingDemonstrated = $true
        $desktop = Join-Path $probe 'desktop'
        New-Item -ItemType Directory -Path $desktop | Out-Null
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'qualification/desktop/DesktopProbe.csproj'),
            (Join-Path $PSScriptRoot 'qualification/desktop/Program.cs') -Destination $desktop
        Push-Location -LiteralPath $probe
        try {
            & $inventory.DotnetPath build (Join-Path $desktop 'DesktopProbe.csproj') --nologo --configuration Debug `
                --configfile (Join-Path $probe 'NuGet.Config') --disable-build-servers -p:UseSharedCompilation=false
            if ($LASTEXITCODE -ne 0) { throw 'Windows desktop probe compilation failed.' }
        }
        finally { Pop-Location }
        $desktopResult = & $python.Source -B (Join-Path $PSScriptRoot 'qualification/run_probe.py') --timeout 20 `
            $inventory.DotnetPath (Join-Path $desktop 'bin/Debug/net10.0-windows/DesktopProbe.dll')
        if ($LASTEXITCODE -ne 0) { throw 'Windows message loop/notification-area probe failed.' }
        $desktopResult | Set-Content -LiteralPath (Join-Path $probe 'desktop-result.json') -Encoding utf8
        $desktopStatus = $desktopResult | ConvertFrom-Json
        $report.DesktopPumpPassed = [bool]$desktopStatus.DesktopPumpPassed
        $report.TrayAddPassed = [bool]$desktopStatus.TrayAddPassed
        $report.TrayRemovePassed = [bool]$desktopStatus.TrayRemovePassed
        if (-not ($report.DesktopPumpPassed -and $report.TrayAddPassed -and $report.TrayRemovePassed)) { throw 'Incomplete desktop qualification.' }
        $report.Fingerprint = Get-QualificationFingerprint $root $application $inventory.DotnetPath $inventory.DebuggerPath
        $report.Qualified = $true
        $report.Reason = 'Console build/run/test and actual debugging passed; Windows desktop message loop and shell notification icon add/remove passed.'
    }
}
catch { $report.Reason = $_.Exception.Message }
Publish-QualificationReport $output $report
Write-Output ($report | ConvertTo-Json)
if ($report.Qualified) { exit 0 }
exit 2
