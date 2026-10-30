Set-StrictMode -Version Latest

function Stop-WorkspaceAction {
    param([string] $Message, [int] $ExitCode)
    $exception = [InvalidOperationException]::new($Message)
    $exception.Data['ExitCode'] = $ExitCode
    throw $exception
}

function Resolve-ChildPath {
    param([string] $Root, [string] $Relative)
    if ([IO.Path]::IsPathRooted($Relative)) { Stop-WorkspaceAction 'Expected a repository-relative path.' 64 }
    $parent = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $path = [IO.Path]::GetFullPath((Join-Path $parent $Relative))
    if (-not $path.StartsWith($parent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        Stop-WorkspaceAction 'Configured path escapes its application/workspace.' 64
    }
    return $path
}

function Resolve-WorkspaceApplication {
    param([string] $Root, [string] $Id)
    if ($Id -notmatch '^[a-z][a-z0-9-]*$') { Stop-WorkspaceAction 'Invalid application identifier.' 64 }
    $registry = Import-PowerShellDataFile -LiteralPath (Join-Path $Root 'workspace.psd1')
    if (-not $registry.Applications.ContainsKey($Id)) { Stop-WorkspaceAction "Unknown application: $Id" 64 }
    $manifestPath = Resolve-ChildPath $Root $registry.Applications[$Id]
    $manifest = Import-PowerShellDataFile -LiteralPath $manifestPath
    if ($manifest.Id -ne $Id) { Stop-WorkspaceAction 'Application identifier differs from its manifest.' 64 }
    $appRoot = [IO.Path]::GetDirectoryName($manifestPath)
    $driver = Resolve-ChildPath $appRoot $manifest.Driver
    if (-not (Test-Path -LiteralPath $driver -PathType Leaf)) { Stop-WorkspaceAction 'Application driver is missing.' 64 }
    return [pscustomobject]@{
        Id = $Id
        Root = $appRoot
        ManifestPath = $manifestPath
        Manifest = $manifest
        DriverPath = $driver
    }
}

function Get-WorkspaceApplications {
    param([string] $Root)
    $registry = Import-PowerShellDataFile -LiteralPath (Join-Path $Root 'workspace.psd1')
    foreach ($id in @($registry.Applications.Keys | Sort-Object)) { Resolve-WorkspaceApplication $Root $id }
}

function Get-DotnetStatus {
    param([string] $Root, $Application)
    $major = [int]$Application.Manifest.RequiredSdkMajor
    $candidates = [Collections.Generic.List[string]]::new()
    $configured = Join-Path $Root 'local/toolchains/dotnet.psd1'
    if (Test-Path -LiteralPath $configured) {
        $settings = Import-PowerShellDataFile -LiteralPath $configured
        if ($settings.ContainsKey('DotnetPath') -and $settings.DotnetPath) { $candidates.Add($settings.DotnetPath) }
    }
    $command = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { $candidates.Add($command.Source) }
    foreach ($name in @('DOTNET_ROOT', 'DOTNET_ROOT_X64', 'DOTNET_ROOT(x86)')) {
        $directory = [Environment]::GetEnvironmentVariable($name)
        if ($directory) { $candidates.Add((Join-Path $directory 'dotnet.exe')) }
    }
    foreach ($directory in @('C:\Program Files\dotnet', 'C:\Program Files (x86)\dotnet',
            (Join-Path $HOME '.dotnet'), (Join-Path $env:LOCALAPPDATA 'Microsoft/dotnet'))) {
        $candidates.Add((Join-Path $directory 'dotnet.exe'))
    }
    foreach ($registryRoot in @('HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions\x64',
            'HKLM:\SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64')) {
        $registration = Get-ItemProperty -LiteralPath $registryRoot -Name InstallLocation -ErrorAction SilentlyContinue
        if ($registration -and $registration.InstallLocation) {
            $candidates.Add((Join-Path $registration.InstallLocation 'dotnet.exe'))
        }
    }
    $sdkVersions = @()
    $dotnetPath = $null
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { continue }
        $listed = & $candidate --list-sdks 2>&1
        if ($LASTEXITCODE -ne 0) { continue }
        $matching = @($listed | ForEach-Object {
            if ($_ -match ('^(' + $major + '\.\d+\.\d+)\s+\[')) { $Matches[1] }
        })
        if ($matching.Count) { $sdkVersions = $matching; $dotnetPath = $candidate; break }
    }
    $debugger = Get-Command netcoredbg.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    $debuggerPath = if ($debugger) { $debugger.Source } else { $null }
    if ((Test-Path -LiteralPath $configured) -and $settings.ContainsKey('DebuggerPath') -and
        (Test-Path -LiteralPath $settings.DebuggerPath -PathType Leaf)) { $debuggerPath = $settings.DebuggerPath }
    $solution = Resolve-ChildPath $Application.Root $Application.Manifest.Solution
    $qualification = Join-Path $Root ('local/toolchains/' + $Application.Id + '/qualification.json')
    $qualificationState = 'not-run'
    if (Test-Path -LiteralPath $qualification) {
        try {
            $record = Get-Content -LiteralPath $qualification -Raw | ConvertFrom-Json
            $qualificationState = 'not-qualified'
            if ($record.Qualified -and $dotnetPath -and $debuggerPath) {
                $required = @('CompilationPassed', 'ExecutionPassed', 'NominalTestPassed', 'InteractiveDebuggingDemonstrated',
                    'DesktopPumpPassed', 'TrayAddPassed', 'TrayRemovePassed')
                $complete = $record.PSObject.Properties['SchemaVersion'] -and $record.SchemaVersion -eq 1 -and
                    $record.PSObject.Properties['Fingerprint']
                foreach ($flag in $required) {
                    if (-not $record.PSObject.Properties[$flag] -or $record.$flag -ne $true) { $complete = $false }
                }
                $fingerprint = Get-QualificationFingerprint $Root $Application $dotnetPath $debuggerPath
                $qualificationState = if ($complete -and $record.Fingerprint -eq $fingerprint) { 'qualified' } else { 'stale' }
            }
        }
        catch { $qualificationState = 'invalid-record' }
    }
    return [pscustomobject]@{
        Application = $Application.Id
        RequiredSdkMajor = $major
        SdkStatus = if ($dotnetPath) { 'available' } else { 'missing' }
        DotnetPath = $dotnetPath
        SdkVersions = $sdkVersions
        PowerShellRuntime = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        DebuggerStatus = if ($qualificationState -eq 'qualified') { 'demonstrated' }
                         elseif ($debuggerPath) { 'discovered-not-demonstrated' } else { 'not-discovered' }
        DebuggerPath = $debuggerPath
        ApplicationStatus = if (Test-Path -LiteralPath $solution) { 'source-present' } else { 'not-implemented' }
        QualificationStatus = $qualificationState
    }
}

function Get-QualificationFingerprint {
    param([string] $Root, $Application, [string] $DotnetPath, [string] $DebuggerPath)
    $files = @($DotnetPath, $DebuggerPath, (Join-Path $Application.Root 'global.json'),
        (Join-Path $Root 'tools/qualify-dotnet.ps1'), (Join-Path $Root 'tools/ToolchainEnvironment.psm1'),
        (Join-Path $Root 'tools/qualification/debug_probe.py')) +
        @(Get-ChildItem -LiteralPath (Join-Path $Root 'tools/qualification') -Recurse -File |
            Where-Object { $_.Extension -in '.py', '.cs', '.csproj' } | ForEach-Object FullName)
    $entries = foreach ($file in $files | Sort-Object -Unique) {
        [ordered]@{ Path = $file; Hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash }
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($entries | ConvertTo-Json -Compress))
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
}

Export-ModuleMember -Function Stop-WorkspaceAction, Resolve-ChildPath, Resolve-WorkspaceApplication,
    Get-WorkspaceApplications, Get-DotnetStatus, Get-QualificationFingerprint
