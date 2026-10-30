function Invoke-IsolatedToolchain {
    param([string] $DotnetPath, [string] $CacheRoot, [scriptblock] $Action)
    $sdkRoot = Split-Path $DotnetPath -Parent
    $variables = @{
        DOTNET_ROOT = $sdkRoot; DOTNET_ROOT_X64 = $sdkRoot
        DOTNET_CLI_HOME = (Join-Path $CacheRoot 'cli-home')
        NUGET_PACKAGES = (Join-Path $CacheRoot 'nuget/packages')
        NUGET_HTTP_CACHE_PATH = (Join-Path $CacheRoot 'nuget/http')
        DOTNET_BUNDLE_EXTRACT_BASE_DIR = (Join-Path $CacheRoot 'bundles')
        TEMP = (Join-Path $CacheRoot 'tmp'); TMP = (Join-Path $CacheRoot 'tmp')
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'; DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_NOLOGO = '1'; DOTNET_MULTILEVEL_LOOKUP = '0'
        DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
        DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
        DOTNET_CLI_USE_MSBUILD_SERVER = '0'; MSBUILDDISABLENODEREUSE = '1'
        PATH = ($sdkRoot + [IO.Path]::PathSeparator + $env:PATH)
    }
    foreach ($key in @('DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'DOTNET_BUNDLE_EXTRACT_BASE_DIR', 'TEMP')) {
        New-Item -ItemType Directory -Path $variables[$key] -Force | Out-Null
    }
    $saved = @{}
    try {
        foreach ($key in $variables.Keys) {
            $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
            [Environment]::SetEnvironmentVariable($key, $variables[$key], 'Process')
        }
        & $Action
    }
    finally {
        foreach ($key in $saved.Keys) {
            # PowerShell otherwise binds $null to an empty string; .NET preserves
            # that empty variable instead of removing an originally absent one.
            $value = if ($null -eq $saved[$key]) { [System.Management.Automation.Language.NullString]::Value } else { $saved[$key] }
            [Environment]::SetEnvironmentVariable($key, $value, 'Process')
        }
    }
}

Export-ModuleMember -Function Invoke-IsolatedToolchain
