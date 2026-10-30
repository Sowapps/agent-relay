$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $root 'tools/ToolchainEnvironment.psm1') -Force
$before = [Environment]::GetEnvironmentVariables('Process')
$userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
$machinePath = [Environment]::GetEnvironmentVariable('PATH', 'Machine')
$cache = Join-Path $root ('local/tool-tests/environment-' + [guid]::NewGuid().ToString('N'))
$fakeSdk = Join-Path $cache 'sdk/dotnet.exe'
$normal = $false
Invoke-IsolatedToolchain $fakeSdk $cache {
    if ($env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH -ne 'false' -or $env:DOTNET_CLI_HOME -notlike ($cache + '*')) {
        throw 'Isolation environment not applied.'
    }
    $script:normal = $true
}
if (-not $normal) { throw 'Environment callback was not called.' }
try {
    Invoke-IsolatedToolchain $fakeSdk $cache { throw 'fixture-error' }
    throw 'Expected callback failure was swallowed.'
}
catch { if ($_.Exception.Message -ne 'fixture-error') { throw } }
$after = [Environment]::GetEnvironmentVariables('Process')
foreach ($key in @($before.Keys) + @($after.Keys) | Sort-Object -Unique) {
    if ($before[$key] -cne $after[$key]) { throw "Environment not restored: $key" }
}
if ($userPath -cne [Environment]::GetEnvironmentVariable('PATH', 'User') -or
    $machinePath -cne [Environment]::GetEnvironmentVariable('PATH', 'Machine')) { throw 'Persistent PATH changed.' }
Write-Output 'TOOLCHAIN_ENVIRONMENT=PASS (normal callback, failed callback, process restoration, persistent PATH unchanged)'
