using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

internal sealed class MvpWindow : Form
{
    private readonly RelayPipeClient client;
    private readonly ComboBox profiles = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DisplayMember = "Name" };
    private readonly ComboBox commands = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DisplayMember = "Name" };
    private readonly TextBox input = new() { Multiline = true, Dock = DockStyle.Fill, MaxLength = 4000, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox output = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both };
    private readonly ListBox history = new() { Dock = DockStyle.Fill, DisplayMember = "Label" };
    private readonly ListBox inbox = new() { Dock = DockStyle.Fill, DisplayMember = "Label" };
    private readonly Label status = UiTheme.Label("Chargement des profils…", 9, muted: true);
    private readonly Label destination = UiTheme.Label("", 9, muted: true);
    private readonly Label modeHint = UiTheme.Label("", 9, muted: true);
    private readonly Label commandLabel = UiTheme.Label("Commande autorisée", bold: true);
    private readonly Label resultTitle = UiTheme.Label("Aucune opération sélectionnée", 12, bold: true);
    private readonly Label resultMeta = UiTheme.Label("Sélectionnez une opération pour consulter son résultat.", 9, muted: true);
    private readonly Panel messagePanel = new() { Dock = DockStyle.Top, Height = 125, Margin = new Padding(0, 12, 0, 8) };
    private readonly Button submit;
    private readonly Button startAgent;
    private readonly Button checkAgent;
    private readonly Label commandContext = UiTheme.Label("", 9, muted: true);
    private readonly Dictionary<string, WslProfile> profileDetails = new();
    private object? selectedDetail;
    private bool rawDetails;
    private bool detailPinned;
    private bool showSentMessage;
    private readonly Button copySent;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 1500 };
    private bool refreshing;
    private Guid? followedOperation;
    private readonly TabControl tabs = new() { Dock = DockStyle.Fill };
    private readonly string dataDirectory;
    private readonly Dictionary<string, string> profileNames = new();
    public event EventHandler? QuitRequested;

    public MvpWindow(string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
        client = new(dataDirectory);
        UiTheme.Apply(this);
        profiles.AccessibleName = "Destination"; commands.AccessibleName = "Commande autorisée";
        input.AccessibleName = "Message à transmettre"; output.AccessibleName = "Résultat";
        history.AccessibleName = "Historique des opérations"; inbox.AccessibleName = "Messages reçus";
        Text = "Agent Relay"; Size = new Size(1100, 790); MinimumSize = new Size(880, 680); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 12), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62)); header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new BrandMark { Margin = new Padding(0, 3, 0, 0) });
        var brand = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, Dock = DockStyle.Fill };
        brand.Controls.Add(UiTheme.Label("Agent Relay", 21, bold: true)); brand.Controls.Add(UiTheme.Label("Commandes et échanges avec vos agents WSL", 10, muted: true));
        header.Controls.Add(brand);
        var headerActions = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Top | AnchorStyles.Right, Margin = new Padding(0, 8, 0, 0) };
        headerActions.Controls.Add(UiTheme.Button("Profils", Configure));
        headerActions.Controls.Add(UiTheme.Button("Quitter", (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty)));
        header.Controls.Add(headerActions); layout.Controls.Add(header);

        var composer = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 1, Margin = new Padding(0) };
        composer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var selectors = new TableLayoutPanel { Dock = DockStyle.Top, Height = 65, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
        selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); selectors.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); selectors.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        selectors.Controls.Add(UiTheme.Label("Destination", bold: true), 0, 0); selectors.Controls.Add(commandLabel, 1, 0);
        profiles.Margin = new Padding(0, 0, 16, 0); commands.Margin = new Padding(0);
        selectors.Controls.Add(profiles, 0, 1); selectors.Controls.Add(commands, 1, 1); composer.Controls.Add(selectors);
        destination.Margin = new Padding(0, 4, 0, 8); composer.Controls.Add(destination);
        commandContext.MaximumSize = new Size(960, 0); commandContext.Margin = new Padding(0, 0, 0, 8); composer.Controls.Add(commandContext);
        input.BorderStyle = BorderStyle.FixedSingle; input.BackColor = Color.FromArgb(250, 251, 254);
        input.PlaceholderText = "Écrivez le message à transmettre à l’agent…";
        var messageLabel = UiTheme.Label("Message", bold: true); messageLabel.Dock = DockStyle.Top;
        messagePanel.Controls.Add(input); messagePanel.Controls.Add(messageLabel); composer.Controls.Add(messagePanel);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        submit = UiTheme.Button("Exécuter la commande", Submit, primary: true);
        startAgent = UiTheme.Button("Démarrer l’agent", StartAgent);
        checkAgent = UiTheme.Button("État de l’agent", CheckAgent);
        actions.Controls.Add(submit); actions.Controls.Add(startAgent); actions.Controls.Add(checkAgent); composer.Controls.Add(actions);
        modeHint.Margin = new Padding(0, 10, 0, 0); composer.Controls.Add(modeHint);
        var composerCard = UiTheme.Card(composer); composerCard.AutoSize = true; composerCard.Dock = DockStyle.Top; composerCard.Margin = new Padding(0, 0, 0, 18);
        layout.Controls.Add(composerCard);

        var results = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); results.RowStyles.Add(new RowStyle(SizeType.Absolute, 45)); results.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var resultsHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        resultsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); resultsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        resultsHeader.Controls.Add(UiTheme.Label("Activité et résultats", 12, bold: true));
        resultsHeader.Controls.Add(UiTheme.Button("Relever les messages WSL", RefreshInbox)); results.Controls.Add(resultsHeader);
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(980, 330), SplitterDistance = 325, SplitterWidth = 12, BackColor = UiTheme.Background, Panel1MinSize = 230, Panel2MinSize = 320 };
        tabs.Padding = new Point(12, 7);
        var resultsTab = new TabPage("Opérations") { BackColor = UiTheme.Surface, Padding = new Padding(8) }; resultsTab.Controls.Add(history);
        var messagesTab = new TabPage("Messages reçus") { BackColor = UiTheme.Surface, Padding = new Padding(8) }; messagesTab.Controls.Add(inbox);
        tabs.TabPages.Add(resultsTab); tabs.TabPages.Add(messagesTab); split.Panel1.Controls.Add(tabs);
        UiTheme.ListStyle(history); UiTheme.ListStyle(inbox); StyleHistory(); StyleInbox();
        output.BorderStyle = BorderStyle.None; output.BackColor = UiTheme.Surface; output.Font = new Font("Consolas", 10); output.WordWrap = false;
        var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); detail.RowStyles.Add(new RowStyle(SizeType.AutoSize)); detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resultMeta.MaximumSize = new Size(650, 0); resultMeta.Margin = new Padding(0, 0, 0, 16);
        var detailActions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 12), WrapContents = true };
        detailActions.Controls.Add(UiTheme.Button("Copier", (_, _) =>
        {
            try { if (output.Text.Length > 0) { Clipboard.SetText(output.Text); status.Text = "Contenu copié."; } }
            catch (System.Runtime.InteropServices.ExternalException) { status.Text = "Le presse-papiers est occupé. Réessayez dans un instant."; }
        }));
        copySent = UiTheme.Button("Message envoyé", (_, _) => { showSentMessage = !showSentMessage; rawDetails = false; RenderDetail(); });
        detailActions.Controls.Add(copySent);
        detailActions.Controls.Add(UiTheme.Button("Détails techniques", (_, _) => { rawDetails = !rawDetails; RenderDetail(); }));
        detail.Controls.Add(resultTitle); detail.Controls.Add(resultMeta); detail.Controls.Add(detailActions); detail.Controls.Add(output);
        detail.SizeChanged += (_, _) => resultMeta.MaximumSize = new Size(Math.Max(120, detail.Width), 0);
        split.Panel2.Controls.Add(UiTheme.Card(detail)); results.Controls.Add(split); layout.Controls.Add(results);
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft; status.Margin = new Padding(0, 10, 0, 0); layout.Controls.Add(status);
        Controls.Add(layout);
        profiles.SelectedIndexChanged += (_, _) => UpdateDestination();
        commands.SelectedIndexChanged += (_, _) => UpdateCommandContext();
        history.SelectedIndexChanged += (_, _) => { if (!refreshing) { detailPinned = false; showSentMessage = false; } ShowResult(this, EventArgs.Empty); };
        inbox.SelectedIndexChanged += (_, _) => ShowInbox();
        tabs.SelectedIndexChanged += (_, _) => { detailPinned = false; showSentMessage = false; if (tabs.SelectedIndex == 0) ShowResult(this, EventArgs.Empty); else ShowInbox(); };
        Shown += async (_, _) => { try { await LoadProfiles(); timer.Start(); await RefreshData(); } catch (Exception error) { ShowError(error); } };
        timer.Tick += async (_, _) => await RefreshData();
        FormClosing += (_, args) => { if (args.CloseReason == CloseReason.UserClosing) { args.Cancel = true; Hide(); } };
    }

    private void UpdateDestination()
    {
        commands.Items.Clear();
        if (profiles.SelectedItem is not WslProfile profile) { submit.Enabled = false; return; }
        var commandMode = profile.Client == "command";
        commands.Items.AddRange(profile.Commands); commands.Enabled = commandMode;
        commands.Visible = commandMode; commandLabel.Visible = commandMode;
        if (commands.Items.Count > 0) commands.SelectedIndex = 0;
        messagePanel.Visible = !commandMode; startAgent.Visible = !commandMode; checkAgent.Visible = !commandMode;
        submit.Text = commandMode ? "Exécuter la commande" : "Envoyer le message";
        submit.Enabled = !commandMode || commands.Items.Count > 0;
        destination.Text = $"{profile.Distribution}  ·  {profile.User}  ·  {(commandMode ? "Exécution directe" : profile.Client == "codex" ? "Agent Codex" : "Agent Claude")}";
        modeHint.Text = commandMode ? "Exécution directe dans WSL, sans appel à un agent." : "Démarrez l’agent avant le premier envoi. Sa réponse sera conservée ci-dessous.";
        commandContext.Visible = commandMode; UpdateCommandContext();
    }

    private void UpdateCommandContext()
    {
        if (profiles.SelectedItem is WslProfile profile && commands.SelectedItem is CommandRule command)
            commandContext.Text = $"Dossier : {AgentControl.Context(profile).Workspace}\nCommande : {command.Program} {string.Join(" ", command.Arguments.Select(argument => argument.Contains(' ') ? $"\"{argument}\"" : argument))}";
    }

    private void StyleHistory()
    {
        history.DrawMode = DrawMode.OwnerDrawFixed; history.ItemHeight = 64;
        history.DrawItem += (_, args) =>
        {
            if (args.Index < 0 || history.Items[args.Index] is not HistoryRow row) return;
            var selected = (args.State & DrawItemState.Selected) != 0;
            using var brush = new SolidBrush(selected ? UiTheme.Selection : UiTheme.Surface); args.Graphics.FillRectangle(brush, args.Bounds);
            var bounds = new Rectangle(args.Bounds.X + 10, args.Bounds.Y + 10, args.Bounds.Width - 20, 22);
            TextRenderer.DrawText(args.Graphics, $"{State(row.Work.State)} · {row.Work.Label ?? row.ProfileName}", Font, bounds, UiTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            bounds.Y += 23;
            TextRenderer.DrawText(args.Graphics, $"{row.Work.CreatedAt.ToLocalTime():dd/MM HH:mm}  ·  {row.Preview}", Font, bounds, UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
        };
    }

    private void ShowInbox()
    {
        if (tabs.SelectedIndex != 1) return;
        if (inbox.SelectedItem is not InboxRow row) { selectedDetail = null; resultTitle.Text = "Aucun message sélectionné"; resultMeta.Text = "Les messages relevés depuis WSL apparaissent ici."; RenderDetail(); return; }
        resultTitle.Text = profileNames.GetValueOrDefault(row.Message.ProfileId, row.Message.Sender);
        resultMeta.Text = $"{(row.Message.InReplyTo is null ? "Message spontané" : "Réponse à une demande")} · {row.Message.Sender} → {row.Message.Recipient} · {row.Message.ReceivedAt.ToLocalTime():dd/MM HH:mm}";
        if (row.Message.SessionId is { } sessionId) resultMeta.Text += "\nSession : " + sessionId;
        selectedDetail = row.Message; RenderDetail();
    }

    private void StyleInbox()
    {
        inbox.DrawMode = DrawMode.OwnerDrawFixed; inbox.ItemHeight = 64;
        inbox.DrawItem += (_, args) =>
        {
            if (args.Index < 0 || inbox.Items[args.Index] is not InboxRow row) return;
            using var brush = new SolidBrush((args.State & DrawItemState.Selected) != 0 ? UiTheme.Selection : UiTheme.Surface);
            args.Graphics.FillRectangle(brush, args.Bounds);
            var bounds = new Rectangle(args.Bounds.X + 10, args.Bounds.Y + 10, args.Bounds.Width - 20, 22);
            TextRenderer.DrawText(args.Graphics, $"{(row.Message.InReplyTo is null ? "Message spontané" : "Réponse")} · {profileNames.GetValueOrDefault(row.Message.ProfileId, row.Message.Sender)}", Font, bounds, UiTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            bounds.Y += 23;
            TextRenderer.DrawText(args.Graphics, $"{row.Message.ReceivedAt.ToLocalTime():dd/MM HH:mm} · {row.Message.Text.ReplaceLineEndings(" ")}", Font, bounds, UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
        };
    }

    private void RenderDetail()
    {
        copySent.Enabled = selectedDetail is WorkRecord { Request.Kind: "message" };
        var text = rawDetails && selectedDetail is Exception exception ? exception.ToString() :
            rawDetails && selectedDetail is not null ? System.Text.Json.JsonSerializer.Serialize(selectedDetail, selectedDetail.GetType(), new System.Text.Json.JsonSerializerOptions(RelayJson.Options) { WriteIndented = true }) :
            showSentMessage && selectedDetail is WorkRecord sent ? sent.Request.Text : selectedDetail switch
        {
            WorkRecord work => work.Error is not null ? UserMessages.Error(work.ErrorCode ?? (work.Error.Contains("unavailable:", StringComparison.Ordinal) ? "unavailable" : null)) :
                work is { Request.Kind: "transfer", Result.Details: { } receipt } ? "Transfert terminé\nWindows : " + receipt.GetValueOrDefault("windowsPath") +
                    "\nWSL : " + receipt.GetValueOrDefault("linuxPath") + "\nTaille : " + receipt.GetValueOrDefault("bytes") + " octets\nSHA-256 : " + receipt.GetValueOrDefault("sha256") : work.Result?.Output ?? "En attente de confirmation…",
            InboxMessage message => message.Text,
            AgentStatus agent => UserMessages.AgentState(agent.State),
            Exception error => UserMessages.Error(error),
            _ => ""
        };
        if (!rawDetails && selectedDetail is WorkRecord { Result.ErrorOutput.Length: > 0 }) text += "\n\nDiagnostic disponible dans les détails techniques.";
        text = text.ReplaceLineEndings("\r\n");
        if (output.Text != text) output.Text = text;
    }

    private void ShowError(Exception error)
    {
        detailPinned = true; showSentMessage = false;
        status.Text = UserMessages.Error(error);
        resultTitle.Text = "Action non confirmée"; resultMeta.Text = "Les détails techniques restent accessibles.";
        selectedDetail = error; rawDetails = false; RenderDetail();
    }

    private async Task LoadProfiles()
    {
        var response = await client.RequestAsync(new("configuration"), CancellationToken.None);
        profiles.Items.Clear(); profiles.Items.AddRange(response.Configuration!.Profiles);
        profileNames.Clear(); foreach (var profile in response.Configuration.Profiles) profileNames[profile.Id] = profile.Name;
        profileDetails.Clear(); foreach (var profile in response.Configuration.Profiles) profileDetails[profile.Id] = profile;
        if (profiles.Items.Count > 0) profiles.SelectedIndex = 0;
        status.Text = profiles.Items.Count == 0 ? "Ajoutez vos profils WSL avec le bouton Profils." : "Prêt. Choisissez une destination.";
    }

    private async void Submit(object? sender, EventArgs args)
    {
        try
        {
            if (profiles.SelectedItem is not WslProfile profile) throw new InvalidOperationException("Choisissez un profil.");
            var request = new WorkRequest(Guid.NewGuid(), profile.Id, profile.Client == "command" ? "command" : "message", profile.Client == "command" ? "" : input.Text,
                (commands.SelectedItem as CommandRule)?.Id);
            var response = await client.RequestAsync(new("submit", Work: request), CancellationToken.None);
            detailPinned = false; showSentMessage = false;
            followedOperation = response.Work!.Request.Id;
            tabs.SelectedIndex = 0;
            status.Text = "Opération enregistrée. Son résultat apparaîtra ci-dessous.";
            resultTitle.Text = "Opération enregistrée"; resultMeta.Text = $"Identifiant : {response.Work!.Request.Id}"; output.Text = "En attente de confirmation…";
            await RefreshData();
        }
        catch (Exception error) { ShowError(error); }
    }

    private async void StartAgent(object? sender, EventArgs args)
    {
        try
        {
            if (profiles.SelectedItem is not WslProfile profile || profile.Client == "command") return;
            status.Text = "Ouverture de la session dédiée…";
            var response = await client.RequestAsync(new("start_profile", ProfileId: profile.Id), CancellationToken.None);
            ShowAgent(response.Agent!);
        }
        catch (Exception error) { ShowError(error); }
    }

    private async void CheckAgent(object? sender, EventArgs args)
    {
        try { if (profiles.SelectedItem is WslProfile profile && profile.Client != "command") ShowAgent((await client.RequestAsync(new("agent_status", ProfileId: profile.Id), CancellationToken.None)).Agent!); }
        catch (Exception error) { ShowError(error); }
    }

    private void ShowAgent(AgentStatus agent)
    {
        detailPinned = true; showSentMessage = false;
        status.Text = UserMessages.AgentState(agent.State); resultTitle.Text = $"État de l’agent · {profileNames.GetValueOrDefault(agent.ProfileId, agent.Client)}";
        resultMeta.Text = $"{agent.Distribution} · {agent.User} · {agent.Workspace}";
        selectedDetail = agent; rawDetails = false; RenderDetail();
    }

    private async void Configure(object? sender, EventArgs args)
    {
        try
        {
            var current = await client.RequestAsync(new("configuration"), CancellationToken.None);
            using var editor = new ProfileEditor(current.Configuration!, dataDirectory);
            if (editor.ShowDialog(this) != DialogResult.OK) return;
            await client.RequestAsync(new("configure", Configuration: editor.Settings), CancellationToken.None);
            await LoadProfiles();
        }
        catch (Exception error) { ShowError(error); }
    }

    private async void RefreshInbox(object? sender, EventArgs args)
    {
        try { status.Text = "Lecture des messages et confirmations WSL…"; var response = await client.RequestAsync(new("refresh_inbox"), CancellationToken.None);
            status.Text = response.Warnings?.FirstOrDefault() ?? "Messages et confirmations récupérés."; await RefreshData(); }
        catch (Exception error) { ShowError(error); }
    }

    private async Task RefreshData()
    {
        if (refreshing || IsDisposed) return;
        refreshing = true;
        try
        {
            var records = await client.RequestAsync(new("history"), CancellationToken.None);
            var previous = followedOperation ?? (history.SelectedItem as HistoryRow)?.Work.Id ?? records.History!.FirstOrDefault()?.Id;
            var rows = records.History!.Select(work => new HistoryRow(work, profileNames.GetValueOrDefault(work.ProfileId, work.ProfileId))).ToArray();
            if (!history.Items.Cast<HistoryRow>().SequenceEqual(rows))
            {
                history.BeginUpdate();
                try { history.Items.Clear(); history.Items.AddRange(rows); }
                finally { history.EndUpdate(); }
            }
            if (previous is { } id) history.SelectedIndex = Array.FindIndex(rows, row => row.Work.Id == id);
            followedOperation = null;
            if (tabs.SelectedIndex == 0) ShowResult(this, EventArgs.Empty);
            var received = await client.RequestAsync(new("inbox"), CancellationToken.None);
            var selectedMessage = (inbox.SelectedItem as InboxRow)?.Message.Id;
            var messages = received.Inbox!.Select(message => new InboxRow(message)).ToArray();
            if (!inbox.Items.Cast<InboxRow>().SequenceEqual(messages))
            {
                inbox.BeginUpdate();
                try { inbox.Items.Clear(); inbox.Items.AddRange(messages); }
                finally { inbox.EndUpdate(); }
            }
            if (selectedMessage is { } messageId) inbox.SelectedIndex = Array.FindIndex(inbox.Items.Cast<InboxRow>().ToArray(), row => row.Message.Id == messageId);
            else if (inbox.Items.Count > 0) inbox.SelectedIndex = 0;
        }
        catch (Exception error) { if (!IsDisposed) ShowError(error); }
        finally { refreshing = false; }
    }

    private async void ShowResult(object? sender, EventArgs args)
    {
        if (detailPinned || tabs.SelectedIndex != 0 || history.SelectedItem is not HistoryRow row) return;
        try
        {
            var response = await client.RequestAsync(new("result", RequestId: row.Work.Id), CancellationToken.None);
            var work = response.Work!;
            if (IsDisposed || tabs.SelectedIndex != 0 || (history.SelectedItem as HistoryRow)?.Work.Id != row.Work.Id) return;
            resultTitle.Text = $"{State(work.State)} · {row.Work.Label ?? profileNames.GetValueOrDefault(work.Request.ProfileId, work.Request.ProfileId)}";
            resultMeta.Text = $"{work.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}  ·  {(work.Request.Kind switch { "command" => "Commande WSL", "transfer" => "Transfert de fichier", _ => "Message à l’agent" })}" +
                (work.Result?.ExitCode is { } code ? $"  ·  Code de sortie : {code}" : "") + $"\nIdentifiant : {work.Request.Id}";
            if (work.Metadata?.Context is { } context) resultMeta.Text += "\nDossier : " + context;
            else if (work.Request.Kind == "command")
            {
                var workspace = work.Result?.Details?.GetValueOrDefault("workspace") ?? (profileDetails.TryGetValue(work.Request.ProfileId, out var profile) ? AgentControl.Context(profile).Workspace : "Non confirmé");
                resultMeta.Text += "\nDossier : " + workspace;
            }
            if (work.Request.SessionId is { } sessionId) resultMeta.Text += "\nSession : " + sessionId;
            selectedDetail = work; RenderDetail();
        }
        catch (Exception error) { ShowError(error); }
    }

    private static string State(string value) => value switch { "pending" => "En attente", "running" => "En cours", "completed" => "Terminé", "failed" => "Échec", _ => "À confirmer" };
    private sealed record HistoryRow(WorkSummary Work, string ProfileName)
    {
        public string Preview => string.IsNullOrWhiteSpace(Work.Preview) ? Work.Label ?? (Work.Kind == "command" ? "Commande WSL" : "Message à l’agent") : Work.Preview;
        public string Label => $"{State(Work.State)} · {ProfileName} · {Preview}";
    }
    private sealed record InboxRow(InboxMessage Message) { public string Label => $"{Message.ProfileId} · {Message.Sender} · {Message.Text}"; }
    protected override void Dispose(bool disposing) { if (disposing) timer.Dispose(); base.Dispose(disposing); }
}
