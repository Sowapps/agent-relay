#requires -Version 7.2
# Exercise the real publication entry point with fictitious package evidence and command stubs.
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$fixture=Join-Path $root ('local/apps/agent-relay/publication-test-'+[guid]::NewGuid().ToString('N'))
$tools=Join-Path $fixture 'apps/agent-relay/tools'
$package=Join-Path $fixture 'local/apps/agent-relay/releases/fixture'
New-Item -ItemType Directory -Path $tools,(Join-Path $package 'payload') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release.ps1') -Destination $tools
$release=Join-Path $tools 'release.ps1'
$version='1.2.3-rc.1'
$tag='v'+$version
$commit='a'*40
'<Project><PropertyGroup><Version>1.2.3-rc.1</Version></PropertyGroup></Project>' | Set-Content (Join-Path $tools '../Version.props')
$archive=Join-Path $package ('agent-relay-'+$version+'-win-x64.zip')
'Simulated archive for publication guard tests' | Set-Content $archive
$hash=(Get-FileHash $archive).Hash.ToLowerInvariant()
@{version=$version;passed=$true;archiveSha256=$hash} | ConvertTo-Json | Set-Content (Join-Path $package 'verification.json')
@{passed=$true;archiveSha256=$hash} | ConvertTo-Json | Set-Content (Join-Path $package 'visual-verification.json')
@{version=$version;sourceInputs=@{}} | ConvertTo-Json | Set-Content (Join-Path $package 'payload/release-manifest.json')
$target='sowapps/agent-relay'
$canonical='https://github.com/'+$target
$state=@{pushUrls=@($canonical+'.git');metadata=@{isPrivate=$false;nameWithOwner=$target;url=$canonical};releaseStatus=404;pushFails=$false}
$writes=[Collections.Generic.List[string]]::new()

function git {
    $global:LASTEXITCODE=0
    switch ($args -join ' ') {
        'status --porcelain' { return }
        'rev-parse HEAD' { return $commit }
        "rev-parse $tag^{commit}" { return $commit }
        'rev-parse refs/heads/main' { return $commit }
        'remote get-url --push --all origin' { return $state.pushUrls }
        default {
            if($args[0] -ne 'push'){throw 'Unexpected Git command in fixture.'}
            $writes.Add($args -join ' ')
            if($state.pushFails){$global:LASTEXITCODE=1}
        }
    }
}

function gh {
    $global:LASTEXITCODE=0
    switch ($args[0]) {
        'repo' { return ($state.metadata | ConvertTo-Json -Compress) }
        'api' {
            if($state.releaseStatus -eq 200){return '{}'}
            $global:LASTEXITCODE=1
            return ('HTTP/2.0 '+$state.releaseStatus+' Fixture')
        }
        'release' { $writes.Add($args -join ' ') }
        default { throw 'Unexpected GitHub command in fixture.' }
    }
}

function Assert-Refusal([string]$Expected,[string]$Repository=$target) {
    try { & $release -Action Publish -Directory $package -Repository $Repository; throw 'Publication was not refused.' }
    catch { if($_.Exception.Message -notlike ('*'+$Expected+'*')){throw} }
    if($writes.Count){throw 'Refusal attempted an external write.'}
}

$plan=& $release -Action PlanPublish -Directory $package -Repository $target | ConvertFrom-Json
if($plan.repository -ne $target -or $plan.isPrivate -ne $false -or $plan.tag -ne $tag -or $writes.Count){throw 'Public publication plan was incorrect.'}
Assert-Refusal 'Select the authorized publication target' 'unrelated/project'
Assert-Refusal 'Select the authorized publication target' ''
$state.pushUrls=@('https://github.com/unrelated/project.git')
Assert-Refusal 'single origin push target'
$state.pushUrls=@($canonical+'.git',$canonical+'.git')
Assert-Refusal 'single origin push target'
$state.pushUrls=@('git@github.com:'+$target+'.git')
$state.metadata.nameWithOwner='unrelated/project'
Assert-Refusal 'different repository'
$state.metadata.nameWithOwner=$target
$state.releaseStatus=200
Assert-Refusal 'Release already exists'
$state.releaseStatus=503
Assert-Refusal 'Could not verify that the release is absent'
$state.releaseStatus=404
$state.pushFails=$true
try { & $release -Action Publish -Directory $package -Repository $target; throw 'Push failure was ignored.' }
catch { if($_.Exception.Message -notlike '*Source/tag push failed*'){throw} }
if($writes.Count -ne 1){throw 'A release was attempted after a failed push.'}
$writes.Clear()
$state.pushFails=$false
& $release -Action Publish -Directory $package -Repository $target
if($writes.Count -ne 2 -or $writes[0] -ne "push --atomic origin refs/heads/main:refs/heads/main refs/tags/${tag}:refs/tags/$tag" -or
    $writes[1] -notlike ('release create '+$tag+'*--repo github.com/'+$target+'*--verify-tag*--prerelease')){throw 'Unexpected simulated publication commands.'}
Write-Output 'RELEASE_PUBLICATION=PASS (public plan, wrong repository/remote, duplicate remote, existing release, API failure, simulated publication)'
