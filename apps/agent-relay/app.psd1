@{
    Id = 'agent-relay'
    Toolchain = 'dotnet'
    RequiredSdkMajor = 10
    Driver = 'tools/app.ps1'
    Solution = 'AgentRelay.slnx'
    RunProject = 'src/AgentRelay.Windows/AgentRelay.Windows.csproj'
    TestProject = 'tests/AgentRelay.Tests/AgentRelay.Tests.csproj'
}
