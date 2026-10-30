[CmdletBinding()]
param([Parameter(Mandatory)] [string] $Snapshot)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Import-Module (Join-Path $PSScriptRoot 'Workspace.psm1') -Force
$directory = Resolve-ChildPath $root $Snapshot
$before = @(Get-Content -LiteralPath (Join-Path $directory 'before-files.json') -Raw | ConvertFrom-Json)
$map = @(Get-Content -LiteralPath (Join-Path $directory 'file-map.json') -Raw | ConvertFrom-Json)
$changes = [Collections.Generic.List[object]]::new()
$proofCount = 0
$archive = [IO.Compression.ZipFile]::OpenRead((Join-Path $directory 'before-layout.zip'))
try {
    foreach ($entry in $before) {
        $archived = $archive.GetEntry($entry.Path.Replace('\', '/'))
        if ($null -eq $archived) { throw "Original missing from archive: $($entry.Path)" }
        $stream = $archived.Open()
        try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
        finally { $stream.Dispose() }
        if ($hash -ne $entry.Sha256) { throw "Archive content hash differs: $($entry.Path)" }
    }
}
finally { $archive.Dispose() }
foreach ($entry in $before) {
    $normalized = $entry.Path.Replace('/', '\')
    $mapped = @($map | Where-Object { $_.Before.Replace('/', '\') -eq $normalized })
    if ($mapped.Count -ne 1) { throw "Missing or duplicate mapping: $($entry.Path)" }
    $path = Resolve-ChildPath $root $mapped[0].After
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Original file lost: $($entry.Path)" }
    $same = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $entry.Sha256
    if ($normalized -like 'notes\evidence\*' -and $normalized -ne 'notes\evidence\README.md') {
        if (-not $same) { throw "Existing evidence changed: $($entry.Path)" }
        $proofCount++
    }
    if (-not $same) { $changes.Add([pscustomobject]@{ Before = $entry.Path; After = $mapped[0].After }) }
}
$report = [ordered]@{
    OriginalFilesPresent = $before.Count
    ArchivedOriginalHashesVerified = $before.Count
    ExistingProofsByteIdentical = $proofCount
    ChangedOriginalFiles = @($changes.ToArray())
    ArchiveSha256 = (Get-FileHash -LiteralPath (Join-Path $directory 'before-layout.zip') -Algorithm SHA256).Hash
}
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $directory 'audit.json') -Encoding utf8
Write-Output "MIGRATION=PASS ($($before.Count) original files present; $proofCount existing proofs byte-identical)"
Write-Output "Changed original files: $($changes.Count); review audit.json alongside the source archive."
