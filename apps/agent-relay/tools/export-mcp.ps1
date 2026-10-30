#requires -Version 7.2
[CmdletBinding()]
param([string] $DataDirectory, [switch] $CodexProject)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
$assembly = Get-AppAssembly $context $context.Application.Manifest.RunProject
Import-Module (Join-Path $PSScriptRoot 'RuntimeStaging.psm1') -Force
$assembly = Get-StagedApplication $assembly (Join-Path $root 'local/apps/agent-relay/run')
$data = if ($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } else { $context.DataDirectory }
New-Item -ItemType Directory -Path $data -Force | Out-Null
$python = (Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$pointer = @{ dotnet = $context.Status.DotnetPath; assembly = $assembly; python = $python; trayLauncher = (Join-Path $PSScriptRoot 'launch-installed.py') }
$existingPointer = Join-Path $data 'mcp-runtime.json'
if (Test-Path -LiteralPath $existingPointer) {
    $previousPointer = Get-Content -LiteralPath $existingPointer -Raw | ConvertFrom-Json
    if ($previousPointer.connectionId) { $pointer.connectionId = $previousPointer.connectionId }
}
$pointer | ConvertTo-Json |
    Set-Content -LiteralPath (Join-Path $data 'mcp-runtime.pending.json') -Encoding utf8
Move-Item -LiteralPath (Join-Path $data 'mcp-runtime.pending.json') -Destination (Join-Path $data 'mcp-runtime.json') -Force
$arguments = @('-B', (Join-Path $PSScriptRoot 'mcp-entry.py'), '--data-dir', $data)
@{ mcpServers = @{ agent_relay = @{ command = $python; args = $arguments } } } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $data 'mcp.windows.json') -Encoding utf8
$quote = { param($value) ConvertTo-Json -InputObject $value -Compress }
$command = & $quote $python
$encodedArguments = & $quote $arguments
$toml = "[mcp_servers.agent_relay]`ncommand = $command`nargs = $encodedArguments`nrequired = true`ntool_timeout_sec = 180`n"
$toml |
    Set-Content -LiteralPath (Join-Path $data 'mcp.codex.toml') -Encoding utf8
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
Write-Output "MCP fragments exported to $data. No global client configuration changed."
