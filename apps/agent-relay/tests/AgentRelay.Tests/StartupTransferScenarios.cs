using AgentRelay.Windows;

internal static class StartupTransferScenarios
{
    public static void Run()
    {
        var events = new List<string>();
        var source = new Preference("source", true, events);
        var target = new Preference("target", false, events);
        var preview = StartupPreferenceTransfer.Run(source, target, apply: false);
        Check(preview == new StartupTransferResult(true, false, false) && events.Count == 0, "preview is read-only");

        var applied = StartupPreferenceTransfer.Run(source, target, apply: true);
        Check(applied == new StartupTransferResult(false, true, true) &&
            events.SequenceEqual(new[] { "target:True", "source:False" }), "enable and verify target before disabling source");
        Check(StartupPreferenceTransfer.Run(source, target, apply: true) == new StartupTransferResult(false, true, false)
            && events.Count == 2, "repeated transfer preserves the target");

        events.Clear();
        source = new("source", false, events);
        target = new("target", false, events);
        Check(StartupPreferenceTransfer.Run(source, target, apply: true) == new StartupTransferResult(false, false, false)
            && events.Count == 0, "disabled preference stays disabled");

        source = new("source", true, events) { FailDisable = true };
        try
        {
            StartupPreferenceTransfer.Run(source, target, apply: true);
            throw new InvalidOperationException("Expected the injected legacy removal failure.");
        }
        catch (IOException)
        {
            Check(source.Read().Enabled == true && target.Read().Enabled == false &&
                events.SequenceEqual(new[] { "target:True", "source:False", "target:False" }), "rollback preserves legacy preference");
        }
        Console.WriteLine("STARTUP_PREFERENCE_TRANSFER=PASS");
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Preference(string name, bool enabled, List<string> events) : IStartupPreference
    {
        public bool FailDisable { get; init; }

        public StartupPreferenceState Read() => new(enabled);

        public StartupPreferenceState Set(bool value)
        {
            events.Add($"{name}:{value}");
            if (!value && FailDisable) throw new IOException("Injected startup entry removal failure.");
            enabled = value;
            return Read();
        }
    }
}
