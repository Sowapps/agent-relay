using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace AgentRelay.Windows.Presentation;

// Owns the rendering boundary, not project authorization or application actions.
internal sealed class RelayWebView : UserControl
{
    internal const string Page = "https://agent-relay.invalid/index.html";
    private readonly WebView2 browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(247, 249, 252) };
    private readonly TaskCompletionSource pageReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal event Action<JsonElement>? Command;
    internal event Action<string>? Failed;
    internal string RuntimeVersion { get; private set; } = "";
    internal bool IsReady { get; private set; }

    public RelayWebView() { Dock = DockStyle.Fill; Controls.Add(browser); }

    internal async Task InitializeAsync(string userDataDirectory, string? assetDirectory = null)
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, Path.GetFullPath(userDataDirectory));
        await browser.EnsureCoreWebView2Async(environment);
        RuntimeVersion = environment.BrowserVersionString;
        var core = browser.CoreWebView2;
        core.Settings.AreHostObjectsAllowed = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.DownloadStarting += (_, args) => args.Cancel = true;
        core.NavigationStarting += (_, args) => args.Cancel = args.Uri != Page;
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            if (!Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "agent-relay.invalid")
                args.Response = environment.CreateWebResourceResponse(null, 403, "External resources disabled", "");
        };
        core.ProcessFailed += (_, args) => Failed?.Invoke("Le moteur d’affichage s’est arrêté : " + args.ProcessFailedKind + ". Fermez puis rouvrez la fenêtre.");
        core.NavigationCompleted += (_, args) => { if (!args.IsSuccess) pageReady.TrySetException(new IOException("Chargement de l’interface : " + args.WebErrorStatus)); };
        core.WebMessageReceived += (_, args) =>
        {
            if (args.Source != Page || args.WebMessageAsJson.Length > 262144) return;
            try
            {
                using var document = JsonDocument.Parse(args.WebMessageAsJson);
                var message = document.RootElement;
                if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String) return;
                if (action.GetString() == "ready") { IsReady = true; pageReady.TrySetResult(); }
                else Command?.Invoke(message.Clone());
            }
            catch (JsonException) { /* Malformed presentation input is never an application action. */ }
        };
        var assets = Path.GetFullPath(assetDirectory ?? Path.Combine(AppContext.BaseDirectory, "presentation"));
        if (!File.Exists(Path.Combine(assets, "index.html"))) throw new FileNotFoundException("Les fichiers de présentation d’Agent Relay sont absents.");
        core.SetVirtualHostNameToFolderMapping("agent-relay.invalid", assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Navigate(Page);
        await pageReady.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    internal void Send(object message)
    { if (IsReady && !IsDisposed) browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, Infrastructure.RelayJson.Options)); }

    // Explicit verification surface; no desktop/window discovery or input is involved.
    internal Task<string> EvaluateForVerificationAsync(string script) => browser.CoreWebView2.ExecuteScriptAsync(script);
    internal async Task CaptureAsync(string path)
    { await using var stream = File.Create(path); await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream); }

    protected override void Dispose(bool disposing)
    { if (disposing) { IsReady = false; browser.Dispose(); } base.Dispose(disposing); }
}
