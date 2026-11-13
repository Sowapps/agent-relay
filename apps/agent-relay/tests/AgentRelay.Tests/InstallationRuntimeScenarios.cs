using System.Diagnostics;
using AgentRelay.Core;
using AgentRelay.Infrastructure;

internal static class InstallationRuntimeScenarios
{
    public static async Task FixtureAsync(string data)
    {
        await using var runtime = RelayRuntime.Start(data, error => Console.Error.WriteLine(error));
        await runtime.Completion;
    }

    public static async Task RunAsync(string data)
    {
        var project = Guid.NewGuid(); var endpoint = Guid.NewGuid(); var connection = Guid.NewGuid();
        new ProjectSettingsStore(data).Save(new(1, [new(project, "OS adapter fixture")], [new(endpoint, "Caller", "external", "fixture")], [], [new(project, endpoint, "main", [RelayCapabilities.ReadHistory], [])]));
        await new ConnectionStore(data).CreateAsync(connection, endpoint, [project], false, CancellationToken.None);
        var executable = Path.Combine(AppContext.BaseDirectory, "AgentRelay.Tests.exe");
        File.WriteAllText(Path.Combine(data, "mcp-runtime.json"), RelayJson.Serialize(new { executable, connectionId = connection }));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("update-host-fixture"); start.ArgumentList.Add(data);
        using var child = Process.Start(start) ?? throw new Exception("Owned host fixture did not start");
        var errors = child.StandardError.ReadToEndAsync();
        var output = child.StandardOutput.ReadToEndAsync();
        try
        {
            var adapter = new InstallationRuntime(data);
            adapter.DemandReady(executable, McpIdentity.Current.Version, McpIdentity.Current.ContractHash);
            var observed = adapter.Inspect(executable) ?? throw new Exception("Authenticated host identity unavailable");
            if (observed.ProcessId != child.Id) throw new Exception("Adapter selected a different process");
            File.WriteAllText(Path.Combine(data, "promotion.lock"), "owned-test");
            adapter.ConfirmDrain();
            adapter.Stop(observed);
            if (!child.HasExited) throw new Exception("Verified idle test host remained running");
            using var ownership = RelayLocation.OpenHostOwnership(data);
            Console.WriteLine("INSTALLATION_RUNTIME=PASS (real authenticated PID/start time/image, quiescence, exact owned child stop; no stable/native/model/startup mutation)");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(); await child.WaitForExitAsync(); }
            File.WriteAllText(Path.Combine(data, "host.stderr.log"), await errors);
            File.WriteAllText(Path.Combine(data, "host.stdout.log"), await output);
        }
    }
}
