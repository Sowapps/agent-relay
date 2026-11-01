#requires -Version 7.2
[CmdletBinding()]
param([string] $DataDirectory, [string] $OutputDirectory, [switch] $PrepareRuntime, [switch] $CodexProject)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
Import-Module (Join-Path $PSScriptRoot 'RuntimeStaging.psm1') -Force
Import-Module (Join-Path $PSScriptRoot 'NativeRuntime.psm1') -Force
$data = if ($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } else { $context.DataDirectory }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else {$data}
$existingPointer = Join-Path $data 'mcp-runtime.json'
$pointer=if($PrepareRuntime){New-StagedRelayRuntime $context}elseif(Test-Path -LiteralPath $existingPointer){Get-Content -LiteralPath $existingPointer -Raw | ConvertFrom-Json -AsHashtable}else{throw 'Prepare the runtime explicitly first (-PrepareRuntime), then export its connection.'}
if ($PrepareRuntime -and (Test-Path -LiteralPath $existingPointer)) {
    $previousPointer = Get-Content -LiteralPath $existingPointer -Raw | ConvertFrom-Json
    if ($previousPointer.connectionId) { $pointer.connectionId = $previousPointer.connectionId }
}
if(-not $pointer.executable -or -not (Test-Path -LiteralPath $pointer.executable)){throw 'Export requires a prepared native runtime.'}
New-Item -ItemType Directory -Path $data -Force | Out-Null
New-Item -ItemType Directory -Path $output -Force | Out-Null
if($PrepareRuntime){
$pointer | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $data 'mcp-runtime.pending.json') -Encoding utf8
Move-Item -LiteralPath (Join-Path $data 'mcp-runtime.pending.json') -Destination (Join-Path $data 'mcp-runtime.json') -Force
}
$arguments = @('mcp', '--data-dir', $data)
if($pointer.connectionId){$arguments+=@('--connection-id',$pointer.connectionId)}
@{ mcpServers = @{ agent_relay = @{ command = $pointer.executable; args = $arguments } } } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'mcp.windows.json') -Encoding utf8
$quote = { param($value) ConvertTo-Json -InputObject $value -Compress }
$command = & $quote $pointer.executable
$encodedArguments = & $quote $arguments
$toml = "[mcp_servers.agent_relay]`ncommand = $command`nargs = $encodedArguments`nrequired = true`ntool_timeout_sec = 180`n"
$toml |
    Set-Content -LiteralPath (Join-Path $output 'mcp.codex.toml') -Encoding utf8
if ($CodexProject) {
    $directory = Join-Path $root '.codex'
    $target = Join-Path $directory 'config.toml'
    if (Test-Path -LiteralPath $target) {
        $existing = Get-Content -LiteralPath $target -Raw
        $tables = @([regex]::Matches($existing, '(?m)^\s*\[([^\]]+)\]') | ForEach-Object { $_.Groups[1].Value })
        $unknown = @($existing -split "`r?`n" | Where-Object { $_ -notmatch '^\s*(#.*|\[mcp_servers\.agent_relay\]|command\s*=.*|args\s*=.*|required\s*=.*|tool_timeout_sec\s*=.*)?\s*$' })
        if ($tables.Count -ne 1 -or $tables[0] -ne 'mcp_servers.agent_relay' -or $unknown.Count) {
            throw 'Project config contains other settings. Merge the generated fragment explicitly; it was not overwritten.'
        }
        Copy-Item -LiteralPath $target -Destination (Join-Path $data 'mcp.project.before.toml') -Force
    }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $toml | Set-Content -LiteralPath $target -Encoding utf8
    Write-Output 'Project-local Codex MCP configuration updated. Global trust and client reload are separate steps.'
}
Write-Output "MCP fragments exported to $output for runtime data $data. No global client configuration changed."
