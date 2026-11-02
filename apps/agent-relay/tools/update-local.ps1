#requires -Version 7.2
[CmdletBinding()]
param([Parameter(Mandatory)][string]$DataDirectory,[string]$ReportDirectory,[switch]$Show,[string]$CodexControlSocket,[string]$ThreadId)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$data=[IO.Path]::GetFullPath($DataDirectory)
$scope=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($data.ToUpperInvariant()))).Substring(0,12)
$reports=if($ReportDirectory){[IO.Path]::GetFullPath($ReportDirectory)}else{Join-Path $root ('var/apps/agent-relay/deployment/update-'+$scope)}
& (Join-Path $PSScriptRoot 'publish-local.ps1') -DataDirectory $data -ReportDirectory $reports -Show:$Show
$arguments=@('-X','utf8','-B',(Join-Path $PSScriptRoot 'update-connections.py'),'--data',$data,'--reports',$reports)
if($CodexControlSocket){$arguments+=@('--codex-control-socket',$CodexControlSocket)}
if($ThreadId){$arguments+=@('--thread',$ThreadId)}
& python @arguments
if($LASTEXITCODE -ne 0){throw 'Host deployment completed but client registration/refresh needs attention. Read the retained report; no client success is inferred.'}
