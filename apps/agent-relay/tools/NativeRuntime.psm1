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
    $assembly=Get-StagedApplication (Join-Path $candidate 'AgentRelay.Windows.dll') (Join-Path $var 'run') $root
    # This candidate is a freshly created owned build output, never a live runtime.
    $owned=[IO.Path]::GetFullPath((Join-Path $var 'preparing'))+[IO.Path]::DirectorySeparatorChar
    if(-not [IO.Path]::GetFullPath($candidate).StartsWith($owned,[StringComparison]::OrdinalIgnoreCase)){throw 'Candidate cleanup escaped preparing root.'}
    Remove-Item -LiteralPath $candidate -Recurse -Force
    return @{assembly=$assembly;executable=(Join-Path (Split-Path $assembly -Parent) 'AgentRelay.Windows.exe');distribution='portable-win-x64'}
}
Export-ModuleMember -Function New-StagedRelayRuntime
