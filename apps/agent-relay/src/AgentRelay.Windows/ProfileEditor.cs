using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

internal sealed class ProfileEditor : Form
{
    private readonly ListBox profiles = new() { Dock = DockStyle.Fill, DisplayMember = "Name" };
    private readonly TextBox name = new(), distribution = new(), user = new(), root = new(), executable = new() { PlaceholderText = "Facultatif : chemin Linux absolu du client" };
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox program = new() { Text = "/bin/ls" }, arguments = new() { Text = "-1", Multiline = true, Height = 65 };
    private string? selectedId;
    private readonly CheckBox localWork = new() { Text = "Autoriser le travail local : fichiers et commandes du projet", AutoSize = true };
    private readonly string dataDirectory;
    private readonly int maximumConcurrency;
    private readonly TransferLimits? transfers;
    internal RelaySettings Settings => new(1, profiles.Items.Cast<WslProfile>().ToArray(), maximumConcurrency, transfers);

    public ProfileEditor(RelaySettings settings, string dataDirectory)
    {
        this.dataDirectory = dataDirectory;
        maximumConcurrency = settings.MaximumConcurrency;
        transfers = settings.Transfers;
        UiTheme.Apply(this);
        Text = "Profils · Agent Relay"; Size = new Size(980, 760); MinimumSize = new Size(880, 680); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 3 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        var heading = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        heading.Controls.Add(UiTheme.Label("Profils WSL", 19, bold: true)); heading.Controls.Add(UiTheme.Label("Choisissez une destination et ses autorisations.", muted: true)); layout.Controls.Add(heading);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 16) };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var listPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 16, 0) };
        listPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); listPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        UiTheme.ListStyle(profiles); listPanel.Controls.Add(profiles);
        listPanel.Controls.Add(UiTheme.Button("Nouveau profil", (_, _) => { selectedId = null; profiles.ClearSelected(); name.Clear(); root.Clear(); localWork.Checked = false; distribution.Text = "Ubuntu-24.04"; user.Text = "sowapps"; kind.SelectedIndex = 0; }));
        var listCard = UiTheme.Card(listPanel); listCard.Margin = new Padding(0, 0, 16, 0); body.Controls.Add(listCard, 0, 0);
        var fieldsPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
        fieldsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var fields = new (string Label, Control Field)[] { ("Nom", name), ("Type", kind), ("Distribution", distribution), ("Utilisateur WSL", user),
            ("Dossier dédié /srv/agent-relay-*", root), ("Exécutable natif (agent)", executable), ("Programme autorisé (commande)", program), ("Arguments : un par ligne", arguments) };
        for (var index = 0; index < fields.Length; index++)
        {
            var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 14) };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.Controls.Add(UiTheme.Label(fields[index].Label, 9, bold: true)); fields[index].Field.Dock = DockStyle.Top;
            fields[index].Field.Margin = new Padding(0); panel.Controls.Add(fields[index].Field); fieldsPanel.Controls.Add(panel);
        }
        kind.Items.AddRange(new object[] { new ClientOption("command", "Commandes WSL"), new ClientOption("codex", "Agent Codex"), new ClientOption("claude", "Agent Claude") });
        localWork.Margin = new Padding(0, 4, 0, 8); fieldsPanel.Controls.Add(localWork);
        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true }; scroll.Controls.Add(fieldsPanel); body.Controls.Add(UiTheme.Card(scroll), 1, 0); layout.Controls.Add(body);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(UiTheme.Button("Préparer un espace WSL dédié", Prepare));
        var saveButtons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Dock = DockStyle.Fill };
        saveButtons.Controls.Add(UiTheme.Button("Appliquer au profil", Apply));
        saveButtons.Controls.Add(UiTheme.Button("Enregistrer et fermer", (_, _) => { if (!ApplyCurrent()) return; try { SettingsStore.Validate(Settings); DialogResult = DialogResult.OK; } catch (Exception error) { MessageBox.Show(this, error.Message); } }, primary: true));
        buttons.Controls.Add(saveButtons); layout.Controls.Add(buttons);
        kind.SelectedIndexChanged += (_, _) => { var commandMode = SelectedClient == "command"; program.Enabled = commandMode; arguments.Enabled = commandMode; localWork.Enabled = !commandMode; };
        profiles.Items.AddRange(settings.Profiles); profiles.SelectedIndexChanged += Select;
        Controls.Add(layout);
        if (profiles.Items.Count > 0) profiles.SelectedIndex = 0;
    }

    private void Select(object? sender, EventArgs args)
    {
        if (profiles.SelectedItem is not WslProfile profile) return;
        selectedId = profile.Id; name.Text = profile.Name; distribution.Text = profile.Distribution; user.Text = profile.User;
        root.Text = profile.Root; kind.SelectedItem = kind.Items.Cast<ClientOption>().Single(item => item.Id == profile.Client);
        localWork.Checked = profile.AllowLocalWork; executable.Text = profile.NativeExecutable ?? "";
        program.Text = profile.Commands.FirstOrDefault()?.Program ?? "/bin/ls"; arguments.Lines = profile.Commands.FirstOrDefault()?.Arguments ?? ["-1"];
    }
    private void Apply(object? sender, EventArgs args)
    { ApplyCurrent(); }

    private bool ApplyCurrent()
    {
        try
        {
            var id = selectedId ?? "profile-" + Guid.NewGuid().ToString("N")[..8];
            var type = SelectedClient;
            var previous = Settings.Profiles.SingleOrDefault(profile => profile.Id == id);
            var previousCommands = previous?.Commands ?? [];
            var profile = new WslProfile(id, name.Text, distribution.Text, user.Text, root.Text, type,
                type == "command" ? new[] { new CommandRule(previousCommands.FirstOrDefault()?.Id ?? "list", previousCommands.FirstOrDefault()?.Name ?? "Commande autorisée", program.Text,
                    arguments.Lines.Where(line => line.Length > 0).ToArray()) }.Concat(previousCommands.Skip(1)).ToArray() : [], localWork.Checked, previous?.Workspace, string.IsNullOrWhiteSpace(executable.Text) ? null : executable.Text.Trim());
            var proposed = Settings.Profiles.Where(item => item.Id != id).Append(profile).ToArray();
            SettingsStore.Validate(new(1, proposed));
            var index = profiles.SelectedIndex;
            if (selectedId is not null && index >= 0) profiles.Items[index] = profile; else profiles.Items.Add(profile);
            profiles.SelectedItem = profile;
            return true;
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Profil à corriger"); return false; }
    }

    private async void Prepare(object? sender, EventArgs args)
    {
        if (sender is not Button button) return;
        button.Enabled = false;
        try
        {
            var prepared = await WslProvisioner.PrepareAsync(dataDirectory, string.IsNullOrWhiteSpace(distribution.Text) ? "Ubuntu-24.04" : distribution.Text,
                string.IsNullOrWhiteSpace(user.Text) ? "sowapps" : user.Text, CancellationToken.None);
            profiles.Items.AddRange(prepared.Profiles);
            profiles.SelectedItem = prepared.Profiles[0];
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Préparation WSL"); }
        finally { button.Enabled = true; }
    }

    private string SelectedClient => (kind.SelectedItem as ClientOption)?.Id ?? "command";
    private sealed record ClientOption(string Id, string Label) { public override string ToString() => Label; }
}
