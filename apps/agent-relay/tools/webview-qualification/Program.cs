using System.Diagnostics;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var assets = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output); var result = 1;
        ApplicationConfiguration.Initialize();
        using var window = new Form { Text = "Agent Relay · WebView qualification", ClientSize = new Size(960, 648) };
        using var view = new WebView2 { Dock = DockStyle.Fill };
        window.Controls.Add(view); var watch = Stopwatch.StartNew();
        window.Shown += async (_, _) =>
        {
            try
            {
                var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(output, "browser-data"));
                await view.EnsureCoreWebView2Async(environment);
                var core = view.CoreWebView2;
                core.Settings.AreHostObjectsAllowed = false; core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.DownloadStarting += (_, e) => e.Cancel = true;
                core.NavigationStarting += (_, e) => e.Cancel = e.Uri != "https://agent-relay.invalid/index.html";
                core.SetVirtualHostNameToFolderMapping("agent-relay.invalid", assets, CoreWebView2HostResourceAccessKind.DenyCors);
                var loaded = new TaskCompletionSource();
                core.NavigationCompleted += (_, e) => { if (e.IsSuccess) loaded.TrySetResult(); else loaded.TrySetException(new IOException(e.WebErrorStatus.ToString())); };
                core.Navigate("https://agent-relay.invalid/index.html");
                await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20));
                var fixture = File.ReadAllText(args[2]);
                await core.ExecuteScriptAsync("window.RelayUI.receive({type:'snapshot',data:" + fixture + "});window.RelayUI.route({project:'atelier',detail:'task',id:'t-home'});");
                var geometry = await core.ExecuteScriptAsync("JSON.stringify({width:innerWidth,height:innerHeight,drawer:!!document.querySelector('.detail-panel'),cards:document.querySelectorAll('.stat').length})");
                await using (var stream = File.Create(Path.Combine(output,"own-view.png"))) await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,stream);
                var browser = Process.GetProcessById((int)core.BrowserProcessId);
                await File.WriteAllTextAsync(Path.Combine(output,"result.json"),JsonSerializer.Serialize(new { passed=true, runtime=environment.BrowserVersionString, elapsedMs=watch.ElapsedMilliseconds, geometry, browserPrivateBytes=browser.PrivateMemorySize64, browserPid=browser.Id, source=core.Source, capture="WebView2 own surface; no desktop capture", sdk="1.0.4314.35" },new JsonSerializerOptions { WriteIndented=true }));
                result=0;
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(output,"failure.txt"),error.ToString()); }
            finally { window.Close(); }
        };
        Application.Run(window); return result;
    }
}
