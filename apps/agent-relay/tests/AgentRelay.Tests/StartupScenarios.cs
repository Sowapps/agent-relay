using System.Diagnostics;
using System.Text.Json;
using AgentRelay.Infrastructure;
using AgentRelay.Windows;

internal static partial class UiRenderScenarios
{
    public static async Task Startup(string data, string directory)
    {
        Directory.CreateDirectory(data); Directory.CreateDirectory(directory);
        var executable = Path.Combine(AppContext.BaseDirectory, "AgentRelay.Windows.exe");
        await File.WriteAllTextAsync(Path.Combine(data, "mcp-runtime.json"), JsonSerializer.Serialize(new { executable, distribution = "portable-win-x64" }));
        if (StartupRegistration.Enabled(data)) throw new Exception("Startup qualification requires a fresh unregistered scope");
        var other = data + "-other";
        var otherBefore = StartupRegistration.Enabled(other);
        var errors = new List<Exception>();
        var preference = new FaultableStartup(new WindowsStartupPreference(data));
        try
        {
            Console.WriteLine("STARTUP_CHECK=ui_begin");
            await using (var host = RelayRuntime.Start(data, errors.Add))
            {
                await OnWindow(async window =>
                {
                    using var project = new ProjectWindow(data, preference) { TopLevel = false, Dock = DockStyle.Fill };
                    window.Controls.Add(project); project.Show(); await project.InitializePresentation();
                    var view = project.Presentation;
                    await view.EvaluateForVerificationAsync("window.RelayUI.route({view:'settings'})");
                    await WaitFor(view, "document.querySelector('#startup-switch')?.getAttribute('aria-checked')==='false'");
                    await view.EvaluateForVerificationAsync("document.querySelector('#startup-switch').click()");
                    await WaitFor(view, "document.querySelector('#startup-switch')?.getAttribute('aria-checked')==='true'&&!document.querySelector('#startup-switch').disabled");
                    if (!StartupRegistration.Enabled(data)) throw new Exception("UI did not persist actual isolated Run entry");
                    await view.CaptureAsync(Path.Combine(directory, "enabled.png"));

                    preference.FailNext = true;
                    await view.EvaluateForVerificationAsync("document.querySelector('#startup-switch').click()");
                    await WaitFor(view, "!!document.querySelector('.startup-error')&&!document.querySelector('#startup-switch').disabled");
                    if (!StartupRegistration.Enabled(data)) throw new Exception("Failed write changed confirmed preference");
                    await view.CaptureAsync(Path.Combine(directory, "error.png"));
                    await view.EvaluateForVerificationAsync("document.querySelector('[data-action=startup-retry]').click()");
                    await WaitFor(view, "document.querySelector('#startup-switch')?.getAttribute('aria-checked')==='false'&&!document.querySelector('#startup-switch').disabled&&!document.querySelector('.startup-error')");
                    if (StartupRegistration.Enabled(data)) throw new Exception("Retry did not remove isolated registration");

                    preference.Unreadable = true; await project.Reload(true);
                    await WaitFor(view, "document.querySelector('#startup-switch')?.disabled&&document.querySelector('.startup-state').textContent.includes('indisponible')");
                    await view.CaptureAsync(Path.Combine(directory, "unknown.png"));
                    preference.Unreadable = false; await project.Reload(true);
                    await WaitFor(view, "!document.querySelector('#startup-switch')?.disabled");
                    window.ClientSize = new Size(960,648);
                    await view.CaptureAsync(Path.Combine(directory, "compact.png"));
                    if (await view.EvaluateForVerificationAsync("document.querySelector('.app-scroll').scrollWidth<=document.querySelector('.app-scroll').clientWidth+1") != "true") throw new Exception("Startup settings overflow");
                });
            }
            Console.WriteLine("STARTUP_CHECK=ui_passed");
            // Exercise the actual retained WinExe bootstrap, including space-containing paths.
            var launcher = Path.Combine(data,"startup-launcher","AgentRelay.Windows.exe");
            var start = new ProcessStartInfo(launcher) { UseShellExecute = false, CreateNoWindow = true };
            foreach (var value in new[] { "startup", "--lifetime-ms", "6000" }) start.ArgumentList.Add(value);
            using var bootstrap = Process.Start(start)!;
            Console.WriteLine("STARTUP_CHECK=bootstrap_started:"+bootstrap.Id);
            await bootstrap.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            Console.WriteLine("STARTUP_CHECK=bootstrap_exited");
            if (bootstrap.ExitCode != 0) throw new Exception("Retained native bootstrap failed");
            var client = new RelayPipeClient(data);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.RequestAsync(new("configuration"),timeout.Token);
            Console.WriteLine("STARTUP_CHECK=host_ready");
            using var duplicate = Process.Start(start)!;
            await duplicate.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Console.WriteLine("STARTUP_CHECK=duplicate_exited");
            if (duplicate.ExitCode != 0) throw new Exception("Duplicate startup failed");
            await client.RequestAsync(new("configuration"),timeout.Token);
            await Task.Delay(6500);
            var pointer = Path.Combine(data,"mcp-runtime.json");
            var portable = await File.ReadAllTextAsync(pointer);
            await File.WriteAllTextAsync(pointer,JsonSerializer.Serialize(new { dotnet=Environment.ProcessPath,assembly=Path.Combine(AppContext.BaseDirectory,"AgentRelay.Windows.dll") }));
            var source = StartupRegistration.ResolveRuntime(data);
            if (source.ArgumentList.Count!=4 || source.ArgumentList[0]!=Path.Combine(AppContext.BaseDirectory,"AgentRelay.Windows.dll") || source.ArgumentList.Contains("--show") || !source.CreateNoWindow) throw new Exception("Source startup launch route is incorrect");
            await File.WriteAllTextAsync(pointer,JsonSerializer.Serialize(new {executable=Path.Combine(data,"missing.exe")}));
            try { StartupRegistration.Set(data,true); throw new Exception("Missing target was accepted"); }
            catch (IOException) { if(StartupRegistration.Enabled(data))throw new Exception("Missing target enabled registration"); }
            await File.WriteAllTextAsync(pointer,portable);
            // Host lock must be released after the bounded startup host, including its duplicate.
            using var ownership = new FileStream(Path.Combine(data,"host.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            if (StartupRegistration.Enabled(other) != otherBefore || errors.Count != 0) throw new Exception("Startup qualification leaked scope or host errors");
            await File.WriteAllTextAsync(Path.Combine(directory,"verification.json"),JsonSerializer.Serialize(new {passed=true,checks=new[]{"actual UI to per-user isolated Run enable/disable", "failure retains state and retry succeeds", "unknown state never shown as disabled", "compact layout", "retained native bootstrap and space paths", "duplicate startup preserves one owner", "source and portable launch arguments", "other scope unchanged", "registration cleaned"}}));
        }
        finally { StartupRegistration.Set(data,false); }
    }

    private sealed class FaultableStartup(IStartupPreference inner) : IStartupPreference
    {
        public bool FailNext { get; set; }
        public bool Unreadable { get; set; }
        public StartupPreferenceState Read() => Unreadable ? new(null,"Lecture refusée pour la qualification isolée.") : inner.Read();
        public StartupPreferenceState Set(bool enabled)
        {
            if (FailNext) { FailNext=false; throw new UnauthorizedAccessException("Écriture refusée pour la qualification isolée."); }
            return inner.Set(enabled);
        }
    }
}
