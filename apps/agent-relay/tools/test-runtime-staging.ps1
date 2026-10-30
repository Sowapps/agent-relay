$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'RuntimeStaging.psm1') -Force
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$fixture = Join-Path $root ('local/apps/agent-relay/tests/staging-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $fixture 'source'
$runtime = Join-Path $fixture 'run'
New-Item -ItemType Directory -Path $source -Force | Out-Null
$assembly = Join-Path $source 'app.dll'
'first' | Set-Content -LiteralPath $assembly -Encoding utf8
$first = Get-StagedApplication $assembly $runtime
$second = Get-StagedApplication $assembly $runtime
if ($first -ne $second -or @(Get-ChildItem -LiteralPath $runtime -Directory).Count -ne 1) { throw 'Unchanged runtime was duplicated.' }
$loaded = [IO.File]::Open($first, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    'rebuilt' | Set-Content -LiteralPath $assembly -Encoding utf8
    $rebuilt = Get-StagedApplication $assembly $runtime
    if ($rebuilt -eq $first -or (Get-Content -LiteralPath $first).Trim() -ne 'first' -or
        (Get-Content -LiteralPath $rebuilt).Trim() -ne 'rebuilt') { throw 'Rebuild replaced a running version.' }
}
finally { $loaded.Dispose() }
Write-Output 'RUNTIME_STAGING=PASS (content reuse; locked running copy preserved; rebuilt output staged separately)'
