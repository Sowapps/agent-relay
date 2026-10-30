using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Windows.Monitoring;
using AgentRelay.Windows.Presentation;

namespace AgentRelay.Windows;

internal sealed partial class ProjectWindow : Form
{
    private readonly string data;
    private readonly IMonitorSource source;
    private readonly RelayPipeClient client;
    private readonly AttentionReviews reviews;
    internal readonly RelayWebView Presentation = new();
    private readonly Dictionary<Guid, ProjectObservation> observations = [];
    private readonly Dictionary<Guid, ProjectObservation> threads = [];
    private Guid? activeSession;
    private bool observingOnline;
    private string? observingError;
    private ProjectConfiguration configuration = new(1, [], [], [], []);
    private IReadOnlyList<EndpointDescriptor> descriptors = [];
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 6000 };
    private readonly CancellationTokenSource lifetime = new();
    private bool loading, released;
    private Task? initialization;
    private string? previousPresentation;
    private TechnicalConsoleWindow? console;
    public event EventHandler? QuitRequested;

    public ProjectWindow(string data)
    {
        this.data = Path.GetFullPath(data); source = new PipeMonitorSource(data); client = new(data); reviews = new(data);
        UiTheme.Apply(this); Text = "Agent Relay"; ClientSize = new Size(1120,728);
        MinimumSize = new Size(816, 590); StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(Presentation);
        Presentation.Command += async message => await HandleAction(message);
        Presentation.Failed += ShowRenderingFailure;
        refresh.Tick += async (_, _) => { if (Visible && !(console?.Visible ?? false)) await Reload(false); };
        Shown += async (_, _) => await InitializePresentation();
        FormClosing += (_, args) => { if (args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); } };
    }

    internal Task InitializePresentation() => initialization ??= InitializeCore();
    private async Task InitializeCore()
    {
        try
        {
            await Presentation.InitializeAsync(Path.Combine(data,"presentation-cache"));
            if (released) return;
            await Reload(true); if (reviews.Warning is not null) Notice(reviews.Warning); refresh.Start();
        }
        catch (Exception error) when (!released) { ShowRenderingFailure(UserMessages.Error(error)); }
        catch (ObjectDisposedException) when (released) { }
    }

    private void ShowRenderingFailure(string message)
    {
        if (released) return;
        refresh.Stop(); Presentation.Visible = false;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(32), FlowDirection = FlowDirection.TopDown };
        panel.Controls.Add(new Label { Text = "L’interface ne peut pas s’afficher", AutoSize = true, Font = new Font(Font.FontFamily,18,FontStyle.Bold) });
        panel.Controls.Add(new Label { Text = message + "\nLe relais et MCP restent indépendants de cette fenêtre. Aucune installation automatique n’est effectuée.", Width = 680, AutoSize = true, MaximumSize = new Size(680,0) });
        var technical = new Button { Text = "Console technique", AutoSize = true }; technical.Click += (_,_) => OpenConsole(); panel.Controls.Add(technical);
        var quit = new Button { Text = "Quitter", AutoSize = true }; quit.Click += (_,_) => QuitRequested?.Invoke(this, EventArgs.Empty); panel.Controls.Add(quit);
        Controls.Add(panel); panel.BringToFront();
    }

    internal async Task Reload(bool configurationChanged, bool older = false, Guid? projectId = null)
    {
        if (loading || released || !Presentation.IsReady) return; loading = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token); timeout.CancelAfter(TimeSpan.FromSeconds(12));
        var online = false; string? error = null;
        try
        {
            // Refresh scope before every observation; revoked/deleted projects must disappear promptly.
            configuration = await source.ConfigurationAsync(timeout.Token); descriptors = source.Describe(configuration);
            foreach (var id in observations.Keys.Where(id => !configuration.Projects.Any(project => project.Id == id)).ToArray()) observations.Remove(id);
            foreach (var id in threads.Keys.Where(id=>!observations.ContainsKey(threads[id].ProjectId)).ToArray()) threads.Remove(id);
            foreach (var project in configuration.Projects) observations.TryAdd(project.Id,new(project.Id));
            var failures = new List<string>();
            foreach (var project in observations.Values.Where(item => !older || projectId is null || item.ProjectId == projectId).ToArray())
            {
                try { await source.LoadAsync(project, older, timeout.Token); }
                catch (Exception failure) when (!released) { project.Error = UserMessages.Error(failure); failures.Add(project.Error); }
            }
            if (activeSession is { } selected && Sessions.FirstOrDefault(item=>item.Id==selected) is { } session) await ReadThread(session,false,timeout.Token);
            online = failures.Count == 0; error = failures.FirstOrDefault();
        }
        catch (Exception failure) when (!released) { error = UserMessages.Error(failure); }
        catch (OperationCanceledException) when (released) { }
        finally { loading = false; }
        if (released) return;
        observingOnline = online; observingError = error;
        PublishSnapshot(configurationChanged || older);
    }

    private void PublishSnapshot(bool force)
    {
        if (released) return;
        var online=observingOnline;var error=observingError;
        var startup = StartupRegistration.Enabled(data);
        var version = RelayJson.Serialize(configuration) + RelayJson.Serialize(descriptors) + observations.Values.Sum(item=>item.Revision) + RelayJson.Serialize(threads.Select(item=>new {item.Key,item.Value.Revision,item.Value.Loaded,item.Value.Error,item.Value.MoreOperations,item.Value.MoreMessages})) + online + error + startup + reviews.Revision + DateTime.Today;
        if (!force && version == previousPresentation) return;
        previousPresentation = version;
        var snapshot = PresentationSnapshot.Build(configuration,descriptors,observations.Values.ToArray(),online,error,DateTimeOffset.Now,startup,reviews,threads);
        Presentation.Send(new { type = "snapshot", data = snapshot });
    }

    private void Notice(string text) => Presentation.Send(new { type = "notice", text });
    private void OpenConsole()
    {
        if (console is null || console.IsDisposed) { console = new TechnicalConsoleWindow(data) { Text = "Agent Relay · Console technique" }; console.QuitRequested += (_,_) => console.Close(); }
        console.Show(this); console.Activate();
    }
    private async Task EditProjects()
    {
        using var editor = new RelayProjectEditor(configuration,new SettingsStore(data).Load());
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        await client.RequestAsync(new("configure_projects",ProjectsConfiguration:editor.Configuration),lifetime.Token); await Reload(true);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !released) { released=true; lifetime.Cancel(); refresh.Dispose(); console?.Dispose(); Presentation.Dispose(); lifetime.Dispose(); }
        base.Dispose(disposing);
    }
}
