$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
Import-Module (Join-Path $root 'tools/Workspace.psm1') -Force
$installed = Import-PowerShellDataFile -LiteralPath (Join-Path $root 'local/toolchains/dotnet.psd1')
$fixture = Join-Path $root ('local/tool-tests/qualification-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $fixture 'apps/fixture'
$data = Join-Path $fixture 'local/toolchains/fixture'
New-Item -ItemType Directory -Path $app, $data, (Join-Path $fixture 'tools') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tools/qualification') -Destination (Join-Path $fixture 'tools') -Recurse
Copy-Item -LiteralPath (Join-Path $root 'tools/qualify-dotnet.ps1'), (Join-Path $root 'tools/ToolchainEnvironment.psm1') `
    -Destination (Join-Path $fixture 'tools')
Copy-Item -LiteralPath (Join-Path $root 'apps/agent-relay/global.json') -Destination $app
Copy-Item -LiteralPath (Join-Path $root 'local/toolchains/dotnet.psd1') -Destination (Join-Path $fixture 'local/toolchains')
$application = [pscustomobject]@{ Id = 'fixture'; Root = $app; Manifest = @{ RequiredSdkMajor = 10; Solution = 'none.slnx' } }
$record = [ordered]@{
    SchemaVersion = 1; Qualified = $true; CompilationPassed = $true; ExecutionPassed = $true
    NominalTestPassed = $true; InteractiveDebuggingDemonstrated = $true
    DesktopPumpPassed = $true; TrayAddPassed = $true; TrayRemovePassed = $true
    Fingerprint = (Get-QualificationFingerprint $fixture $application $installed.DotnetPath $installed.DebuggerPath)
}
$path = Join-Path $data 'qualification.json'
$record | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8
if ((Get-DotnetStatus $fixture $application).QualificationStatus -ne 'qualified') { throw 'Matching fixture report was rejected.' }
Add-Content -LiteralPath (Join-Path $fixture 'tools/qualification/dotnet/Program.cs') -Value '// changed fixture source'
if ((Get-DotnetStatus $fixture $application).QualificationStatus -ne 'stale') { throw 'Changed source retained qualification.' }
$record.Qualified = $false
$record | ConvertTo-Json | Set-Content -LiteralPath $path -Encoding utf8
if ((Get-DotnetStatus $fixture $application).QualificationStatus -ne 'not-qualified') { throw 'Failed report retained qualification.' }
'{"partial":' | Set-Content -LiteralPath $path -Encoding utf8
if ((Get-DotnetStatus $fixture $application).QualificationStatus -ne 'invalid-record') { throw 'Interrupted report was not classified.' }
Write-Output 'QUALIFICATION_RECORD=PASS (matching report, changed source, failed report, interrupted JSON; isolated fixtures)'
