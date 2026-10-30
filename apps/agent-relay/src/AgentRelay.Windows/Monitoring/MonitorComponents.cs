using AgentRelay.Core;
using System.Drawing.Drawing2D;

namespace AgentRelay.Windows.Monitoring;

internal static class MonitorComponents
{
    public static readonly Color Canvas = UiTheme.Background, Blue = UiTheme.Accent, Ink = UiTheme.Text, Quiet = UiTheme.Muted;
    public static Label Text(string text, float size = 10, bool strong = false, Color? color = null) => new()
    {
        Text = text, AutoSize = true, ForeColor = color ?? Ink, BackColor = Color.Transparent,
        Font = new Font("Segoe UI", size, strong ? FontStyle.Bold : FontStyle.Regular), Margin = new Padding(0, 0, 0, 8)
    };
    public static Button Button(string text, Action action, bool primary = false)
    {
        var button = new MonitorButton { Text = text, Font = new Font("Segoe UI", 9), MinimumSize = new Size(70, 34), Height = 36, AutoSize = true, Padding = new Padding(12, 4, 12, 4), Margin = new Padding(0, 0, 8, 6), BackColor = primary ? Blue : Color.White, ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand };
        button.Click += (_, _) => action(); return button;
    }
    public static FlowLayoutPanel Stack(int width = 700) => new()
    { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Width = width, Margin = Padding.Empty, Padding = Padding.Empty };
    public static FlowLayoutPanel Actions(params Control[] controls)
    {
        var panel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 4, 0, 8), MaximumSize = new Size(1000, 0) };
        panel.Controls.AddRange(controls); return panel;
    }
    public static Control Card(Control child, int width)
    {
        var panel = new MonitorSurface { Width = width, Height = Math.Max(70, child.GetPreferredSize(new Size(width - 36, 0)).Height + 34), BackColor = Color.White, Padding = new Padding(18), Margin = new Padding(0, 0, 0, 14) };
        child.Dock = DockStyle.Fill; panel.Controls.Add(child);
        return panel;
    }
    public static Control Note(string title, string text, int width, bool alert = false)
    {
        var stack = Stack(width - 36); var heading = Text(title, 10, true); var content = Text(text, 9, color: Quiet); content.MaximumSize = new Size(width - 44, 0);
        stack.Controls.Add(heading); stack.Controls.Add(content); stack.PerformLayout();
        var panel = Card(stack, width); if (alert) panel.BackColor = Color.FromArgb(255, 251, 240); return panel;
    }
    public static Control Facts(IEnumerable<(string Label, string Value)> facts, int width)
    {
        var stack = Stack(width - 36);
        foreach (var (label, value) in facts)
        {
            var caption = Text(label, 8, color: Quiet); var content = Text(value, 9); content.MaximumSize = new Size(width - 48, 0);
            stack.Controls.Add(caption); stack.Controls.Add(content);
        }
        stack.PerformLayout(); return Card(stack, width);
    }
    public static TextBox Readable(string text, int width, int maxHeight = 250)
    {
        var measured = TextRenderer.MeasureText(text.Length == 0 ? " " : text, new Font("Segoe UI", 10), new Size(width - 28, int.MaxValue), TextFormatFlags.WordBreak);
        return new TextBox { Text = text.ReplaceLineEndings(Environment.NewLine), ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Ink, Width = width, Height = Math.Clamp(measured.Height + 18, 56, maxHeight), Margin = new Padding(0, 0, 0, 12), AccessibleName = "Contenu sélectionnable" };
    }
    public static string State(string state) => state switch
    { "blocked" => "Bloqué", "idle" => "Sans activité", "closed" => "Fermée", "created" => "Créée", "ready" => "Prête", "preparing" or "starting" => "Préparation", "unknown" => "Résultat incertain", _ => UserMessages.WorkState(state) };
    public static Color StateColor(string state) => state switch
    { "completed" or "ready" => Color.FromArgb(33, 123, 97), "failed" => Color.FromArgb(177, 63, 63), "awaiting_approval" or "unknown" => Color.FromArgb(151, 109, 32), "blocked" => Color.FromArgb(115, 74, 159), _ => Blue };
    public static string Short(string text, int length = 90)
    { var line = text.ReplaceLineEndings(" ").Trim(); return line.Length <= length ? line : line[..length] + "…"; }
    public static string OperationTitle(WorkRecord work) => work.Request.Kind switch
    { "message" => Short(work.Request.Text.Length > 0 ? work.Request.Text : work.Metadata?.Label ?? "Message"), "command" => "Commande · " + work.Request.CommandId, "transfer" => "Transfert · " + (work.Request.Artifact?.Source.RelativePath.Replace('\\', '/').Split('/').LastOrDefault() ?? "fichier"), _ => work.Metadata?.Label ?? work.Request.Kind };
    public static Panel Field(string label, Control control)
    {
        var panel = new Panel { Width = control.Width, Height = 55, Margin = new Padding(0, 0, 8, 6) };
        var caption = Text(label, 8, color: Quiet); caption.Dock = DockStyle.Top; control.Dock = DockStyle.Bottom;
        panel.Controls.Add(control); panel.Controls.Add(caption); return panel;
    }
    public static Control Metrics(int width, params (string Value, string Label)[] metrics)
    {
        var row = new TableLayoutPanel { Width = width, Height = 86, ColumnCount = metrics.Length, RowCount = 1, Margin = new Padding(0, 0, 0, 14) };
        foreach (var (value, label) in metrics)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / metrics.Length));
            var card = new MonitorSurface { Dock = DockStyle.Fill, BackColor = Color.White, Margin = new Padding(0, 0, 10, 0) };
            var number = Text(value, 18, true); number.Location = new Point(16, 8); var caption = Text(label, 9, color: Quiet); caption.Location = new Point(16, 45);
            card.Controls.Add(number); card.Controls.Add(caption); row.Controls.Add(card);
        }
        return row;
    }
}

