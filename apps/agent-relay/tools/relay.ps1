#requires -Version 7.2
[CmdletBinding()]
param(
    [Parameter(Position=0)] [ValidateSet('send','read')] [string] $Action = 'send',
    [string] $Message,
    [guid] $Id,
    [string] $DataDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
$context = New-AppCommandContext $root (Join-Path (Split-Path $PSScriptRoot -Parent) 'app.psd1')
$assembly = Get-AppAssembly $context $context.Application.Manifest.RunProject
$data = if ($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } else { $context.DataDirectory }
$arguments = @($assembly, $Action, '--data-dir', $data)
if ($Action -eq 'send') { $arguments += @('--message', $Message) }
if ($PSBoundParameters.ContainsKey('Id')) { $arguments += @('--id', $Id.ToString('D')) }
Invoke-AppDotnet $context $arguments
