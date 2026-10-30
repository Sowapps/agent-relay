[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$issues = [Collections.Generic.List[string]]::new()
$roots = @((Join-Path $root 'doc'), (Join-Path $root 'notes'), (Join-Path $root 'apps'))
$files = @($roots | ForEach-Object { Get-ChildItem -LiteralPath $_ -Recurse -File -Filter '*.md' | Where-Object { $_.FullName -notmatch '\\(bin|obj|__pycache__)\\' } }) +
    @(Get-Item -LiteralPath (Join-Path $root 'README.md'), (Join-Path $root 'AGENTS.md'))
$references = [Collections.Generic.HashSet[string]]::new()
$links = @{}
$documents = @($files | Where-Object { $_.FullName -match '\\doc\\' })
foreach ($file in $files) {
    $body = Get-Content -LiteralPath $file.FullName -Raw
    $links[$file.FullName] = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($body, '\]\((?:<([^>]+)>|([^\)]+))\)')) {
        $target = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
        if ($target -match '^(https?://|//)') { continue }
        $parts = $target -split '#', 2
        $destination = if ($parts[0]) { Join-Path $file.DirectoryName $parts[0] } else { $file.FullName }
        if (-not (Test-Path -LiteralPath $destination)) { $issues.Add("Broken link: $($file.FullName) -> $target"); continue }
        $destination = [IO.Path]::GetFullPath($destination)
        if ([IO.Path]::GetExtension($destination) -eq '.md') { $links[$file.FullName].Add($destination) }
        if ($parts.Count -eq 2 -and [IO.Path]::GetExtension($destination) -eq '.md') {
            $anchors = @(Get-Content -LiteralPath $destination | Where-Object { $_ -match '^#{1,6} ' } | ForEach-Object {
                $heading = ($_ -replace '^#{1,6}\s+', '').ToLowerInvariant() -replace '[^\p{L}\p{N}_\-\s]', ''
                $heading -replace '\s', '-'
            })
            if ($parts[1] -notin $anchors) { $issues.Add("Broken anchor: $target in $($file.FullName)") }
        }
    }
    foreach ($match in [regex]::Matches($body, 'evidence `([^`]+)`')) { [void]$references.Add($match.Groups[1].Value) }
    if ($file.FullName -match '\\doc\\') {
        if ($body -notmatch '(?m)^\*\*Utility:\*\* .+  \r?$' -or $body -notmatch '(?m)^\*\*Usage:\*\* ' -or $body -notmatch '(?m)^## ') {
            $issues.Add("Documentation structure: $($file.FullName)")
        }
    }
}
$reached = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$pending = [Collections.Generic.Queue[string]]::new()
$pending.Enqueue((Join-Path $root 'doc/README.md'))
while ($pending.Count) {
    $current = $pending.Dequeue()
    if (-not $reached.Add($current) -or -not $links.ContainsKey($current)) { continue }
    foreach ($linked in $links[$current]) { $pending.Enqueue($linked) }
}
foreach ($document in $documents) {
    if (-not $reached.Contains($document.FullName)) { $issues.Add("Unreachable document: $($document.FullName)") }
}
foreach ($source in @((Get-Item -LiteralPath (Join-Path $root 'dev.ps1'))) +
        @(Get-ChildItem -LiteralPath (Join-Path $root 'tools'), (Join-Path $root 'apps') -Recurse -File |
            Where-Object Extension -in '.ps1', '.psm1', '.psd1')) {
    $parseTokens = $null
    $parseErrors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($source.FullName, [ref]$parseTokens, [ref]$parseErrors)
    foreach ($parseError in $parseErrors) { $issues.Add("PowerShell syntax: $($source.FullName): $($parseError.Message)") }
}
foreach ($directory in @((Get-Item -LiteralPath (Join-Path $root 'doc'))) +
        @($documents | ForEach-Object DirectoryName | Sort-Object -Unique | ForEach-Object { Get-Item -LiteralPath $_ })) {
    if (-not (Test-Path -LiteralPath (Join-Path $directory.FullName 'README.md'))) { $issues.Add("Missing index: $($directory.FullName)") }
}
foreach ($id in $references) {
    if (-not (Test-Path -LiteralPath (Join-Path $root ('notes/evidence/' + $id + '.md')))) { $issues.Add("Missing evidence: $id") }
}
foreach ($record in Get-ChildItem -LiteralPath (Join-Path $root 'notes/evidence') -File -Filter '*.md' | Where-Object Name -ne 'README.md') {
    if (-not $references.Contains($record.BaseName)) { $issues.Add("Unreferenced evidence: $($record.Name)") }
}
if ($issues.Count) {
    $issues | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit 1
}
Write-Output 'DOCUMENT_LINKS_ANCHORS_INDEXES_EVIDENCE=PASS'
Write-Output 'DOCUMENT_REACHABILITY_POWERSHELL_SYNTAX=PASS'
& (Join-Path $PSScriptRoot 'tests/entrypoints.ps1')
& (Join-Path $PSScriptRoot 'tests/toolchain-installation.ps1')
& (Join-Path $PSScriptRoot 'tests/toolchain-environment.ps1')
Write-Output 'WORKSPACE_VERIFICATION=PASS'
exit 0
