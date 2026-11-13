param([string]$Root,[ValidateSet('Open','Initialize')][string]$Mode='Open',[switch]$Show)
$ErrorActionPreference = 'Stop'
$Root = [IO.Path]::GetFullPath($Root)
$layout = Get-Content -LiteralPath (Join-Path $Root 'portable-layout.json') -Raw | ConvertFrom-Json
if ($layout.schema -ne 1) { throw 'Unsupported portable launch layout.' }
function Resolve-LaunchPath([string]$Relative) {
    $path = [IO.Path]::GetFullPath($Relative,$Root)
    if (-not $path.StartsWith($Root.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Launch path escaped this installation.' }
    return $path
}
$data = Resolve-LaunchPath $layout.data
$pointer = Resolve-LaunchPath $layout.runtimePointer
$entry = Resolve-LaunchPath $layout.entryScript
$binary = if (Test-Path -LiteralPath $pointer) { Resolve-LaunchPath (Get-Content -LiteralPath $pointer -Raw | ConvertFrom-Json).executable } else { Resolve-LaunchPath $layout.initialExecutable }
if ($Mode -eq 'Initialize') { & $binary initialize --data-dir $data @args }
elseif (Test-Path -LiteralPath $entry) { & $entry -HostLaunch -Show:$Show }
else {
    $launch = @('tray','--data-dir',$data)
    if ($Show) { $launch += '--show' }
    & $binary @launch
}
exit $LASTEXITCODE
