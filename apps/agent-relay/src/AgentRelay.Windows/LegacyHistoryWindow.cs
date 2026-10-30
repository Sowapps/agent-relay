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
        ActivityListStyle.Apply(list, row => row is WorkRecord work ? UserMessages.WorkState(work.State) + " · " + (work.Metadata?.Label ?? work.Request.Kind) : "", row => row is WorkRecord work ? work.CreatedAt.ToLocalTime().ToString("dd/MM HH:mm") + " · " + work.Request.Text.ReplaceLineEndings(" ") : "");
        split.Panel1.Controls.Add(list); split.Panel2.Controls.Add(output); Controls.Add(split);
        var notice = UiTheme.Label("Ces opérations n’ont pas de projet attribué. Aucune relance ni attribution de droits n’est effectuée.", 9, muted: true); notice.Dock = DockStyle.Top; Controls.Add(notice);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46 }; actions.Controls.Add(UiTheme.Button("Copier", (_, _) => { try { if (output.Text.Length > 0) Clipboard.SetText(output.Text); } catch (System.Runtime.InteropServices.ExternalException) { } }));
        var raw = false;
        void Render() { if (list.SelectedItem is WorkRecord work) output.Text = raw ? System.Text.Json.JsonSerializer.Serialize(work, new System.Text.Json.JsonSerializerOptions(RelayJson.Options) { WriteIndented = true }) : work.Result?.Output ?? (work.Error is not null ? UserMessages.Error(work.ErrorCode) : "Résultat non confirmé."); }
        actions.Controls.Add(UiTheme.Button("Détails techniques", (_, _) => { raw = !raw; Render(); })); Controls.Add(actions); list.SelectedIndexChanged += (_, _) => Render();
        Shown += async (_, _) =>
        {
            var records = await new SqliteWorkStore(new RelayDatabase(data)).ListAsync(CancellationToken.None);
            list.Items.AddRange(records.Where(record => record.Request.Scope is null).Cast<object>().ToArray()); if (list.Items.Count > 0) list.SelectedIndex = 0;
        };
    }
}
