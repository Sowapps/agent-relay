function Get-StagedApplication {
    param([string] $Assembly, [string] $RuntimeRoot)
    $source = Split-Path $Assembly -Parent
    $files = @(Get-ChildItem -LiteralPath $source -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($source, $_.FullName); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $bytes = [Text.Encoding]::UTF8.GetBytes(($files | ConvertTo-Json -Compress))
    $fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes))
    $destination = Join-Path $RuntimeRoot $fingerprint
    $marker = Join-Path $destination 'runtime.json'
    if (-not (Test-Path -LiteralPath $marker)) {
        New-Item -ItemType Directory -Path $RuntimeRoot -Force | Out-Null
        $stage = Join-Path $RuntimeRoot ('.staging-' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $stage | Out-Null
        Get-ChildItem -LiteralPath $source -Force | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stage -Recurse }
        foreach ($file in $files) {
            if ((Get-FileHash -LiteralPath (Join-Path $stage $file.Path) -Algorithm SHA256).Hash -ne $file.Hash) { throw 'Runtime copy verification failed.' }
        }
        [ordered]@{ Fingerprint = $fingerprint; Files = $files } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath (Join-Path $stage 'runtime.json') -Encoding utf8
        $rootPath = [IO.Path]::GetFullPath($RuntimeRoot).TrimEnd('\') + '\'
        foreach ($target in @($stage, $destination)) {
            if (-not [IO.Path]::GetFullPath($target).StartsWith($rootPath, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime move escaped its root.' }
        }
        [IO.Directory]::Move($stage, $destination)
    }
    return Join-Path $destination ([IO.Path]::GetFileName($Assembly))
}

Export-ModuleMember -Function Get-StagedApplication
