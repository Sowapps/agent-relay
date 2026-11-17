#requires -Version 7.2
<#
.SYNOPSIS
Retire a disconnected MCP transport whose process ID belongs to a different Windows process.
.DESCRIPTION
Preview by default. Use only for a retained update waiting for transport acknowledgements.
The original presence and observed Windows identity are retained beside the update journal.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $DataDirectory,
    [Parameter(Mandatory)][guid] $InstanceId,
    [switch] $Apply
)
$ErrorActionPreference = 'Stop'

function Get-OrdinaryItem([string] $Path) {
    $item = Get-Item -LiteralPath $Path -Force
    while ($item) {
        if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Reparse point in scope: $($item.FullName)" }
        $item = if ($item -is [IO.DirectoryInfo]) { $item.Parent } else { $item.Directory }
    }
}

$data = (Get-Item -LiteralPath $DataDirectory).FullName
if ((Split-Path $data -Leaf) -ne 'default') { throw 'Specify the retained portable default data directory.' }
$update = Join-Path (Split-Path $data -Parent) 'update'
$journal = Join-Path $update 'current.json'
Get-OrdinaryItem $journal
$transaction = Get-Content -LiteralPath $journal -Raw | ConvertFrom-Json -AsHashtable
if ($transaction.data -ne $data -or $transaction.phase -notin @('awaiting_transports','files_restored') -or
    $transaction.id -cnotmatch '^[0-9a-f]{12}$' -or $transaction.activation.transportInstances -notcontains $InstanceId.ToString('D')) {
    throw 'The selected transport is not required by this paused update.'
}

$selectionPath = Join-Path $data 'transport-selection.json'
Get-OrdinaryItem $selectionPath
$selection = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json -AsHashtable
$relayImage = [IO.Path]::GetFileName($selection.gateway.executable)
if ([string]::IsNullOrWhiteSpace($relayImage)) { throw 'Cannot identify the selected transport image.' }
$recordPath = Join-Path $data "transport/instances/$($InstanceId.ToString('N')).json"
Get-OrdinaryItem $recordPath
$content = [IO.File]::ReadAllText($recordPath)
$record = $content | ConvertFrom-Json -AsHashtable
if ($record.instanceId -ne $InstanceId.ToString('D') -or $record.state -eq 'closed' -or !$record.processStartedAt) {
    throw 'The selected presence is closed or its original process identity is incomplete.'
}
$recordPid = [int]$record.processId
if ($recordPid -le 0) { throw 'Invalid transport process ID.' }
$recordStart = [datetimeoffset]$record.processStartedAt
$os = Get-CimInstance Win32_Process -Filter "ProcessId = $recordPid"
if (!$os -or [string]::IsNullOrWhiteSpace($os.Name) -or $os.Name -eq $relayImage -or !$os.CreationDate -or
    [datetimeoffset]$os.CreationDate -le $recordStart.AddSeconds(2)) {
    throw 'Windows did not prove that a newer, unrelated process owns this PID.'
}
$proof = [ordered]@{transactionId=$transaction.id; instanceId=$record.instanceId; processId=$recordPid;
    recordedStart=$recordStart; observedStart=[datetimeoffset]$os.CreationDate; observedImage=$os.Name; applied=[bool]$Apply}

if ($Apply) {
    $audit = Join-Path $update "$($transaction.id)/transport-reconciliation"
    $parent = Split-Path $audit -Parent
    Get-OrdinaryItem $parent
    if (Test-Path -LiteralPath $audit) { Get-OrdinaryItem $audit }
    [IO.Directory]::CreateDirectory($audit) | Out-Null
    $auditFile = Join-Path $audit "$($InstanceId.ToString('N')).json"
    $record.state = 'closed'
    $pending = "$recordPath.retire-$([guid]::NewGuid().ToString('N'))"
    try {
        $evidence = [ordered]@{proof=$proof; original=$content} | ConvertTo-Json -Depth 8
        if (Test-Path -LiteralPath $auditFile) {
            Get-OrdinaryItem $auditFile
            $existing = Get-Content -LiteralPath $auditFile -Raw | ConvertFrom-Json -AsHashtable
            if ($existing.original -cne $content) { throw 'The prior before-image differs.' }
        } else {
            $stream = [IO.File]::Open($auditFile, [IO.FileMode]::CreateNew)
            try { $stream.Write([Text.Encoding]::UTF8.GetBytes($evidence)); $stream.Flush($true) } finally { $stream.Dispose() }
        }
        [IO.File]::WriteAllText($pending, ($record | ConvertTo-Json -Depth 12 -Compress))
        if ([IO.File]::ReadAllText($recordPath) -cne $content) { throw 'The presence changed during reconciliation.' }
        [IO.File]::Move($pending, $recordPath, $true)
    } finally {
        if (Test-Path -LiteralPath $pending) { Remove-Item -LiteralPath $pending }
    }
}
$proof | ConvertTo-Json -Compress