internal sealed class MonitorSurface : Panel
{
    public MonitorSurface() => DoubleBuffered = true;
    internal static GraphicsPath Shape(Rectangle bounds)
    {
        var path = new GraphicsPath(); const int diameter = 12;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90); path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90); path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure(); return path;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(Parent?.BackColor ?? Monitoring.MonitorComponents.Canvas);
        using var shape = Shape(new Rectangle(0, 0, Math.Max(12, Width - 1), Math.Max(12, Height - 1)));
        using var fill = new SolidBrush(BackColor); using var border = new Pen(UiTheme.Border); e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
    }
}

internal sealed class MonitorButton : Button
{
    public MonitorButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; DoubleBuffered = true; UseVisualStyleBackColor = false; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; e.Graphics.Clear(Parent?.BackColor ?? Monitoring.MonitorComponents.Canvas);
        using var shape = MonitorSurface.Shape(new Rectangle(0, 0, Math.Max(12, Width - 1), Math.Max(12, Height - 1)));
        using var fill = new SolidBrush(BackColor); using var border = new Pen(Focused ? UiTheme.Accent : UiTheme.Border);
        e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -10, -3), Enabled ? ForeColor : SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | (TextAlign == ContentAlignment.MiddleLeft ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter));
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
    }
}

internal sealed class MonitorChoice : ComboBox
{
    public MonitorChoice() { DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 22; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Color.White); using var border = new Pen(Focused ? UiTheme.Accent : UiTheme.Border); e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        TextRenderer.DrawText(e.Graphics, GetItemText(SelectedItem), Font, new Rectangle(8, 0, Math.Max(1, Width - 30), Height), MonitorComponents.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        using var pen = new Pen(MonitorComponents.Quiet); var center = Height / 2; e.Graphics.DrawLines(pen, [new Point(Width - 17, center - 2), new Point(Width - 13, center + 2), new Point(Width - 9, center - 2)]);
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground(); var index = e.Index < 0 ? SelectedIndex : e.Index;
        if (index >= 0 && index < Items.Count) TextRenderer.DrawText(e.Graphics, GetItemText(Items[index]), Font, Rectangle.Inflate(e.Bounds, -4, 0), MonitorComponents.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        e.DrawFocusRectangle();
    }
}

internal sealed class HistoryRow : Button
{
    private readonly string heading, secondary, state, stamp;
    public HistoryRow(string heading, string secondary, string state, DateTimeOffset at, int width, Action action)
    {
        this.heading = heading; this.secondary = secondary; this.state = state; stamp = at.ToLocalTime().ToString("dd/MM HH:mm");
        Text = heading + " · " + secondary + " · " + MonitorComponents.State(state); AccessibleName = Text; Width = width; Height = 92; Margin = new Padding(0, 0, 0, 1);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; BackColor = Color.White; Cursor = Cursors.Hand; Click += (_, _) => action();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Focused || ClientRectangle.Contains(PointToClient(Cursor.Position)) ? UiTheme.Selection : BackColor);
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        using var title = new Font("Segoe UI", 10, FontStyle.Bold); using var small = new Font("Segoe UI", 9);
        TextRenderer.DrawText(e.Graphics, heading, title, new Rectangle(18, 12, Width - 42, 25), MonitorComponents.Ink, flags);
        TextRenderer.DrawText(e.Graphics, secondary, small, new Rectangle(18, 38, Width - 42, 21), MonitorComponents.Quiet, flags);
        TextRenderer.DrawText(e.Graphics, "● " + MonitorComponents.State(state), small, new Rectangle(18, 64, Width - 140, 22), MonitorComponents.StateColor(state), flags);
        TextRenderer.DrawText(e.Graphics, stamp, small, new Rectangle(Width - 120, 64, 100, 22), MonitorComponents.Quiet, flags | TextFormatFlags.Right);
        using var border = new Pen(UiTheme.Border); e.Graphics.DrawLine(border, 0, Height - 1, Width, Height - 1);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width - 6, Height - 6));
    }
}
