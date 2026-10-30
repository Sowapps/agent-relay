using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

internal sealed class RelayProjectEditor : Form
{
    private readonly TextBox name = new(), local = new(), remote = new();
    private readonly ComboBox principal = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly ComboBox agent = new() { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
    private readonly CheckBox localWork = new() { Text = "Autoriser l’agent à modifier les fichiers et lancer ses commandes locales", AutoSize = true };
    private readonly CheckBox transfers = new() { Text = "Autoriser les transferts de fichiers entre ces espaces", AutoSize = true };
    private readonly CheckBox commands = new() { Text = "Autoriser les commandes directes déjà configurées pour WSL", AutoSize = true };
    private readonly ProjectConfiguration original;
    private readonly RelaySettings bindings;
    public ProjectConfiguration? Configuration { get; private set; }

    public RelayProjectEditor(ProjectConfiguration original, RelaySettings bindings)
    {
        this.original = original; this.bindings = bindings; UiTheme.Apply(this);
        Text = "Nouveau projet relayé · Agent Relay"; Size = new Size(820, 660); MinimumSize = Size; StartPosition = FormStartPosition.CenterParent;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 1, AutoScroll = true };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        void Field(string label, Control control)
        { panel.Controls.Add(UiTheme.Label(label, bold: true)); control.Dock = DockStyle.Top; control.Margin = new Padding(0, 0, 0, 12); panel.Controls.Add(control); }
        panel.Controls.Add(UiTheme.Label("Déclarez les participants et leurs espaces de travail", 16, bold: true));
        Field("Nom du projet relayé", name);
        principal.Items.AddRange(original.Endpoints.Where(endpoint => endpoint.AdapterId == "local-files").Cast<object>().ToArray());
        if (principal.Items.Count == 0) principal.Items.Add(new RelayEndpoint(Guid.NewGuid(), "Agent principal Windows", "local-files", "local"));
        principal.SelectedIndex = 0; Field("Main / Principal", principal);
        var localPanel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        localPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); localPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); local.Dock = DockStyle.Fill;
        localPanel.Controls.Add(local); localPanel.Controls.Add(UiTheme.Button("Parcourir", (_, _) => { using var dialog = new FolderBrowserDialog(); if (dialog.ShowDialog(this) == DialogResult.OK) local.Text = dialog.SelectedPath; }));
        Field("Workspace Windows / Espace de travail Windows existant", localPanel);
        agent.Items.AddRange(bindings.Profiles.Where(profile => profile.Client is "codex" or "claude").Cast<object>().ToArray());
        if (agent.Items.Count > 0) agent.SelectedIndex = 0; Field("Secondary / Secondaire", agent);
        Field("Workspace Linux / Espace de travail Linux existant (chemin absolu)", remote);
        panel.Controls.Add(localWork); panel.Controls.Add(transfers); panel.Controls.Add(commands);
        panel.Controls.Add(UiTheme.Label("Aucun dossier n’est copié ou synchronisé. Les autorisations restent propres à ce projet.", 9, muted: true));
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, Margin = new Padding(0, 18, 0, 0) };
        buttons.Controls.Add(UiTheme.Button("Créer le projet", Save, primary: true)); buttons.Controls.Add(UiTheme.Button("Annuler", (_, _) => Close())); panel.Controls.Add(buttons); Controls.Add(panel);
    }

    private void Save(object? sender, EventArgs args)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(name.Text) || principal.SelectedItem is not RelayEndpoint main || agent.SelectedItem is not WslProfile profile)
                throw new ArgumentException("Renseignez le nom et choisissez les deux participants.");
            if (!Directory.Exists(local.Text) || !remote.Text.StartsWith('/') || remote.Text.Any(char.IsControl)) throw new ArgumentException("Choisissez un dossier Windows existant et un chemin Linux absolu. Son existence sera vérifiée avant utilisation.");
            var project = new RelayProject(Guid.NewGuid(), name.Text.Trim()); var secondary = new RelayEndpoint(Guid.NewGuid(), profile.Name, "wsl-agent", profile.Id);
            var localWorkspace = new RelayWorkspace(Guid.NewGuid(), main.Id, "Windows · " + project.Name, Path.GetFullPath(local.Text));
            var remoteWorkspace = new RelayWorkspace(Guid.NewGuid(), secondary.Id, "Linux · " + project.Name, remote.Text.TrimEnd('/'));
            var endpoints = original.Endpoints.ToList(); if (!endpoints.Any(endpoint => endpoint.Id == main.Id)) endpoints.Add(main); endpoints.Add(secondary);
            var workspaces = original.Workspaces.Append(localWorkspace).Append(remoteWorkspace).ToList();
            var mainRights = new List<string> { RelayCapabilities.SendMessage, RelayCapabilities.ReceiveMessage, RelayCapabilities.ReadHistory, RelayCapabilities.ManageSession, RelayCapabilities.ReadFile, RelayCapabilities.InspectHost };
            var targetRights = new List<string> { RelayCapabilities.SendMessage, RelayCapabilities.ReceiveMessage };
            if (localWork.Checked) { mainRights.Add(RelayCapabilities.LocalWork); targetRights.Add(RelayCapabilities.LocalWork); }
            if (transfers.Checked) mainRights.AddRange([RelayCapabilities.WriteFile, RelayCapabilities.ReplaceFile]);
            var access = new List<WorkspaceAccess> { new(localWorkspace.Id, true, transfers.Checked, transfers.Checked), new(remoteWorkspace.Id, true, localWork.Checked || transfers.Checked, transfers.Checked) };
            var memberships = original.Memberships.ToList(); var commandIds = Array.Empty<string>();
            if (commands.Checked)
            {
                var commandBinding = bindings.Profiles.FirstOrDefault(item => item.Client == "command" && item.Distribution == profile.Distribution && item.User == profile.User)
                    ?? throw new ArgumentException("Aucun profil de commandes directes correspondant n’est configuré.");
                var commandEndpoint = new RelayEndpoint(Guid.NewGuid(), "Commandes · " + project.Name, "wsl-command", commandBinding.Id); endpoints.Add(commandEndpoint);
                var commandWorkspace = new RelayWorkspace(Guid.NewGuid(), commandEndpoint.Id, remoteWorkspace.Name, remoteWorkspace.Location); workspaces.Add(commandWorkspace); access.Add(new(commandWorkspace.Id, true));
                memberships.Add(new(project.Id, commandEndpoint.Id, "secondary", [], [])); mainRights.Add(RelayCapabilities.RunCommand); commandIds = commandBinding.Commands.Select(item => item.Id).ToArray();
            }
            memberships.Add(new(project.Id, main.Id, "main", mainRights.ToArray(), access.ToArray(), commandIds));
            memberships.Add(new(project.Id, secondary.Id, "secondary", targetRights.ToArray(), [new(remoteWorkspace.Id, true, localWork.Checked)]));
            Configuration = new(1, original.Projects.Append(project).ToArray(), endpoints.ToArray(), workspaces.ToArray(), memberships.ToArray());
            ProjectPolicy.Validate(Configuration); DialogResult = DialogResult.OK;
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Projet incomplet", MessageBoxButtons.OK, MessageBoxIcon.Information); }
    }
}
