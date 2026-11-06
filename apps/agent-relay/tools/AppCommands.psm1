function New-AppCommandContext {
    param([string] $RepositoryRoot, [string] $ManifestPath)
    Import-Module (Join-Path $RepositoryRoot 'tools/Workspace.psm1') -Force
    Import-Module (Join-Path $RepositoryRoot 'tools/ToolchainEnvironment.psm1') -Force
    Import-Module (Join-Path $PSScriptRoot 'RuntimeStaging.psm1') -Force
    $manifest = Import-PowerShellDataFile -LiteralPath $ManifestPath
    $application = Resolve-WorkspaceApplication $RepositoryRoot $manifest.Id
    $status = Get-DotnetStatus $RepositoryRoot $application
    if ($status.QualificationStatus -ne 'qualified') { Stop-WorkspaceAction 'Development toolchain is not currently qualified. Run dev.ps1 qualify.' 2 }
    [pscustomobject]@{
        RepositoryRoot = $RepositoryRoot; Application = $application; Status = $status
        CacheRoot = (Join-Path $RepositoryRoot 'local/toolchains/cache')
        DataDirectory = (Join-Path $RepositoryRoot 'var/apps/agent-relay/default')
    }
}

function Invoke-AppDotnet {
    param($Context, [string[]] $Arguments)
    Invoke-IsolatedToolchain $Context.Status.DotnetPath $Context.CacheRoot {
        Push-Location -LiteralPath $Context.Application.Root
        try {
            & $Context.Status.DotnetPath @Arguments
            if ($LASTEXITCODE -ne 0) { throw "Application dotnet command failed (exit $LASTEXITCODE): $($Arguments -join ' ')" }
        }
        finally { Pop-Location }
    }
}

function Get-AppAssembly {
    param($Context, [string] $Project)
    $path = Invoke-AppDotnet $Context @('msbuild', $Project, '-getProperty:TargetPath', '-p:Configuration=Debug')
    $path = ($path -join '').Trim()
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Build output is missing: $path" }
    return $path
}

function Build-App {
    param($Context, [switch] $Offline)
    $solution = $Context.Application.Manifest.Solution
    $restore = @('restore', $solution, '--locked-mode')
    if ($Offline) {
        $configuration = Join-Path $Context.CacheRoot 'offline.NuGet.Config'
        New-Item -ItemType Directory -Path $Context.CacheRoot -Force | Out-Null
        '<configuration><packageSources><clear /></packageSources></configuration>' | Set-Content -LiteralPath $configuration -Encoding utf8
        $restore += @('--configfile', $configuration, '-p:NuGetAudit=false')
    }
    Invoke-AppDotnet $Context $restore
    Invoke-AppDotnet $Context @('build', $solution, '--no-restore', '--disable-build-servers', '-p:UseSharedCompilation=false')
}

function Test-App {
    param($Context, [switch] $Offline)
    Build-App $Context -Offline:$Offline
    & (Join-Path $PSScriptRoot 'test-runtime-staging.ps1')
    $directory = Join-Path $Context.RepositoryRoot ('local/apps/agent-relay/tests/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $tests = Get-AppAssembly $Context $Context.Application.Manifest.TestProject
    Invoke-AppDotnet $Context @($tests, (Join-Path $directory 'integration')) |
        Tee-Object -FilePath (Join-Path $directory 'integration.log')
    $windows = Get-AppAssembly $Context $Context.Application.Manifest.RunProject
    $windows = Get-StagedApplication $windows (Join-Path $Context.RepositoryRoot 'local/apps/agent-relay/run')
    $python = Get-Command python.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
    Invoke-IsolatedToolchain $Context.Status.DotnetPath $Context.CacheRoot {
        & $python.Source -B (Join-Path $Context.Application.Root 'tools/application-smoke.py') `
            --dotnet $Context.Status.DotnetPath --assembly $windows --data (Join-Path $directory 'processes') |
            Tee-Object -FilePath (Join-Path $directory 'process-smoke.log')
        if ($LASTEXITCODE -ne 0) { throw 'Windows application process smoke failed.' }
    }
    Write-Output "Application test artifacts: $directory"
}

function Start-App {
    param($Context, [switch] $Offline, [int] $LifetimeMilliseconds = 0)
    Build-App $Context -Offline:$Offline
    $assembly = Get-AppAssembly $Context $Context.Application.Manifest.RunProject
    $data = New-RelayDevelopmentScope $Context.RepositoryRoot
    $assembly = Get-StagedApplication $assembly (Join-Path $data 'runtime')
    $fixture = New-RelayDevelopmentFixture $Context $assembly $data
    $start = [Diagnostics.ProcessStartInfo]::new($Context.Status.DotnetPath)
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $arguments = @($assembly, 'tray', '--data-dir', $data)
    if ($LifetimeMilliseconds -gt 0) {
        $arguments += @('--lifetime-ms', $LifetimeMilliseconds.ToString([Globalization.CultureInfo]::InvariantCulture))
    } else {
        $arguments += '--show'
    }
    foreach ($argument in $arguments) { $start.ArgumentList.Add($argument) }
    $process = Invoke-IsolatedToolchain $Context.Status.DotnetPath $Context.CacheRoot { [Diagnostics.Process]::Start($start) }
    try {
        Write-Output "Agent Relay dev running (PID $($process.Id)); data: $data; MCP fragment: $fixture"
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw "Agent Relay exited with code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}

function New-RelayDevelopmentScope {
    param([string] $RepositoryRoot)
    $base = Get-Item -LiteralPath $RepositoryRoot
    foreach ($segment in @('var', 'apps', 'agent-relay', 'isolated-delivery', 'dev')) {
        $path = Join-Path $base.FullName $segment
        if (-not (Test-Path -LiteralPath $path)) { New-Item -ItemType Directory -Path $path | Out-Null }
        $base = Get-Item -LiteralPath $path -Force
        if (-not $base.PSIsContainer -or ($base.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Dev scope requires an ordinary directory: $path"
        }
    }
    (New-Item -ItemType Directory -Path (Join-Path $base.FullName ([guid]::NewGuid().ToString('N')))).FullName
}

function New-RelayDevelopmentFixture {
    param($Context, [string] $Assembly, [string] $Data)
    $main = (New-Item -ItemType Directory -Path (Join-Path $Data 'workspace-main')).FullName
    $peer = (New-Item -ItemType Directory -Path (Join-Path $Data 'workspace-peer')).FullName
    $requests = Join-Path $Data 'fixture'
    $example = Join-Path $Context.Application.Root 'doc/using/examples/new-project-request.ps1'
    & $example -DataDirectory $Data -Name 'Agent Relay dev' -MainWorkspace $main -PeerWorkspace $peer -OutputDirectory $requests -LocalOnly | Out-Null
    foreach ($request in @('configure-project.request.json', 'connection.request.json')) {
        Invoke-AppDotnet $Context @($Assembly, 'admin', '--data-dir', $Data, '--input', (Join-Path $requests $request)) | Out-Null
    }
    $binding = Get-Content -LiteralPath (Join-Path $requests 'example-context.json') -Raw | ConvertFrom-Json
    $fragment = Join-Path $Data 'mcp.dev.json'
    @{ mcpServers = @{ agent_relay_dev = @{
        command = $Context.Status.DotnetPath
        args = @($Assembly, 'mcp-worker', '--data-dir', $Data, '--connection-id', $binding.connectionId)
    } } } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $fragment -Encoding utf8
    $fragment
}

Export-ModuleMember -Function New-AppCommandContext, Invoke-AppDotnet, Get-AppAssembly, Build-App, Test-App, Start-App
