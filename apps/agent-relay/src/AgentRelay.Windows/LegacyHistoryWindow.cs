using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

internal sealed class LegacyHistoryWindow : Form
{
    public LegacyHistoryWindow(string data)
    {
        UiTheme.Apply(this); Text = "Historique antérieur · Lecture seule"; Size = new Size(900, 650); StartPosition = FormStartPosition.CenterParent;
        var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(880, 590), SplitterDistance = 340 };
        var list = new ListBox { Dock = DockStyle.Fill }; var output = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both };
        ActivityListStyle.Apply(list, row => row is WorkRecord work ? UserMessages.WorkState(work.State) + " · " + (work.Metadata?.Label ?? work.Request.Kind) : "", row => row is WorkRecord work ? DisplayDateTime.Format(work.CreatedAt) + " · " + work.Request.Text.ReplaceLineEndings(" ") : "");
        split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(output); Controls.Add(split);
        var notice = UiTheme.Label("Chargement de l’historique…", 9, muted: true); notice.Dock = DockStyle.Top; Controls.Add(notice);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46 };
        var copy = UiTheme.Button("Copier", (_, _) =>
        {
            try { if (output.Text.Length > 0) { Clipboard.SetText(output.Text); notice.Text = "Contenu copié."; } }
            catch (System.Runtime.InteropServices.ExternalException) { notice.Text = "Le presse-papiers est occupé. Réessayez."; }
        });
        copy.Enabled = false; actions.Controls.Add(copy);
        var raw = false;
        void Render() { if (list.SelectedItem is WorkRecord work) output.Text = raw ? System.Text.Json.JsonSerializer.Serialize(work, new System.Text.Json.JsonSerializerOptions(RelayJson.Options) { WriteIndented = true }) : work.Result?.Output ?? (work.Error is not null ? UserMessages.Error(work.ErrorCode) : "Résultat non confirmé."); }
        var details = UiTheme.Button("Détails techniques", (_, _) => { raw = !raw; Render(); });
        details.Enabled = false;
        actions.Controls.Add(details); Controls.Add(actions); list.SelectedIndexChanged += (_, _) => Render();
        output.TextChanged += (_, _) => copy.Enabled = output.Text.Length > 0;
        Shown += async (_, _) =>
        {
            try
            {
                var records = await new SqliteWorkStore(new RelayDatabase(data)).ListAsync(CancellationToken.None);
                if (IsDisposed) return;
                list.Items.AddRange(records.Where(record => record.Request.Scope is null).Cast<object>().ToArray());
                details.Enabled = list.Items.Count > 0;
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                notice.Text = list.Items.Count > 0 ? "Opérations antérieures sans projet attribué. Lecture seule." : "Aucune opération antérieure à afficher.";
            }
            catch (Exception error) { if (!IsDisposed) notice.Text = UserMessages.Error(error); }
        };
    }
}
