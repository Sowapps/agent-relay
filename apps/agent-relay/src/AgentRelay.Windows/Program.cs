using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        try
        {
            var options = LaunchOptions.Parse(arguments);
            return options.Mode == "tray" ? RunTray(options) : RunClientAsync(options).GetAwaiter().GetResult();
        }
        catch (Exception error)
        {
            var code = error is RelayException relay ? relay.Code : error is OperationCanceledException ? "unavailable_or_timeout" : "application_error";
            Console.Error.WriteLine(RelayJson.Serialize(new RelayFailure(code, error.Message)));
            return 1;
        }
    }

    private static async Task<int> RunClientAsync(LaunchOptions options)
    {
        if (options.Mode == "initialize") { await PortableConfiguration.InitializeAsync(options.DataDirectory, options.ConnectionId); return 0; }
        if (options.Mode == "mcp") { await McpFacade.RunAsync(options.DataDirectory, options.ConnectionId); return 0; }
        if (options.Mode == "admin")
        {
            if (options.InputFile is null) throw new ArgumentException("Administrative maintenance requires an explicit input file.");
            var text = await File.ReadAllTextAsync(options.InputFile);
            var request = RelayJson.Deserialize<PipeRequest>(text);
            if (request.Operation is not ("configure_projects" or "connection")) throw new ArgumentException("Only explicit project configuration and connection maintenance are accepted.");
            Directory.CreateDirectory(options.DataDirectory);
            FileStream? ownership = null;
            try { ownership = new FileStream(Path.Combine(options.DataDirectory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { /* Host owns the directory: use its authenticated administrative route. */ }
            if (ownership is null)
            {
                Console.WriteLine(RelayJson.Serialize(await new RelayPipeClient(options.DataDirectory).RequestAsync(request, CancellationToken.None))); return 0;
            }
            using (ownership)
            {
                var connections = new ConnectionStore(options.DataDirectory); connections.EnsureAdministrator();
                Console.WriteLine(RelayJson.Serialize(await ProjectMaintenance.ApplyAsync(options.DataDirectory, request, CancellationToken.None))); return 0;
            }
        }

        if (options.Mode == "prepare")
        {
            var store = new SettingsStore(options.DataDirectory);
            var existing = store.Load();
            var distribution = existing.Profiles.FirstOrDefault()?.Distribution ?? "Ubuntu-24.04";
            var user = existing.Profiles.FirstOrDefault()?.User ?? "sowapps";
            var prepared = await WslProvisioner.PrepareAsync(options.DataDirectory, distribution, user, CancellationToken.None);
            var combined = new RelaySettings(1, existing.Profiles.Concat(prepared.Profiles).ToArray(), existing.MaximumConcurrency, existing.Transfers);
            store.Save(combined);
            Console.WriteLine(RelayJson.Serialize(combined));
            return 0;
        }
        var client = new RelayPipeClient(options.DataDirectory);
        if (options.Mode is not ("send" or "read"))
        {
            var request = options.Mode switch
            {
                "submit" => new PipeRequest("submit", Work: new(options.Id ?? Guid.NewGuid(), options.Profile ?? "", options.Kind ?? "message", options.Message ?? "", options.Command)),
                "result" => new PipeRequest("result", RequestId: options.Id),
                "history" => new PipeRequest("history"),
                "inbox" => new PipeRequest("refresh_inbox"),
                "profiles" => new PipeRequest("configuration"),
                "start-profile" => new PipeRequest("start_profile", ProfileId: options.Profile),
                _ => throw new ArgumentException("Unknown client mode.")
            };
            Console.WriteLine(RelayJson.Serialize(await client.RequestAsync(request, CancellationToken.None)));
            return 0;
        }
        var exchange = options.Mode == "send"
            ? await client.SendAsync(LocalDemo.Message(options.Message!, options.Id), CancellationToken.None)
            : await client.ReadAsync(options.Id!.Value, CancellationToken.None);
        Console.WriteLine(RelayJson.Serialize(exchange));
        return 0;
    }

    private static int RunTray(LaunchOptions options)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        void Report(Exception error)
        {
            try { File.AppendAllText(Path.Combine(options.DataDirectory, "diagnostics.log"), $"{DateTimeOffset.UtcNow:o} {error}\n"); }
            catch (IOException) { Console.Error.WriteLine(error); }
        }
        RelayRuntime runtime;
        try { runtime = RelayRuntime.Start(options.DataDirectory, Report); }
        catch (RelayAlreadyRunningException)
        {
            if (options.Show) new RelayPipeClient(options.DataDirectory).RequestAsync(new("show_window"), CancellationToken.None).GetAwaiter().GetResult();
            return 0;
        }
        try
        {
            using var context = new TrayContext(options, runtime.Completion);
            runtime.ShowWindowRequested += context.RequestOpen;
            try { Application.Run(context); }
            finally { runtime.ShowWindowRequested -= context.RequestOpen; }
            return context.HostFailed ? 1 : 0;
        }
        finally { runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }
}
