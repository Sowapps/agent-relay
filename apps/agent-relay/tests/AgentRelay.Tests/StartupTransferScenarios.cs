using Microsoft.Win32;
using AgentRelay.Windows;

internal static class StartupTransferScenarios
{
    public static void Run(string root)
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
        MigrationEntries(root);
    }

    private static void MigrationEntries(string root)
    {
        var legacy = new StartupEntry(StartupRegistration.Command(@"C:\fixture\legacy"), RegistryValueKind.String);
        var current = new StartupEntry(StartupRegistration.Command(@"C:\fixture\current"), RegistryValueKind.String);
        var exact = new StartupEntry(legacy.Value.ToLowerInvariant(), RegistryValueKind.ExpandString);
        var source = new EntryFixture(legacy, exact);
        var target = new EntryFixture(current, null);
        var unrelated = new EntryFixture(new("unrelated", RegistryValueKind.String), exact);

        source.Apply(new(exact, "disable"));
        var enabled = target.Apply(new(null, "enable")).Entry;
        Check(source.Entry is null && enabled == current && target.Prepared, "migration enables only the new scope");
        target.Apply(new(enabled, "disable"));
        source.Apply(new(null, "restore", exact));
        Check(source.Entry == exact && target.Entry is null && unrelated.Entry == exact && unrelated.Writes == 0,
            "rollback restores exact command and registry kind");

        target.Apply(new(null, "disable"));
        Check(target.Writes == 2, "a disabled entry stays absent without a write");
        Refuses(() => source.Apply(new(null, "disable")));
        Refuses(() => target.Apply(new(null, "restore", exact)));
        Check(source.Entry == exact && target.Entry is null, "mismatched receipts leave both scopes intact");

        Refuses(() => target.Apply(new(null, "enable"), () => target.Entry = current));
        Check(target.Entry == current && target.Writes == 2, "an entry changed during preparation is not overwritten");
        StartupEntry? selected = legacy;
        StartupEntrySnapshot Capture() => new(selected, current, [legacy, current]);
        void Apply(StartupEntryChange change) => StartupRegistration.Change(change, Capture, value => selected = value, () => { });
        Apply(new(legacy, "enable"));
        Check(selected == current, "known legacy command transitions to common entry");
        Apply(new(current, "restore", legacy));
        Check(selected == legacy, "rollback restores known legacy entry");
        Refuses(() => Apply(new(legacy, "restore", new("unknown", RegistryValueKind.String))));
        Check(selected == legacy, "unrelated entry is never restored");
        Console.WriteLine("STARTUP_MIGRATION_ENTRIES=PASS");
        CommonEntryIdentity(root);
    }

    private static void CommonEntryIdentity(string root)
    {
        var data = Path.Combine(root, "startup-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        File.WriteAllText(AgentRelay.Infrastructure.RuntimePaths.TransportSelection(data), "{}");
        var binding = Guid.NewGuid();
        var oldShell = @"C:\previous PowerShell\pwsh.exe";
        var launch = AgentRelay.Infrastructure.McpTransportInstallation.Launch(data, binding) with { Command = oldShell };
        var pointer = AgentRelay.Infrastructure.RuntimePaths.RuntimePointer(data);
        File.WriteAllText(pointer, AgentRelay.Infrastructure.RelayJson.Serialize(new { connectionId = binding, mcpLaunch = launch }));
        var template = AgentRelay.Infrastructure.McpTransportInstallation.HostLaunch(data);
        string Quote(string value) => '"' + value + '"';
        var previous = new StartupEntry(Quote(oldShell) + " " + string.Join(" ", template.Args.Select(Quote)), RegistryValueKind.ExpandString);
        StartupEntry? selected = previous;
        StartupEntrySnapshot Capture() => StartupRegistration.Capture(data, selected);
        StartupRegistration.Change(new(previous, "enable"), Capture, value => selected = value, () => { });
        Check(selected == Capture().EnabledEntry, "prior common-entry shell migrates to current command");
        StartupRegistration.Change(new(selected, "restore", previous), Capture, value => selected = value, () => { });
        Check(selected == previous, "common-entry rollback restores exact value and kind");
        Refuses(() => StartupRegistration.Capture(data, previous with { Value = previous.Value + " -Show" }));
        Refuses(() => StartupRegistration.Capture(data, previous with { Value = previous.Value.Replace("launch.ps1", "foreign.ps1") }));
        Check(StartupRegistration.Capture(data, null).Entry is null, "disabled preference remains absent");
        File.WriteAllText(pointer, AgentRelay.Infrastructure.RelayJson.Serialize(new { connectionId = binding, mcpLaunch = launch with { Args = ["-File", @"C:\foreign.ps1"] } }));
        Refuses(() => StartupRegistration.Capture(data, previous));
        Console.WriteLine("STARTUP_COMMON_ENTRY_IDENTITY=PASS (copied metadata and fake entry writes only)");
    }

    private static void Refuses(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        throw new InvalidOperationException("Expected the startup entry change to be refused.");
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

    private sealed class EntryFixture(StartupEntry enabled, StartupEntry? entry)
    {
        public StartupEntry? Entry { get; set; } = entry;
        public int Writes { get; private set; }
        public bool Prepared { get; private set; }

        public StartupEntrySnapshot Apply(StartupEntryChange change, Action? whilePreparing = null) =>
            StartupRegistration.Change(change, () => new(Entry, enabled), value =>
            {
                Entry = value;
                Writes++;
            }, () =>
            {
                Prepared = true;
                whilePreparing?.Invoke();
            });
    }
}
