$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$fixture = Join-Path $root ('local/tool-tests/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path (Join-Path $fixture 'tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'dev.ps1') -Destination $fixture
Copy-Item -LiteralPath (Join-Path $root 'tools/Workspace.psm1') -Destination (Join-Path $fixture 'tools')
"@{ Applications = @{ alpha = 'apps/alpha/app.psd1'; beta = 'apps/beta/app.psd1' } }" |
    Set-Content -LiteralPath (Join-Path $fixture 'workspace.psd1') -Encoding utf8
foreach ($id in @('alpha', 'beta')) {
    $app = Join-Path $fixture ('apps/' + $id)
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    "@{ Id = '$id'; Driver = 'driver.ps1' }" | Set-Content -LiteralPath (Join-Path $app 'app.psd1') -Encoding utf8
    @'
param($Action, $WorkspaceRoot, $ManifestPath)
$id = (Import-PowerShellDataFile -LiteralPath $ManifestPath).Id
if ($Action -eq 'doctor') {
    [pscustomobject]@{ Application = $id; Ready = $true; Summary = 'fixture-ready' }
    return
}
Write-Output ($id + ':' + $Action)
exit 0
'@ | Set-Content -LiteralPath (Join-Path $app 'driver.ps1') -Encoding utf8
}
$shell = (Get-Command pwsh.exe -CommandType Application | Select-Object -First 1).Source
$count = 0
function Assert-Command {
    param([string[]] $Arguments, [int] $ExpectedExit, [string] $ExpectedText)
    $output = & $shell -NoProfile -File (Join-Path $fixture 'dev.ps1') @Arguments 2>&1
    $exit = $LASTEXITCODE
    if ($exit -ne $ExpectedExit -or ($output -join "`n") -notlike ('*' + $ExpectedText + '*')) {
        throw "Entry point failed: $Arguments; exit=$exit; output=$output"
    }
    $script:count++
}
Assert-Command @('build', '-App', 'alpha') 0 'alpha:build'
Assert-Command @('test', '-App', 'beta') 0 'beta:test'
Assert-Command @('run', '-App', 'alpha') 0 'alpha:run'
Assert-Command @('doctor', '-Json') 0 'fixture-ready'
Assert-Command @('build') 64 'Select an application'
Assert-Command @('setup') 64 'Select an application'
Assert-Command @('run', '-App', 'unknown') 64 'Unknown application'
Assert-Command @('run', '-App', '../alpha') 64 'Invalid application'
# An apparently valid identifier must not let a registry entry escape the workspace.
"@{ Applications = @{ alpha = '../outside/app.psd1' } }" |
    Set-Content -LiteralPath (Join-Path $fixture 'workspace.psd1') -Encoding utf8
Assert-Command @('run', '-App', 'alpha') 64 'escapes'
Write-Output ("ENTRYPOINT_CONTRACTS=PASS ($count cases; two isolated fixture applications)")
# Keep the tiny fixture and process outputs addressable until the operator reviews
# the work. They are ignored local data, not registered real applications.
