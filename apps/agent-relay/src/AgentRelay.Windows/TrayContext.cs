namespace AgentRelay.Windows;

internal sealed class TrayContext : ApplicationContext
{
    private readonly NotifyIcon icon;
    private readonly ProjectWindow window;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 200 };
    private readonly ContextMenuStrip menu = new();
    public bool HostFailed { get; private set; }

    public TrayContext(LaunchOptions options, Task host)
    {
        window = new(options.DataDirectory);
        if (options.WindowTitle is { Length: > 0 } title) window.Text = title;
        window.QuitRequested += (_, _) => ExitThread();
        menu.Items.Add("Ouvrir Agent Relay", null, (_, _) => Open());
        menu.Items.Add("Quitter", null, (_, _) => ExitThread());
        icon = new NotifyIcon { Text = "Agent Relay", Icon = UiTheme.Icon, ContextMenuStrip = menu, Visible = true };
        icon.DoubleClick += (_, _) => Open();
        var started = Environment.TickCount64;
        timer.Tick += (_, _) =>
        {
            if (host.IsCompleted) { HostFailed = true; ExitThread(); }
            else if (options.LifetimeMilliseconds is { } limit && Environment.TickCount64 - started >= limit) ExitThread();
        };
        timer.Start();
        _ = window.Handle;
        Microsoft.Win32.SystemEvents.SessionEnding += SessionEnding;
        if (options.Show) Open();
    }

    private void Open() { window.Show(); window.WindowState = FormWindowState.Normal; window.Activate(); }
    public void RequestOpen() { if (!window.IsDisposed && window.IsHandleCreated) window.BeginInvoke(new Action(Open)); }
    private void SessionEnding(object? sender, Microsoft.Win32.SessionEndingEventArgs args)
    { if (!window.IsDisposed && window.IsHandleCreated) window.BeginInvoke(new Action(ExitThread)); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Microsoft.Win32.SystemEvents.SessionEnding -= SessionEnding;
            timer.Dispose();
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
            window.Dispose();
        }
        base.Dispose(disposing);
    }
}
