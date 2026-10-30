$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$relative = 'local/tool-tests/installation-' + [guid]::NewGuid().ToString('N')
$fixture = Join-Path $root $relative
$cache = Join-Path $fixture 'cache'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$archive = Join-Path $cache 'fixture.zip'
$lock = Join-Path $fixture 'lock.json'
$shellPath = (Get-Command pwsh.exe -CommandType Application | Select-Object -First 1).Source
$results = [Collections.Generic.List[object]]::new()

function Write-FixtureArchive {
    param([string] $Entry)
    $stream = [IO.File]::Open($archive, [IO.FileMode]::Create)
    $zip = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entryStream = $zip.CreateEntry($Entry).Open()
        try { $entryStream.Write([Text.Encoding]::UTF8.GetBytes('fixture-content')) }
        finally { $entryStream.Dispose() }
    }
    finally { $zip.Dispose(); $stream.Dispose() }
    [ordered]@{ schemaVersion = 1; platform = 'win-x64'; packages = @(@{
        id = 'fixture'; version = '1.0'; archive = 'fixture.zip'; entrypoint = 'fixture.txt'
        algorithm = 'SHA256'; hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        url = 'https://github.com/unused-offline-fixture'
    }) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $lock -Encoding utf8
}

function Check-Install {
    param([string] $Name, [int] $Expected, [string] $Text)
    $output = & $shellPath -NoProfile -File (Join-Path $root 'tools/install-toolchain.ps1') -Offline -SkipConfigure `
        -InstallRoot ($relative + '/installed') -DownloadRoot ($relative + '/cache') -LockFile ($relative + '/lock.json') 2>&1
    $actual = $LASTEXITCODE
    if ($actual -ne $Expected -or ($output -join "`n") -notlike ('*' + $Text + '*')) {
        throw "Installer case $Name failed; exit=$actual; output=$output"
    }
    $results.Add([pscustomobject]@{ Name = $Name; Exit = $actual; Output = ($output -join "`n") })
}

Write-FixtureArchive 'fixture.txt'
Move-Item -LiteralPath $archive -Destination ($archive + '.saved')
Check-Install 'offline-cold-cache' 1 'Offline archive missing'
Move-Item -LiteralPath ($archive + '.saved') -Destination $archive
Check-Install 'nominal-offline' 0 'ArchiveBytes'
$installed = Join-Path $fixture 'installed/fixture/1.0/fixture.txt'
$marker = Join-Path $fixture 'installed/fixture/1.0/.installation.json'
$before = (Get-Item -LiteralPath $marker).LastWriteTimeUtc
Check-Install 'idempotent' 0 'Verified existing installation'
if ((Get-Item -LiteralPath $marker).LastWriteTimeUtc -ne $before) { throw 'Valid installation was unnecessarily replaced.' }
'damaged' | Set-Content -LiteralPath $installed
Check-Install 'recover-incomplete' 0 'Incomplete installation preserved'
if ((Get-Content -LiteralPath $installed -Raw) -ne 'fixture-content') { throw 'Recovery did not restore payload.' }
'corrupt' | Set-Content -LiteralPath $archive
Check-Install 'reject-bad-checksum' 1 'checksum mismatch'
Write-FixtureArchive '../escape.txt'
Check-Install 'reject-zip-traversal' 1 'escapes'
if (Get-ChildItem -LiteralPath $fixture -Recurse -File -Filter escape.txt) { throw 'ZIP escaped staging directory.' }
$held = [IO.File]::Open((Join-Path $cache '.toolchain-install.lock'), [IO.FileMode]::OpenOrCreate,
    [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try { Check-Install 'refuse-concurrent-installer' 1 'Another installer is using' }
finally { $held.Dispose() }
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $fixture 'test-results.json') -Encoding utf8
Write-Output "INSTALLATION_CONTRACTS=PASS ($($results.Count) cases; offline fixture archives; no tools executed)"
