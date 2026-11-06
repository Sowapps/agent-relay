#requires -Version 7.2
<#
Utility: Build, verify or publish the versioned portable Windows release.
Usage: Package, then Verify with -Directory from package output; Publish requires a clean tagged source tree and that exact verified archive. External publication requires explicit authorization.
#>
[CmdletBinding()]
param([Parameter(Mandatory)][ValidateSet('Lock','Package','Verify','Tag','PlanPublish','Publish')][string]$Action, [string]$Directory, [string]$Repository)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$root=Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$app=Split-Path $PSScriptRoot -Parent
$version=([xml](Get-Content -LiteralPath (Join-Path $app 'Version.props') -Raw)).Project.PropertyGroup.Version
if($version -notmatch '^\d+\.\d+\.\d+(?:-rc\.[1-9]\d*)?$'){throw 'Only stable X.Y.Z and RC X.Y.Z-rc.N versions are supported.'}
$tag='v'+$version
$python=(Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
if($Action -in @('Lock','Package')){
    if($Directory){throw 'Package chooses a fresh directory; use its output for later actions.'}
    Import-Module (Join-Path $PSScriptRoot 'AppCommands.psm1') -Force
    $context=New-AppCommandContext $root (Join-Path $app 'app.psd1')
    $releaseProperties=@('-p:NuGetLockFilePath=packages.win-x64.lock.json','-p:RuntimeFrameworkVersion=10.0.14','-p:TargetLatestRuntimePatch=false')
    if($Action -eq 'Lock'){
        Invoke-AppDotnet $context (@('restore','src/AgentRelay.Windows/AgentRelay.Windows.csproj','-r','win-x64','-p:SelfContained=true','--force-evaluate','-p:RestoreLockedMode=false')+$releaseProperties)
        Write-Output 'Review and commit packages.win-x64.lock.json for every project before Package.'
        return
    }
    $Directory=Join-Path $root ('local/apps/agent-relay/releases/'+$tag+'/'+[guid]::NewGuid().ToString('N'))
    $payload=Join-Path $Directory 'payload';$binary=Join-Path $payload 'app'
    New-Item -ItemType Directory -Path $binary -Force | Out-Null
    Invoke-AppDotnet $context (@('publish','src/AgentRelay.Windows/AgentRelay.Windows.csproj','-c','Release','-r','win-x64','--self-contained','true','--disable-build-servers','-p:RestoreLockedMode=true','-p:UseSharedCompilation=false','-p:DebugType=None','-p:DebugSymbols=false','-o',$binary)+$releaseProperties)
    Copy-Item -Path (Join-Path $app 'packaging/*') -Destination $payload -Recurse
    New-Item -ItemType Directory -Path (Join-Path $payload 'tools') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $app 'doc/using/examples/new-project-request.ps1') -Destination (Join-Path $payload 'tools/new-project-request.ps1')
    & $python -X utf8 -B (Join-Path $PSScriptRoot 'package-release.py') --payload $payload --output $Directory --version $version --cache (Join-Path $root 'local/toolchains/cache/nuget/packages') --sdk (Split-Path $context.Status.DotnetPath -Parent)
    if($LASTEXITCODE -ne 0){throw 'Archive packaging failed.'}
    Write-Output "RELEASE_DIRECTORY=$Directory"
    return
}
if(-not $Directory){throw 'Supply the exact package Directory.'}
$Directory=[IO.Path]::GetFullPath($Directory)
$ownedRoot=[IO.Path]::GetFullPath((Join-Path $root 'local/apps/agent-relay/releases'))+[IO.Path]::DirectorySeparatorChar
if(-not $Directory.StartsWith($ownedRoot,[StringComparison]::OrdinalIgnoreCase)){throw 'Release artifacts must remain under application local/releases.'}
if($Action -eq 'Verify'){
    & $python -X utf8 -B (Join-Path $PSScriptRoot 'verify-release.py') --directory $Directory --version $version
    if($LASTEXITCODE -ne 0){throw 'Extracted archive verification failed.'}
    return
}
if($Action -in @('PlanPublish','Publish') -and $Repository -ne 'sowapps/agent-relay'){
    throw 'Select the authorized publication target explicitly with -Repository sowapps/agent-relay.'
}
$verification=Get-Content -LiteralPath (Join-Path $Directory 'verification.json') -Raw | ConvertFrom-Json
$archive=Join-Path $Directory ('agent-relay-'+$version+'-win-x64.zip')
if(-not $verification.passed -or (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $verification.archiveSha256){throw 'Exact archive is not verified.'}
$visualPath=Join-Path $Directory 'visual-verification.json'
if(-not (Test-Path -LiteralPath $visualPath)){throw 'Record actual extracted application visual verification before publication.'}
$visual=Get-Content -LiteralPath $visualPath -Raw | ConvertFrom-Json
if(-not $visual.passed -or $visual.archiveSha256 -ne $verification.archiveSha256){throw 'Visual verification does not match the archive.'}
$manifest=Get-Content -LiteralPath (Join-Path $Directory 'payload/release-manifest.json') -Raw | ConvertFrom-Json
if($manifest.version -ne $version -or $verification.version -ne $version){throw 'Package version differs from the source version.'}
foreach($inputFile in $manifest.sourceInputs.PSObject.Properties){
    if((Get-FileHash -LiteralPath (Join-Path $root $inputFile.Name) -Algorithm SHA256).Hash.ToLowerInvariant() -ne $inputFile.Value){throw ('Source changed after packaging: '+$inputFile.Name)}
}
Push-Location -LiteralPath $root
try{
    $dirty=& git status --porcelain
    if($LASTEXITCODE -ne 0 -or $dirty){throw 'Publication needs a clean Git tree.'}
    $head=& git rev-parse HEAD; if($LASTEXITCODE -ne 0){throw 'Missing source commit.'}
    if($Action -eq 'Tag'){
        & git show-ref --verify --quiet ('refs/tags/'+$tag)
        if($LASTEXITCODE -eq 0){
            $existing=& git rev-parse ($tag+'^{commit}')
            if($existing -ne $head){throw 'Existing tag belongs to another commit; it was preserved.'}
            Write-Output "Tag $tag already identifies HEAD."
        }else{
            & git tag -a $tag -m ('Agent Relay '+$tag)
            if($LASTEXITCODE -ne 0){throw 'Tag creation failed.'}
        }
        return
    }
    $tagCommit=& git rev-parse ($tag+'^{commit}');if($LASTEXITCODE -ne 0 -or $tagCommit -ne $head){throw 'Create/review the release tag at HEAD before publishing.'}
    $main=& git rev-parse refs/heads/main
    if($LASTEXITCODE -ne 0 -or $main -ne $head){throw 'Publication requires main and the release tag to identify the reviewed commit.'}
    $url='https://github.com/'+$Repository
    $pushUrls=@(& git remote get-url --push --all origin)
    $expectedPushUrl='\A(?:https://github\.com/|git@github\.com:)' + [regex]::Escape($Repository) + '(?:\.git)?\z'
    if($LASTEXITCODE -ne 0 -or $pushUrls.Count -ne 1 -or $pushUrls[0] -notmatch $expectedPushUrl){
        throw 'The single origin push target must match the selected GitHub repository.'
    }
    $repoJson=& gh repo view $url --json isPrivate,nameWithOwner,url
    if($LASTEXITCODE -ne 0){throw 'Cannot verify the selected GitHub repository.'}
    $repo=$repoJson | ConvertFrom-Json
    if($repo.nameWithOwner -ne $Repository -or $repo.url -ne $url -or $repo.isPrivate -isnot [bool]){
        throw 'GitHub returned a different repository or unknown visibility.'
    }
    $existingRelease=@(& gh api --include --hostname github.com ('repos/'+$Repository+'/releases/tags/'+$tag) 2>$null)
    if($LASTEXITCODE -eq 0){throw 'Release already exists; never overwrite published assets silently.'}
    if($existingRelease.Count -eq 0 -or $existingRelease[0] -notmatch '^HTTP/\S+ 404(?:\s|$)'){
        throw 'Could not verify that the release is absent.'
    }
    $releaseArguments=@('release','create',$tag,$archive,(Join-Path $Directory 'SHA256SUMS.txt'),'--repo',('github.com/'+$Repository),'--verify-tag','--title',('Agent Relay '+$tag),'--notes-file',(Join-Path $app 'packaging/release-notes.md'))
    if($version.Contains('-')){$releaseArguments+='--prerelease'}else{$releaseArguments+='--latest'}
    if($Action -eq 'PlanPublish'){
        [pscustomobject]@{repository=$repo.nameWithOwner;isPrivate=$repo.isPrivate;tag=$tag;commit=$head;archive=$archive;sha256=$verification.archiveSha256;prerelease=$version.Contains('-')} | ConvertTo-Json
        return
    }
    & git push --atomic origin ('refs/heads/main:refs/heads/main') ('refs/tags/'+$tag+':refs/tags/'+$tag)
    if($LASTEXITCODE -ne 0){throw 'Source/tag push failed.'}
    & gh @releaseArguments
    if($LASTEXITCODE -ne 0){throw 'Release creation failed; inspect remote state before retrying.'}
}finally{Pop-Location}
