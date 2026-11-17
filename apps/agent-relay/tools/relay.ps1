#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Position=0)] [ValidateSet('send','read','connections')] [string] $Action = 'send',
    [string] $Message,
    [guid] $Id,
    [string] $DataDirectory,
    [string] $QueryFile
)
$ErrorActionPreference = 'Stop'
if ($Action -eq 'connections' -and (-not $DataDirectory -or -not [IO.Path]::IsPathFullyQualified($DataDirectory))) {
    throw 'Connection inspection requires an explicit absolute DataDirectory.'
}
if ($QueryFile -and $Action -ne 'connections') { throw 'QueryFile is only valid for connection inspection.' }
if ($Action -eq 'connections' -and ($PSBoundParameters.ContainsKey('Message') -or $PSBoundParameters.ContainsKey('Id'))) {
    throw 'Message and Id are not connection inspection options.'
}
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
$assembly = Get-AppAssembly $context $context.Application.Manifest.RunProject
$data = if ($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } else { $context.DataDirectory }
$mode = if ($Action -eq 'connections') { 'mcp-connections' } else { $Action }
$arguments = @($assembly, $mode, '--data-dir', $data)
if ($QueryFile) { $arguments += @('--input', [IO.Path]::GetFullPath($QueryFile)) }
if ($Action -eq 'send') { $arguments += @('--message', $Message) }
if ($PSBoundParameters.ContainsKey('Id')) { $arguments += @('--id', $Id.ToString('D')) }
Invoke-AppDotnet $context $arguments
