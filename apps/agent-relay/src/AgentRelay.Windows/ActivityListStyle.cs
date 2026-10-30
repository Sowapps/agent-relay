namespace AgentRelay.Windows;

internal static class ActivityListStyle
{
    public static void Apply(ListBox list, Func<object, string> title, Func<object, string> subtitle)
    {
        UiTheme.ListStyle(list); list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 64;
        list.DrawItem += (_, args) =>
        {
            if (args.Index < 0 || args.Index >= list.Items.Count) return;
            var row = list.Items[args.Index];
            using var background = new SolidBrush((args.State & DrawItemState.Selected) != 0 ? UiTheme.Selection : UiTheme.Surface);
            args.Graphics.FillRectangle(background, args.Bounds);
            var bounds = new Rectangle(args.Bounds.X + 10, args.Bounds.Y + 9, args.Bounds.Width - 20, 23);
            TextRenderer.DrawText(args.Graphics, title(row), list.Font, bounds, UiTheme.Text, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            bounds.Y += 24;
            TextRenderer.DrawText(args.Graphics, subtitle(row), list.Font, bounds, UiTheme.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
            if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
        };
    }
}
