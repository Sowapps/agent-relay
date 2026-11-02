function Get-StagedApplication {
    param([string] $Assembly, [string] $RuntimeRoot, [string] $InstallationRoot)
    $source = Split-Path $Assembly -Parent
    $files = @(Get-ChildItem -LiteralPath $source -Recurse -File | Sort-Object FullName | ForEach-Object {
        [pscustomobject]@{ Path = [IO.Path]::GetRelativePath($source, $_.FullName); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    $identity = if($InstallationRoot){ [ordered]@{Root=[IO.Path]::GetFullPath($InstallationRoot);Files=$files} | ConvertTo-Json -Depth 5 -Compress }else{$files | ConvertTo-Json -Compress}
    $bytes = [Text.Encoding]::UTF8.GetBytes($identity)
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
        if($InstallationRoot){
            @{version=1;root=[IO.Path]::GetRelativePath($stage,[IO.Path]::GetFullPath($InstallationRoot))} | ConvertTo-Json |
                Set-Content -LiteralPath (Join-Path $stage 'installation.json') -Encoding utf8
            $files=@(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
                [pscustomobject]@{Path=[IO.Path]::GetRelativePath($stage,$_.FullName);Hash=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
            })
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
