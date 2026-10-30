#requires -Version 7.2
[CmdletBinding()]
param(
    [switch] $Offline,
    [string] $InstallRoot = 'local/toolchains',
    [string] $DownloadRoot = 'local/tmp/downloads',
    [string] $LockFile = 'tools/toolchains/windows-x64.json',
    [switch] $SkipConfigure
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'Workspace.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'ToolchainInstallation.psm1') -Force
$localRoot = Resolve-ChildPath $root 'local'
$installation = Resolve-ChildPath $root $InstallRoot
$downloads = Resolve-ChildPath $root $DownloadRoot
foreach ($path in @($installation, $downloads)) {
    if (-not $path.StartsWith($localRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installation and archive cache must stay beneath repository local/.'
    }
}
$lockPath = Resolve-ChildPath $root $LockFile
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
if ($lock.schemaVersion -ne 1 -or $lock.platform -ne 'win-x64') { throw 'Unsupported toolchain lock.' }
if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64' -or -not $IsWindows) {
    throw 'This lock requires Windows x64.'
}
$drive = Get-PSDrive -Name ([IO.Path]::GetPathRoot($installation).Substring(0, 1))
if ($drive.Free -lt 3GB) { throw 'Less than 3 GiB free; no automatic deletion is permitted.' }
New-Item -ItemType Directory -Path $installation, $downloads -Force | Out-Null
$locks = [Collections.Generic.List[IDisposable]]::new()
try {
    foreach ($directory in @($downloads, $installation) | Select-Object -Unique) {
        try {
            $locks.Add([IO.File]::Open((Join-Path $directory '.toolchain-install.lock'),
                [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None))
        }
        catch [IO.IOException] { throw "Another installer is using $directory; retry after it finishes. Lock handles release on process exit." }
    }
    $started = [DateTimeOffset]::UtcNow
    $packages = @{}
    $measurements = foreach ($package in $lock.packages) {
        $archive = Get-VerifiedArchive $downloads $package -Offline:$Offline
        $directory = Install-VerifiedPackage $installation $archive $package
        $packages[$package.id] = Resolve-ChildPath $directory $package.entrypoint
        $record = Get-Content -LiteralPath (Join-Path $directory '.installation.json') -Raw | ConvertFrom-Json
        [pscustomobject]@{
            Id = $package.id; Version = $package.version; Executable = $packages[$package.id]
            ArchiveBytes = (Get-Item -LiteralPath $archive).Length
            InstalledBytes = ($record.Files | Measure-Object -Property Bytes -Sum).Sum
            ArchiveHash = $package.hash
        }
    }
    $report = [ordered]@{
        StartedUtc = $started.ToString('o'); FinishedUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Offline = [bool]$Offline; LockSha256 = (Get-FileHash -LiteralPath $lockPath -Algorithm SHA256).Hash
        Packages = @($measurements)
    }
    $report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $installation 'installation-report.json') -Encoding utf8
    if ($packages.ContainsKey('dotnet') -and $packages.ContainsKey('netcoredbg')) {
        $sdk = $packages.dotnet.Replace("'", "''")
        $debugger = $packages.netcoredbg.Replace("'", "''")
        $configuration = "@{ DotnetPath = '$sdk'; DebuggerPath = '$debugger' }"
        $configuration | Set-Content -LiteralPath (Join-Path $installation 'toolchain.psd1') -Encoding utf8
        if (-not $SkipConfigure) {
            $configuration | Set-Content -LiteralPath (Join-Path $root 'local/toolchains/dotnet.psd1') -Encoding utf8
        }
    }
    Write-Output ($report | ConvertTo-Json -Depth 5)
}
finally { foreach ($handle in $locks) { $handle.Dispose() } }
