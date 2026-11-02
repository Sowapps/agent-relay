function Invoke-RelayNative {
    param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string[]]$Arguments)
    $start=[Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    foreach($argument in $Arguments){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start)
    try {
        $output=$process.StandardOutput.ReadToEndAsync();$errors=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(30000)){$process.Kill();throw 'Owned native preparation command timed out.'}
        $text=$output.GetAwaiter().GetResult();$errorText=$errors.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0){throw ('Native preparation failed: '+$errorText)}
        return $text.Trim()
    } finally {$process.Dispose()}
}

function New-StagedRelayRuntime {
    param($Context)
    $root=$Context.RepositoryRoot
    $var=Join-Path $root 'var/apps/agent-relay'
    $candidate=Join-Path $var ('preparing/'+[guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $candidate | Out-Null
    $arguments=@('publish','src/AgentRelay.Windows/AgentRelay.Windows.csproj','-c','Release','-r','win-x64','--self-contained','true','--disable-build-servers','-p:RestoreLockedMode=true','-p:UseSharedCompilation=false','-p:NuGetLockFilePath=packages.win-x64.lock.json','-p:RuntimeFrameworkVersion=10.0.14','-p:TargetLatestRuntimePatch=false','-p:NuGetAudit=false','-o',$candidate)
    $offline=Join-Path $Context.CacheRoot 'offline.NuGet.Config'
    if(Test-Path -LiteralPath $offline){$arguments+=@('--configfile',$offline)}
    Invoke-AppDotnet $Context $arguments | Out-Host
    $native=Join-Path $candidate 'AgentRelay.Windows.exe'
    $contract=Invoke-RelayNative -Executable $native -Arguments @('mcp-contract','--data-dir',$candidate)
    $identity=$contract | ConvertFrom-Json
    if(-not $identity.version -or -not $identity.contractHash){throw 'Candidate MCP contract could not be identified.'}
    $contract | Set-Content -LiteralPath (Join-Path $candidate 'mcp-contract.json') -Encoding utf8
    $assembly=Get-StagedApplication (Join-Path $candidate 'AgentRelay.Windows.dll') (Join-Path $var 'run') $root
    # This candidate is a freshly created owned build output, never a live runtime.
    $owned=[IO.Path]::GetFullPath((Join-Path $var 'preparing'))+[IO.Path]::DirectorySeparatorChar
    if(-not [IO.Path]::GetFullPath($candidate).StartsWith($owned,[StringComparison]::OrdinalIgnoreCase)){throw 'Candidate cleanup escaped preparing root.'}
    Remove-Item -LiteralPath $candidate -Recurse -Force
    return @{assembly=$assembly;executable=(Join-Path (Split-Path $assembly -Parent) 'AgentRelay.Windows.exe');distribution='portable-win-x64'}
}
Export-ModuleMember -Function New-StagedRelayRuntime,Invoke-RelayNative
