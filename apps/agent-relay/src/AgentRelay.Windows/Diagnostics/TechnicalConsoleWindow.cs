using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

// Explicit manual troubleshooting; never the default monitoring surface.
internal sealed class TechnicalConsoleWindow : Form
{
    private readonly string data;
    private readonly RelayPipeClient client;
    private readonly UiActionStates pendingActions;
    private readonly SemaphoreSlim listRead = new(1, 1);
    private ProjectConfiguration configuration = new(1, [], [], [], []);
    private readonly ComboBox project = Selector("Projet relayé"), actor = Selector("Agent principal"), endpoint = Selector("Destination"), workspace = Selector("Espace de travail"), session = Selector("Session"), command = Selector("Commande");
    private readonly TextBox message = new() { Multiline = true, MaxLength = 8000, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, PlaceholderText = "Message à transmettre à cette conversation…" };
    private readonly TextBox search = new() { Width = 160, PlaceholderText = "Rechercher dans l’activité" };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly ComboBox stateFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 }, period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly CheckBox sessionOnly = new() { Text = "Cette session", AutoSize = true };
    private Guid? taskFilter;
    private readonly ListBox operations = new() { Dock = DockStyle.Fill }, inbox = new() { Dock = DockStyle.Fill }, events = new() { Dock = DockStyle.Fill };
    private readonly TextBox output = new() { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both };
    private readonly Label context = UiTheme.Label("Déclarez un projet pour commencer.", 9, muted: true), status = UiTheme.Label("Chargement…", 9, muted: true), title = UiTheme.Label("Activité du projet", 12, bold: true);
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolTip hints = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1800 };
    private bool loading, polling, raw, sent;
    private object? selected;
    private readonly Button closeCancellation;
    private Guid? cancellationRequest;
    private CancellationProgress? cancellation;
    private long? more;
    private long? inboxCursor;
    private readonly List<WorkRecord> retained = [];
    public event EventHandler? QuitRequested;
    private static ComboBox Selector(string accessible) => new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", AccessibleName = accessible };

    public TechnicalConsoleWindow(string data)
    {
        this.data = data; client = new(data); pendingActions = new(Error); UiTheme.Apply(this); Text = "Agent Relay"; Size = new Size(1190, 900); MinimumSize = new Size(1030, 780); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 5 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 168)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 }; header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new BrandMark()); var brand = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false }; brand.Controls.Add(UiTheme.Label("Agent Relay", 21, bold: true)); brand.Controls.Add(UiTheme.Label("Projets, conversations et échanges", 10, muted: true)); header.Controls.Add(brand);
        var menu = new FlowLayoutPanel { AutoSize = true, WrapContents = false }; menu.Controls.Add(pendingActions.Button("Nouveau projet", () => "configuration", "Création…", NewProject)); menu.Controls.Add(UiTheme.Button("Paramètres", Settings)); menu.Controls.Add(UiTheme.Button("Quitter", (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty))); header.Controls.Add(menu); layout.Controls.Add(header);
        var selectors = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4, Padding = new Padding(12), BackColor = UiTheme.Surface };
        foreach (var width in new[] { 25f, 25f, 25f, 25f }) selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, width));
        var labels = new[] { "Projet relayé", "Main / Principal", "Destination", "Workspace / Espace de travail" }; var controls = new[] { project, actor, endpoint, workspace };
        for (var i = 0; i < 4; i++) { selectors.Controls.Add(UiTheme.Label(labels[i], 9, bold: true), i, 0); selectors.Controls.Add(controls[i], i, 1); }
        selectors.Controls.Add(UiTheme.Label("Session / Conversation", 9, bold: true), 0, 2); selectors.SetColumnSpan(session, 2); selectors.Controls.Add(session, 0, 3);
        var sessionActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        sessionActions.Controls.Add(pendingActions.Button("Nouvelle", () => DestinationKey("new-session", RelayCapabilities.ManageSession), "Création…", NewSession));
        sessionActions.Controls.Add(pendingActions.Button("Démarrer", () => SessionKey(RelayCapabilities.ManageSession, current => current.State != "closed"), "Démarrage…", () => Control("start")));
        sessionActions.Controls.Add(pendingActions.Button("Renommer", () => SessionKey(RelayCapabilities.ManageSession), "Enregistrement…", async () => { var name = AskName("Renommer la session"); if (name is not null) await Control("rename:" + name); }));
        sessionActions.Controls.Add(pendingActions.Button("Fermer", () => SessionKey(RelayCapabilities.ManageSession, current => current.State != "closed"), "Fermeture…", () => Control("close")));
        selectors.Controls.Add(sessionActions, 2, 3); selectors.SetColumnSpan(sessionActions, 2); layout.Controls.Add(selectors);
        var composer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(12), BackColor = UiTheme.Surface }; composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 35)); composer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); composer.RowStyles.Add(new RowStyle(SizeType.Absolute, 42)); context.MaximumSize = new Size(1000, 34); composer.Controls.Add(context); composer.Controls.Add(message);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        actions.Controls.Add(pendingActions.Button("Envoyer", () => string.IsNullOrWhiteSpace(message.Text) ? null :
            SessionKey(RelayCapabilities.SendMessage, current => current.State is not ("closed" or "awaiting_approval")), "Envoi…", Send, primary: true,
            progress: () => session.SelectedItem is SessionSnapshot current && SessionHasWork(current) ? "Travail en cours…" : null));
        actions.Controls.Add(command); command.Width = 180; command.Dock = DockStyle.None;
        actions.Controls.Add(pendingActions.Button("Exécuter la commande", CommandKey, "Envoi…", RunCommand,
            progress: () => CommandIsActive() ? "Commande en cours…" : null));
        actions.Controls.Add(pendingActions.Button("État de la session", () => SessionKey(RelayCapabilities.ReadHistory), "Inspection…", Inspect));
        actions.Controls.Add(pendingActions.Button("Interrompre", () => SessionKey(RelayCapabilities.ManageSession, current => current.State != "closed"), "Interruption…", () => Control("interrupt")));
        composer.Controls.Add(actions); layout.Controls.Add(composer);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1100, 360), SplitterDistance = 445, SplitterWidth = 12, Panel1MinSize = 350, Panel2MinSize = 350 };
        var left = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 }; left.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 110)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        var filters = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true }; kind.Items.AddRange(["Tout", "Messages", "Commandes", "Transferts"]); kind.SelectedIndex = 0;
        stateFilter.Items.AddRange(["Tous les états", "En attente", "En cours", "À autoriser", "Terminée", "Échec", "À vérifier", "Approbation expirée", UserMessages.WorkState("retry_wait"), UserMessages.WorkState("awaiting_reply")]); stateFilter.SelectedIndex = 0;
        period.Items.AddRange(["Toutes les dates", "Dernières 24 h", "7 derniers jours"]); period.SelectedIndex = 0;
        filters.Controls.Add(search); filters.Controls.Add(kind); filters.Controls.Add(stateFilter); filters.Controls.Add(period); filters.Controls.Add(sessionOnly);
        filters.Controls.Add(pendingActions.Button("Filtrer", () => ProjectKey("activity", RelayCapabilities.ReadHistory), "Chargement…", async () => { taskFilter = null; await RefreshActivity(true); }));
        filters.Controls.Add(pendingActions.Button("Même tâche", () => (selected as WorkRecord)?.Request.TaskId is not null
            ? ProjectKey("activity", RelayCapabilities.ReadHistory) : null, "Chargement…", async () => { taskFilter = (selected as WorkRecord)?.Request.TaskId; await RefreshActivity(true); })); left.Controls.Add(filters);
        var history = new TabPage("Opérations"); history.Controls.Add(operations); var received = new TabPage("Messages reçus"); received.Controls.Add(inbox); tabs.TabPages.AddRange([history, received]); left.Controls.Add(tabs);
        var transitions = new TabPage("Événements"); transitions.Controls.Add(events); tabs.TabPages.Add(transitions);
        var foot = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        foot.Controls.Add(pendingActions.Button("Charger plus", () => more is not null ? ProjectKey("activity", RelayCapabilities.ReadHistory) : null, "Chargement…", () => RefreshActivity(false)));
        foot.Controls.Add(pendingActions.Button("Relever les messages", () => ProjectKey("inbox", RelayCapabilities.ReceiveMessage), "Relève…", Receive)); left.Controls.Add(foot); split.Panel1.Controls.Add(left);
        ActivityListStyle.Apply(operations, row => row is WorkRecord work ? State(work.State) + " · " + work.Metadata?.Label : "", row => row is WorkRecord work ? DisplayDateTime.Format(work.CreatedAt) + " · " + work.Request.Text.ReplaceLineEndings(" ") : "");
        ActivityListStyle.Apply(inbox, row => row is InboxMessage item ? (item.InReplyTo is null ? "Message indépendant" : "Réponse") + " · " + EndpointName(item.SenderEndpointId) : "", row => row is InboxMessage item ? DisplayDateTime.Format(item.ReceivedAt) + " · " + item.Text.ReplaceLineEndings(" ") : "");
        ActivityListStyle.Apply(events, row => row is WorkEvent item ? State(item.State) + " · " + EndpointName(item.EndpointId) : "", row => row is WorkEvent item ? DisplayDateTime.Format(item.OccurredAt, seconds: true) + " · Tâche " + item.TaskId : "");
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 }; detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); detail.Controls.Add(title);
        var detailActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top }; detailActions.Controls.Add(UiTheme.Button("Copier", Copy)); detailActions.Controls.Add(UiTheme.Button("Message envoyé", (_, _) => { sent = !sent; raw = false; Render(); })); detailActions.Controls.Add(UiTheme.Button("Détails techniques", (_, _) => { raw = !raw; Render(); }));
        detailActions.Controls.Add(pendingActions.Button("Annuler l’opération", CancellationKey, "Annulation…", Cancel));
        closeCancellation = pendingActions.Button("Fermer la session liée", () => SelectedCancellation is { NextAction: "close_session" } progress
            ? $"session:{progress.SessionId:D}" : null, "Fermeture…", CloseCancellation);
        closeCancellation.Visible = false;
        detailActions.Controls.Add(closeCancellation);
        detail.Controls.Add(detailActions); output.BackColor = UiTheme.Surface; output.BorderStyle = BorderStyle.None; detail.Controls.Add(output); split.Panel2.Controls.Add(UiTheme.Card(detail)); layout.Controls.Add(split); layout.Controls.Add(status); Controls.Add(layout);
        detailActions.Controls.Add(pendingActions.Button("Événements", () => ProjectKey("events", RelayCapabilities.ReadHistory), "Chargement…", ReadEvents));
        detailActions.Controls.Add(pendingActions.Button("Inspecter l’environnement", () => endpoint.SelectedItem is RelayEndpoint target &&
            Has(RelayCapabilities.InspectHost) ? $"inspect-host:{target.Id:D}" : null, "Inspection…", InspectHost));
        detailActions.Controls.Add(pendingActions.Button("Fichier partiel", PartialKey, "Inspection…", () => Partial(false)));
        detailActions.Controls.Add(pendingActions.Button("Nettoyer le partiel", PartialKey, "Nettoyage…", () => Partial(true)));
        project.SelectedIndexChanged += async (_, _) => { if (!loading) await SelectProject(); }; actor.SelectedIndexChanged += async (_, _) => { if (!loading) await SelectEndpoint(); }; endpoint.SelectedIndexChanged += async (_, _) => { if (!loading) await SelectEndpoint(); };
        workspace.SelectedIndexChanged += (_, _) => ShowContext(); session.SelectedIndexChanged += (_, _) => ShowContext(); command.SelectedIndexChanged += (_, _) => ShowContext();
        message.TextChanged += (_, _) => pendingActions.Refresh();
        operations.SelectedIndexChanged += async (_, _) => { if (!polling && operations.SelectedItem is WorkRecord item) await ShowOperation(item.Request.Id); };
        inbox.SelectedIndexChanged += (_, _) => { if (inbox.SelectedItem is InboxMessage item) { selected = item; sent = false; raw = false; title.Text = item.InReplyTo is null ? "Message indépendant" : "Réponse reçue"; Render(); } };
        events.SelectedIndexChanged += (_, _) => { if (events.SelectedItem is WorkEvent item) { selected = item; raw = false; Render(); } };
        timer.Tick += async (_, _) => await RefreshActivity(true, background: true); Shown += async (_, _) => { await LoadConfiguration(); timer.Start(); };
    }

    private Guid ProjectId => (project.SelectedItem as RelayProject)?.Id ?? Guid.Empty;
    private Guid ActorId => (actor.SelectedItem as RelayEndpoint)?.Id ?? Guid.Empty;
    private bool Has(string capability) => !loading && configuration.Memberships.Any(member =>
        member.ProjectId == ProjectId && member.EndpointId == ActorId && member.Capabilities.Contains(capability));
    private string? ProjectKey(string group, string capability) => Has(capability) ? $"{group}:{ProjectId:D}" : null;
    private string? DestinationKey(string group, string capability) => Has(capability) && endpoint.SelectedItem is RelayEndpoint target &&
        workspace.SelectedItem is RelayWorkspace folder ? $"{group}:{ProjectId:D}:{target.Id:D}:{folder.Id:D}" : null;
    private string? SessionKey(string capability, Func<SessionSnapshot, bool>? predicate = null) => Has(capability) &&
        session.SelectedItem is SessionSnapshot current && (predicate is null || predicate(current)) ? $"session:{current.Id:D}" : null;
    private IEnumerable<WorkRecord> CurrentOperations() => selected is WorkRecord current ? retained.Prepend(current) : retained;
    private bool SessionHasWork(SessionSnapshot current) => current.WorkActive || CurrentOperations().Any(item =>
        item.Request.SessionId == current.Id && (MonitorProjection.InProgress(item.State) || item.State == "awaiting_approval"));
    private string? CommandKey()
    {
        var destination = DestinationKey("command", RelayCapabilities.RunCommand);
        return destination is not null && command.SelectedItem is CommandRule rule ? destination + ":" + rule.Id : null;
    }
    private bool CommandIsActive() => command.SelectedItem is CommandRule rule && workspace.SelectedItem is RelayWorkspace folder &&
        CurrentOperations().Any(item => item.Request is { Kind: "command", Scope: { } scope } && scope.ProjectId == ProjectId &&
            scope.SenderId == ActorId && scope.WorkspaceId == folder.Id && item.Request.CommandId == rule.Id && MonitorProjection.InProgress(item.State));
    private string? CancellationKey() => selected is WorkRecord operation && operation.Request.Scope?.ProjectId == ProjectId &&
        (Has(RelayCapabilities.ManageProjectRequests) || Has(RelayCapabilities.ManageSession) && operation.Request.Scope.SenderId == ActorId) &&
        operation.CancellationRequestedAt is null && (MonitorProjection.InProgress(operation.State) || operation.State == "awaiting_approval" ||
        operation.State == "unknown" && operation.ReplyDispatch is not null)
        ? operation.Request.SessionId is { } id ? $"session:{id:D}" : $"request:{operation.Request.Id:D}" : null;
    private string? PartialKey() => selected is WorkRecord { Request.Kind: "transfer" } operation && Has(RelayCapabilities.ManageSession) &&
        operation.Request.Scope?.ProjectId == ProjectId ? $"partial:{operation.Request.Id:D}" : null;
    private string EndpointName(Guid? id) => configuration.Endpoints.SingleOrDefault(item => item.Id == id)?.Name ?? "Agent";
    private async Task<ProjectReply> Call(ProjectAction action) => (await client.RequestAsync(new("project", ProjectAction: action with { ActorId = ActorId }), CancellationToken.None)).Project!;
    private async Task LoadConfiguration()
    {
        try
        {
            configuration = (await client.RequestAsync(new("project", ProjectAction: new("configuration")), CancellationToken.None)).Project!.Configuration!;
            loading = true; var previous = ProjectId; project.Items.Clear(); project.Items.AddRange(configuration.Projects.Where(item => item.Enabled).Cast<object>().ToArray());
            if (project.Items.Count > 0) project.SelectedIndex = Math.Max(0, Array.FindIndex(project.Items.Cast<RelayProject>().ToArray(), item => item.Id == previous));
            loading = false; await SelectProject();
        }
        catch (Exception error) { loading = false; Error(error); }
    }
    private async Task SelectProject()
    {
        Text = project.SelectedItem is RelayProject chosen ? "Agent Relay · " + chosen.Name : "Agent Relay";
        selected = null; taskFilter = null; closeCancellation.Visible = false; output.Clear(); title.Text = "Activité du projet";
        loading = true; actor.Items.Clear(); endpoint.Items.Clear(); workspace.Items.Clear(); session.Items.Clear(); retained.Clear(); more = null; inboxCursor = null; operations.Items.Clear(); inbox.Items.Clear(); events.Items.Clear();
        actor.Items.AddRange(configuration.Endpoints.Where(item => configuration.Memberships.Any(member => member.ProjectId == ProjectId && member.EndpointId == item.Id && member.Role == "main")).Cast<object>().ToArray());
        if (actor.Items.Count > 0) actor.SelectedIndex = 0;
        endpoint.Items.AddRange(configuration.Endpoints.Where(item => configuration.Memberships.Any(member => member.ProjectId == ProjectId && member.EndpointId == item.Id && member.Role != "main")).Cast<object>().ToArray());
        if (endpoint.Items.Count > 0) endpoint.SelectedIndex = 0; loading = false; await SelectEndpoint();
    }
    private async Task SelectEndpoint()
    {
        try
        {
            var selectedSession = (session.SelectedItem as SessionSnapshot)?.Id;
            var selectedWorkspace = (workspace.SelectedItem as RelayWorkspace)?.Id;
            loading = true; pendingActions.Refresh(); workspace.Items.Clear(); session.Items.Clear(); command.Items.Clear();
            if (endpoint.SelectedItem is RelayEndpoint destination)
            {
                workspace.Items.AddRange(configuration.Workspaces.Where(item => item.EndpointId == destination.Id).Cast<object>().ToArray()); if (workspace.Items.Count > 0) workspace.SelectedIndex = Math.Max(0, Array.FindIndex(workspace.Items.Cast<RelayWorkspace>().ToArray(), item => item.Id == selectedWorkspace));
                var binding = new SettingsStore(data).Load().Profiles.SingleOrDefault(item => item.Id == destination.BindingId);
                if (binding is not null) command.Items.AddRange(binding.Commands.Cast<object>().ToArray()); if (command.Items.Count > 0) command.SelectedIndex = 0;
                if (ActorId != Guid.Empty && ProjectId != Guid.Empty)
                {
                    var requestedProject = ProjectId;
                    var requestedActor = ActorId;
                    var response = await Call(new("list_sessions", Query: new(requestedProject, destination.Id, Limit: 100)));
                    if (ProjectId != requestedProject || ActorId != requestedActor || (endpoint.SelectedItem as RelayEndpoint)?.Id != destination.Id)
                    { loading = false; await SelectEndpoint(); return; }
                    session.Items.AddRange(response.Sessions!.Items.Cast<object>().ToArray()); if (session.Items.Count > 0) session.SelectedIndex = Math.Max(0, Array.FindIndex(session.Items.Cast<SessionSnapshot>().ToArray(), item => item.Id == selectedSession));
                }
            }
            loading = false; ShowContext(); await RefreshActivity(true);
        }
        catch (Exception error) { loading = false; Error(error); pendingActions.Refresh(); }
    }
    private void ShowContext()
    {
        context.Text = workspace.SelectedItem is RelayWorkspace selectedWorkspace ? "Dossier : " + selectedWorkspace.Location + " · " + (session.SelectedItem as SessionSnapshot)?.Name : "Déclarez un projet et choisissez une destination. Aucun droit n’est déduit des anciens profils.";
        if (command.SelectedItem is CommandRule rule) context.Text += "\nCommande : " + rule.Program + " " + string.Join(" ", rule.Arguments.Select(value => value.Contains(' ') ? "\"" + value + "\"" : value));
        hints.SetToolTip(context, context.Text);
        pendingActions.Refresh();
    }
    private async Task NewProject()
    {
        using var editor = new RelayProjectEditor(configuration, new SettingsStore(data).Load());
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        try { await client.RequestAsync(new("configure_projects", ProjectsConfiguration: editor.Configuration), CancellationToken.None); await LoadConfiguration(); }
        catch (Exception error) { Error(error); }
    }
    private async Task NewSession()
    {
        var name = AskName("Nom de la nouvelle session"); if (name is null) return;
        try
        {
            if (endpoint.SelectedItem is not RelayEndpoint target || workspace.SelectedItem is not RelayWorkspace folder) throw new ArgumentException("Choisissez une destination et un espace de travail.");
            var requested = SelectionContext();
            status.Text = "Préparation de la session…"; var response = await Call(new("open_session", ProjectId, target.Id, folder.Id, Guid.NewGuid(), DisplayName: name));
            if (SelectionContext() != requested) return;
            await SelectEndpoint(); session.SelectedItem = session.Items.Cast<SessionSnapshot>().SingleOrDefault(item => item.Id == response.Session!.Id); selected = response.Session; Render();
        }
        catch (Exception error) { Error(error); }
    }
    private (Guid Project, Guid Actor, Guid? Endpoint, Guid? Workspace, Guid? Session) SelectionContext() =>
        (ProjectId, ActorId, (endpoint.SelectedItem as RelayEndpoint)?.Id, (workspace.SelectedItem as RelayWorkspace)?.Id,
            (session.SelectedItem as SessionSnapshot)?.Id);
    private async Task Control(string action)
    {
        try
        {
            if (session.SelectedItem is not SessionSnapshot current) throw new ArgumentException("Choisissez une session.");
            var requested = SelectionContext();
            var reply = await Call(new("control_session", SessionId: current.Id, Control: action));
            if (SelectionContext() != requested) return;
            selected = reply.Session; Render(); await SelectEndpoint();
        }
        catch (Exception error) { Error(error); }
    }
    private async Task Send()
    {
        try
        {
            if (session.SelectedItem is not SessionSnapshot current) throw new ArgumentException("Créez ou choisissez une session avant l’envoi.");
            var requested = SelectionContext(); var text = message.Text;
            var response = await Call(new("send_message", SessionId: current.Id, RequestId: Guid.NewGuid(), Text: text));
            if (SelectionContext() != requested) return;
            if (message.Text == text) message.Clear();
            selected = response.Operation; Render(); await RefreshActivity(true);
        }
        catch (Exception error) { Error(error); }
    }
    private async Task RunCommand()
    {
        try
        {
            if (endpoint.SelectedItem is not RelayEndpoint target || workspace.SelectedItem is not RelayWorkspace folder || command.SelectedItem is not CommandRule rule)
                throw new ArgumentException("Choisissez une destination de commandes, son dossier et une commande autorisée.");
            var requested = SelectionContext();
            var response = await Call(new("execute_command", ProjectId, target.Id, folder.Id, RequestId: Guid.NewGuid(), CommandId: rule.Id));
            if (SelectionContext() != requested) return;
            selected = response.Operation; Render(); await RefreshActivity(true);
        }
        catch (Exception error) { Error(error); }
    }
    private async Task Inspect()
    {
        try
        {
            if (session.SelectedItem is not SessionSnapshot current) return;
            var requested = SelectionContext();
            var reply = await Call(new("get_session", SessionId: current.Id));
            if (SelectionContext() == requested) { selected = reply.Session; Render(); }
        }
        catch (Exception error) { Error(error); }
    }
    private async Task Cancel()
    {
        if (selected is not WorkRecord record) return;
        var requestedProject = ProjectId;
        var requestedActor = ActorId;
        try
        {
            var reply = await Call(new("cancel_operation", RequestId: record.Request.Id));
            if (ProjectId == requestedProject && ActorId == requestedActor && (selected as WorkRecord)?.Request.Id == record.Request.Id)
                ShowOperation(reply);
        }
        catch (Exception error) { Error(error); }
    }
    private async Task CloseCancellation()
    {
        if (SelectedCancellation is not { NextAction: "close_session" } progress || selected is not WorkRecord record) return;
        var requestedProject = ProjectId;
        var requestedActor = ActorId;
        try
        {
            await Call(new("control_session", SessionId: progress.SessionId, Control: "close", RequestId: record.Request.Id));
            if (ProjectId == requestedProject && ActorId == requestedActor && (selected as WorkRecord)?.Request.Id == record.Request.Id)
                await ShowOperation(record.Request.Id);
        }
        catch (Exception error) { Error(error); }
    }
    private async Task Receive()
    {
        try { if (ProjectId == Guid.Empty || ActorId == Guid.Empty) return; var requestedProject = ProjectId; var requestedActor = ActorId; var response = await Call(new("read_inbox", Query: new(ProjectId, Cursor: inboxCursor))); if (ProjectId != requestedProject || ActorId != requestedActor) return; foreach (var item in response.Messages!.Items) if (!inbox.Items.Cast<InboxMessage>().Any(existing => existing.Id == item.Id)) inbox.Items.Insert(0, item); inboxCursor = response.Messages.NextCursor; status.Text = response.Messages.Warnings?.FirstOrDefault() ?? "Messages relevés. Aucun contenu reçu n’est exécuté."; }
        catch (Exception error) { Error(error); }
    }
    private async Task ReadEvents()
    {
        try
        {
            var requestedProject = ProjectId; var requestedActor = ActorId;
            var requestedSelection = selected;
            var response = await Call(new("list_events", Query: new(ProjectId, TaskId: (selected as WorkRecord)?.Request.TaskId ?? taskFilter, Limit: 100)));
            if (ProjectId != requestedProject || ActorId != requestedActor || !ReferenceEquals(requestedSelection, selected)) return;
            events.Items.Clear(); events.Items.AddRange(response.Events!.Items.Cast<object>().ToArray()); tabs.SelectedIndex = 2;
            status.Text = response.Events.HasMore ? "100 événements affichés. Filtrez une tâche ou utilisez le curseur MCP pour poursuivre." : "Événements durables chargés.";
        }
        catch (Exception error) { Error(error); }
    }
    private async Task InspectHost()
    {
        try
        {
            if (endpoint.SelectedItem is not RelayEndpoint target) throw new ArgumentException("Choisissez une destination.");
            var requested = SelectionContext(); var response = await Call(new("inspect_host", ProjectId, target.Id));
            if (SelectionContext() != requested) return; selected = response.Host; raw = false; Render();
        }
        catch (Exception error) { Error(error); }
    }
    private async Task Partial(bool remove)
    {
        try
        {
            if (selected is not WorkRecord { Request.Kind: "transfer" } operation) throw new ArgumentException("Sélectionnez une opération de transfert.");
            var requested = SelectionContext(); var response = await Call(new(remove ? "cleanup_transfer" : "inspect_transfer", RequestId: operation.Request.Id));
            if (SelectionContext() != requested || (selected as WorkRecord)?.Request.Id != operation.Request.Id) return;
            selected = response.Partial; raw = false; Render();
        }
        catch (Exception error) { Error(error); }
    }
    private async Task RefreshActivity(bool reset, bool background = false)
    {
        if (loading || ProjectId == Guid.Empty || ActorId == Guid.Empty || (!reset && more is null)) return;
        var contextBeforeWait = SelectionContext();
        if (background && !listRead.Wait(0)) return;
        if (!background) await listRead.WaitAsync();
        polling = true;
        try
        {
            if (contextBeforeWait != SelectionContext() || loading) return;
            var requestedProject = ProjectId; var requestedActor = ActorId;
            var selectedId = (operations.SelectedItem as WorkRecord)?.Request.Id;
            var type = kind.SelectedIndex switch { 1 => "message", 2 => "command", 3 => "transfer", _ => null };
            var state = stateFilter.SelectedIndex switch { 1 => "pending", 2 => "running", 3 => "awaiting_approval", 4 => "completed", 5 => "failed", 6 => "unknown", 7 => "approval_expired", 8 => "retry_wait", 9 => "awaiting_reply", _ => null };
            DateTimeOffset? since = period.SelectedIndex switch { 1 => DateTimeOffset.UtcNow.AddDays(-1), 2 => DateTimeOffset.UtcNow.AddDays(-7), _ => null };
            var response = await Call(new("list_results", Query: new(ProjectId, SessionId: sessionOnly.Checked ? (session.SelectedItem as SessionSnapshot)?.Id : null, TaskId: taskFilter, Kind: type, State: state, Search: search.Text, Since: since, Cursor: reset ? null : more, Limit: 50)));
            if (ProjectId != requestedProject || ActorId != requestedActor) return;
            var hadOlderRows = retained.Count > 50;
            if (reset && !background) retained.Clear();
            foreach (var item in response.Operations!.Items)
            {
                var index = retained.FindIndex(existing => existing.Request.Id == item.Request.Id);
                if (index >= 0) retained[index] = item; else retained.Add(item);
            }
            retained.Sort((a, b) => b.CreatedAt.CompareTo(a.CreatedAt));
            if (!background || !hadOlderRows) more = response.Operations.NextCursor;
            operations.BeginUpdate(); operations.Items.Clear(); operations.Items.AddRange(retained.Cast<object>().ToArray()); operations.EndUpdate();
            if (selectedId is { } id) operations.SelectedIndex = retained.FindIndex(item => item.Request.Id == id);
            if (selected is WorkRecord current) await ShowOperation(current.Request.Id);
        }
        catch (Exception error) { Error(error); }
        finally { polling = false; listRead.Release(); pendingActions.Refresh(); }
    }
    private async Task ShowOperation(Guid id)
    {
        try
        {
            var requestedProject = ProjectId;
            var requestedActor = ActorId;
            var selectedRow = (operations.SelectedItem as WorkRecord)?.Request.Id;
            var selectedDetail = selected;
            var result = await Call(new("get_result", RequestId: id));
            if (ProjectId == requestedProject && ActorId == requestedActor &&
                (operations.SelectedItem as WorkRecord)?.Request.Id == selectedRow && ReferenceEquals(selectedDetail, selected))
                ShowOperation(result);
        }
        catch (Exception error) { Error(error); }
    }
    private CancellationProgress? SelectedCancellation => selected is WorkRecord record &&
        record.Request.Scope is { } scope && scope.ProjectId == ProjectId &&
        (scope.SenderId == ActorId || Has(RelayCapabilities.ManageProjectRequests)) &&
        cancellationRequest == record.Request.Id && record.Request.SessionId == cancellation?.SessionId ? cancellation : null;
    private void ShowOperation(ProjectReply reply)
    {
        selected = reply.Operation;
        cancellationRequest = reply.Operation?.Request.Id;
        cancellation = reply.Cancellation;
        Render();
    }
    private void Render()
    {
        var progress = SelectedCancellation;
        closeCancellation.Visible = progress?.NextAction == "close_session";
        title.Text = selected is WorkRecord work ? State(work.State) + " · " + work.Metadata?.Label : selected is SessionSnapshot named ? named.Name : "Détail du projet";
        output.Text = raw && selected is not null ? System.Text.Json.JsonSerializer.Serialize(selected, selected.GetType(), new System.Text.Json.JsonSerializerOptions(RelayJson.Options) { WriteIndented = true }) : selected switch
        {
            WorkRecord record when sent => record.Request.Text,
            WorkRecord record when record.Error is not null => UserMessages.Error(record.ErrorCode) + "\r\n" + (record.Metadata?.Context ?? ""),
            WorkRecord record => (record.Metadata?.Context ?? "") + "\r\n" + (record.Result?.Output ?? "En attente de confirmation…") + (record.Progress is { } transfer ? $"\r\n{transfer.Bytes:N0} / {transfer.TotalBytes:N0} octets · {transfer.Phase}" : ""),
            InboxMessage received => received.Text,
            SessionSnapshot snapshot => UserMessages.AgentState(snapshot.State) + "\r\n" + snapshot.Diagnostic,
            WorkEvent item => State(item.State) + "\r\nTâche : " + item.TaskId + "\r\nOpération : " + item.RequestId + "\r\nTentative : " + item.AttemptId,
            HostReport host => string.Join("\r\n", host.Facts.Select(item => item.Key + " : " + item.Value)),
            TransferPartial part => part.RelativePath + "\r\n" + (part.Removed ? "Fichier temporaire supprimé. Destination finale conservée." : part.Exists ? $"Fichier temporaire présent : {part.Bytes:N0} octets." : "Aucun fichier temporaire conservé."),
            _ => "Sélectionnez une opération ou un message."
        };
        if (!raw && progress is not null) output.AppendText("\r\n\r\n" + UserMessages.Cancellation(progress.State));
        pendingActions.Refresh();
    }
    private void Error(Exception error) { status.Text = error is ArgumentException ? error.Message : UserMessages.Error(error); }
    private void Copy(object? sender, EventArgs args) { try { if (output.Text.Length > 0) { Clipboard.SetText(output.Text); status.Text = "Contenu copié."; } } catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Presse-papiers occupé. Réessayez dans un instant."; } }
    private static string State(string state) => UserMessages.WorkState(state);
    private static string? AskName(string caption)
    {
        using var dialog = new Form { Text = caption, Size = new Size(450, 165), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        UiTheme.Apply(dialog); var input = new TextBox { Dock = DockStyle.Top, MaxLength = 120 }; var confirm = new Button { Text = "Valider", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom }; dialog.Controls.Add(input); dialog.Controls.Add(confirm); dialog.AcceptButton = confirm;
        return dialog.ShowDialog() == DialogResult.OK && !string.IsNullOrWhiteSpace(input.Text) ? input.Text.Trim() : null;
    }
    private void Settings(object? sender, EventArgs args)
    {
        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => menu.Dispose();
        void Maintenance(string caption, string key, string progress, Func<Task> action, bool available = true)
        {
            var item = new ToolStripMenuItem(pendingActions.Available(key) ? caption : progress)
            { Enabled = available && pendingActions.Available(key) };
            item.Click += async (_, _) => await pendingActions.RunAsync(key, progress, async () => { status.Text = progress; await action(); });
            menu.Items.Add(item);
        }
        Maintenance("Profils techniques", "configuration", "Enregistrement de la configuration…", async () =>
        {
            using var editor = new ProfileEditor(new SettingsStore(data).Load(), data);
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                await client.RequestAsync(new("configure", Configuration: editor.Settings), CancellationToken.None);
                await LoadConfiguration();
            }
            else status.Text = "Aucun enregistrement demandé.";
        });
        Maintenance("Préparer la connexion MCP de ce projet", "configuration", "Préparation de la connexion…", async () =>
        {
            var current = SelectionContext();
            if (current.Actor == Guid.Empty || current.Project == Guid.Empty) throw new ArgumentException("Choisissez un projet et son agent principal.");
            var id = Guid.NewGuid();
            await client.RequestAsync(new("connection", Connection: new(id, current.Actor, [current.Project])), CancellationToken.None);
            var pointer = Path.Combine(data, "mcp-runtime.json");
            var value = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(pointer))!;
            value["connectionId"] = id.ToString("D");
            await File.WriteAllTextAsync(pointer + ".pending", value.ToJsonString());
            File.Move(pointer + ".pending", pointer, true);
            status.Text = "Connexion MCP liée au participant choisi.";
        }, ProjectId != Guid.Empty && ActorId != Guid.Empty);
        var enabled = StartupRegistration.Enabled(data);
        Maintenance(enabled ? "Désactiver le démarrage avec Windows" : "Activer le démarrage avec Windows",
            "startup", "Modification du démarrage…", async () =>
            {
                await Task.Run(() => StartupRegistration.Set(data, !enabled));
                status.Text = !enabled ? "Démarrage avec Windows activé." : "Démarrage avec Windows désactivé.";
            });
        menu.Items.Add("Historique antérieur (lecture seule)", null, (_, _) => { using var archive = new LegacyHistoryWindow(data); archive.ShowDialog(this); });
        menu.Show(this, PointToClient(Cursor.Position));
    }
    protected override void Dispose(bool disposing) { if (disposing) { timer.Dispose(); hints.Dispose(); } base.Dispose(disposing); }
}
