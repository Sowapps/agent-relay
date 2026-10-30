using System.Drawing.Drawing2D;

namespace AgentRelay.Windows;

internal sealed class BrandMark : Control
{
    public BrandMark()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(48, 48);
        AccessibleName = "Agent Relay";
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        args.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        args.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        args.Graphics.DrawImage(UiTheme.Logo, ClientRectangle);
    }
}
