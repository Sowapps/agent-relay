#requires -Version 7.2
# Exercise the real publication entry point with fictitious package evidence and command stubs.
$ErrorActionPreference='Stop'
$root=(Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path
$fixture=Join-Path $root ('local/apps/agent-relay/publication-test-'+[guid]::NewGuid().ToString('N'))
$tools=Join-Path $fixture 'apps/agent-relay/tools'
$package=Join-Path $fixture 'local/apps/agent-relay/releases/fixture'
New-Item -ItemType Directory -Path $tools,(Join-Path $package 'payload') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'release.ps1'),(Join-Path $PSScriptRoot 'release_artifact.py') -Destination $tools
$release=Join-Path $tools 'release.ps1'
$version='1.2.3-rc.1'
$tag='v'+$version
$commit='a'*40
'<Project><PropertyGroup><Version>1.2.3-rc.1</Version></PropertyGroup></Project>' | Set-Content (Join-Path $tools '../Version.props')
$sourceName='apps/agent-relay/provenance.txt'
$source=Join-Path $fixture $sourceName
'Original source' | Set-Content -LiteralPath $source
$sourceInputs=@{}
$sourceInputs[$sourceName]=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
$manifest=Join-Path $package 'payload/release-manifest.json'
@{version=$version;sourceInputs=$sourceInputs} | ConvertTo-Json | Set-Content -LiteralPath $manifest
$archive=Join-Path $package ('agent-relay-'+$version+'-win-x64.zip')
[IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $package 'payload'),$archive)
$hash=(Get-FileHash $archive).Hash.ToLowerInvariant()
$checksums=Join-Path $package 'SHA256SUMS.txt'
$checksumLine=$hash+'  '+[IO.Path]::GetFileName($archive)
$checksumLine | Set-Content -LiteralPath $checksums
@{version=$version;passed=$true;archiveSha256=$hash} | ConvertTo-Json | Set-Content (Join-Path $package 'verification.json')
@{passed=$true;archiveSha256=$hash} | ConvertTo-Json | Set-Content (Join-Path $package 'visual-verification.json')
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
'0'*64+'  '+[IO.Path]::GetFileName($archive) | Set-Content -LiteralPath $checksums
Assert-Refusal 'Checksum file does not match archive verification'
$checksumLine | Set-Content -LiteralPath $checksums
'Changed source' | Set-Content -LiteralPath $source
$sourceInputs[$sourceName]=(Get-FileHash -LiteralPath $source).Hash.ToLowerInvariant()
@{version=$version;sourceInputs=$sourceInputs} | ConvertTo-Json | Set-Content -LiteralPath $manifest
Assert-Refusal 'Source changed after packaging'
'Original source' | Set-Content -LiteralPath $source
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
