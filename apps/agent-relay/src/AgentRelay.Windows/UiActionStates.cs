namespace AgentRelay.Windows;

/// <summary>Tracks user actions by the resource they operate on and restores current eligibility at completion.</summary>
internal sealed class UiActionStates(Action<Exception> showError)
{
    private readonly Dictionary<string, string> active = new(StringComparer.Ordinal);
    private readonly List<(Button Button, string Caption, Func<string?> Key, Func<string?>? Progress)> controls = [];

    public Button Button(string caption, Func<string?> key, string pending, Func<Task> action, bool primary = false,
        Func<string?>? progress = null)
    {
        var button = UiTheme.Button(caption, async (_, _) => await RunAsync(key(), pending, action), primary);
        controls.Add((button, caption, key, progress));
        Refresh();
        return button;
    }

    public bool Available(string key) => !active.ContainsKey(key);

    public async Task RunAsync(string? key, string pending, Func<Task> action)
    {
        if (key is null || !active.TryAdd(key, pending)) return;
        Refresh();
        try { await action(); }
        catch (Exception error) { showError(error); }
        finally { active.Remove(key); Refresh(); }
    }

    public void Refresh()
    {
        foreach (var (button, caption, target, progress) in controls)
        {
            if (button.IsDisposed) continue;
            var key = target();
            var pending = (key is null ? null : active.GetValueOrDefault(key)) ?? progress?.Invoke();
            button.Enabled = key is not null && pending is null;
            button.Text = pending ?? caption;
            button.AccessibleName = pending is null ? caption : caption + " · " + pending;
        }
    }
}
