Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-ArchiveHash {
    param([string] $Path, $Package)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    return (Get-FileHash -LiteralPath $Path -Algorithm $Package.algorithm).Hash -eq $Package.hash
}

function Get-VerifiedArchive {
    param([string] $Directory, $Package, [switch] $Offline)
    $archive = Resolve-ChildPath $Directory $Package.archive
    if (Assert-ArchiveHash $archive $Package) { return $archive }
    if ($Offline) { throw "Offline archive missing or checksum mismatch: $($Package.archive)" }
    if (Test-Path -LiteralPath $archive) {
        Move-Item -LiteralPath $archive -Destination ($archive + '.rejected-' + [guid]::NewGuid().ToString('N'))
    }
    $uri = [uri]$Package.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin @('builds.dotnet.microsoft.com', 'github.com')) {
        throw 'Downloads must use the locked official HTTPS publisher URL.'
    }
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        $partial = $archive + '.partial-' + [guid]::NewGuid().ToString('N')
        try {
            Write-Host "Downloading $($Package.id) $($Package.version), attempt $attempt..."
            Invoke-WebRequest -Uri $uri -OutFile $partial -TimeoutSec 180
            if (-not (Assert-ArchiveHash $partial $Package)) { throw 'Downloaded archive checksum mismatch.' }
            Move-Item -LiteralPath $partial -Destination $archive
            return $archive
        }
        catch {
            if ($attempt -eq 2) { throw }
            Write-Warning "Download failed; preserved partial file and retrying official URL: $($_.Exception.Message)"
        }
    }
}

function Get-InstallationFiles {
    param([string] $Directory)
    foreach ($file in Get-ChildItem -LiteralPath $Directory -Recurse -File | Where-Object Name -ne '.installation.json') {
        [pscustomobject]@{
            Path = [IO.Path]::GetRelativePath($Directory, $file.FullName).Replace('\', '/')
            Bytes = $file.Length
            Hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        }
    }
}

function Test-Installation {
    param([string] $Directory, $Package)
    $marker = Join-Path $Directory '.installation.json'
    if (-not (Test-Path -LiteralPath $marker)) { return $false }
    try {
        $record = Get-Content -LiteralPath $marker -Raw | ConvertFrom-Json
        if ($record.ArchiveHash -ne $Package.hash -or $record.Version -ne $Package.version -or
            -not (Test-Path -LiteralPath (Resolve-ChildPath $Directory $Package.entrypoint))) { return $false }
        foreach ($entry in $record.Files) {
            $file = Resolve-ChildPath $Directory $entry.Path
            if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or
                (Get-Item -LiteralPath $file).Length -ne $entry.Bytes -or
                (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $entry.Hash) { return $false }
        }
        return @($record.Files).Count -gt 0
    }
    catch { return $false }
}

function Install-VerifiedPackage {
    param([string] $Root, [string] $Archive, $Package)
    if ($Package.id -notmatch '^[a-z][a-z0-9-]*$' -or $Package.version -notmatch '^[0-9][a-zA-Z0-9.\-]*$') {
        throw 'Invalid package identifier/version.'
    }
    $destination = Resolve-ChildPath $Root ($Package.id + '/' + $Package.version)
    if (Test-Installation $destination $Package) {
        Write-Host "Verified existing installation: $($Package.id) $($Package.version)"
        return $destination
    }
    $stage = Resolve-ChildPath $Root ('.staging/' + $Package.id + '-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead($Archive)
    try {
        foreach ($entry in $zip.Entries) { [void](Resolve-ChildPath $stage $entry.FullName.TrimEnd('/')) }
        [IO.Compression.ZipFileExtensions]::ExtractToDirectory($zip, $stage)
    }
    finally { $zip.Dispose() }
    if (-not (Test-Path -LiteralPath (Resolve-ChildPath $stage $Package.entrypoint) -PathType Leaf)) {
        throw "Expected executable missing after extraction: $($Package.entrypoint); staging retained."
    }
    $files = @(Get-InstallationFiles $stage)
    [ordered]@{ Id = $Package.id; Version = $Package.version; ArchiveHash = $Package.hash; Files = $files } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $stage '.installation.json') -Encoding utf8
    if (Test-Path -LiteralPath $destination) {
        $recovery = Resolve-ChildPath $Root ('.recovery/' + $Package.id + '-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path (Split-Path $recovery -Parent) -Force | Out-Null
        # Both normalized targets have been checked against the same installation root.
        Move-Item -LiteralPath $destination -Destination $recovery
        Write-Warning "Incomplete installation preserved at $recovery"
    }
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Move-Item -LiteralPath $stage -Destination $destination
    return $destination
}

Export-ModuleMember -Function Assert-ArchiveHash, Get-VerifiedArchive, Test-Installation, Install-VerifiedPackage
