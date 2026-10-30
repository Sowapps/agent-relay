using System.Reflection;

namespace AgentRelay.Windows;

internal static class UiTheme
{
    public static readonly Color Background = Color.FromArgb(247, 249, 252);
    public static readonly Color Surface = Color.White;
    public static readonly Color Text = Color.FromArgb(28, 40, 59);
    public static readonly Color Muted = Color.FromArgb(109, 122, 142);
    public static readonly Color Accent = Color.FromArgb(41, 91, 220);
    public static readonly Color Border = Color.FromArgb(221, 228, 238);
    public static readonly Color Selection = Color.FromArgb(232, 239, 255);
    public static readonly Icon Icon = LoadIcon();
    public static readonly Image Logo = LoadLogo();

    private static Image LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AgentRelay.Windows.Assets.agent-relay-logo.png")
            ?? throw new InvalidOperationException("Application logo is missing.");
        using var image = Image.FromStream(stream);
        return new Bitmap(image);
    }

    private static Icon LoadIcon()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AgentRelay.Windows.Assets.agent-relay.ico")
            ?? throw new InvalidOperationException("Application icon is missing.");
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static void Apply(Form form)
    {
        form.Font = new Font("Segoe UI", 10);
        form.BackColor = Background;
        form.ForeColor = Text;
        form.Icon = Icon;
        form.AutoScaleMode = AutoScaleMode.Dpi;
    }

    public static Label Label(string text, float size = 10, bool bold = false, bool muted = false) => new()
    {
        Text = text, AutoSize = true, ForeColor = muted ? Muted : Text,
        Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
        Margin = new Padding(0, 0, 0, 8)
    };

    public static Button Button(string text, EventHandler action, bool primary = false)
    {
        var button = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(92, 38), Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Surface,
            ForeColor = primary ? Color.White : Text, Cursor = Cursors.Hand, Margin = new Padding(0, 0, 8, 0),
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = primary ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(28, 73, 185) : Selection;
        button.Click += action;
        return button;
    }

    public static Panel Card(Control content, int padding = 18)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(padding), Margin = new Padding(0) };
        content.Dock = DockStyle.Fill;
        panel.Controls.Add(content);
        panel.Paint += (_, args) => ControlPaint.DrawBorder(args.Graphics, panel.ClientRectangle, Border, ButtonBorderStyle.Solid);
        panel.SizeChanged += (_, _) => panel.Invalidate();
        return panel;
    }

    public static void ListStyle(ListBox list)
    {
        list.BorderStyle = BorderStyle.None;
        list.BackColor = Surface;
        list.ForeColor = Text;
        list.IntegralHeight = false;
        list.HorizontalScrollbar = true;
        list.ItemHeight = 32;
    }
}
