using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record NativeAdapters(ScopedSessions Sessions, ScopedWorkExecutor Executor);

// Single composition owner used by production and native-adapter integration checks.
public static class NativeAdapterComposition
{
    public static NativeAdapters Create(string data, SettingsStore technical, ProjectSettingsStore projects,
        SqliteWorkStore store, Func<Guid, bool> active)
    {
        var database = new RelayDatabase(data);
        var bridge = new WslBridge();
        var agents = new AgentControl(technical, bridge);
        var sessions = new SessionService(data, technical, agents, bridge);
        var context = new ScopedSessionContext(projects, sessions);
        var journal = new ProjectJournal(database);
        var inbox = new ScopedInbox(sessions, bridge, store, journal, new(database, "cursors"), context);
        var commands = new ScopedCommands(data, technical, projects, bridge, new(database, "dispatches"));
        var files = new FileTransferService(sessions, bridge, database, store, technical.Load().Transfers, data);
        var executor = new ScopedWorkExecutor(projects, sessions, bridge, store, files, inbox, commands, context);
        var adapter = new ScopedSessions(sessions, agents, projects, journal, inbox, context, active);
        return new(adapter, executor);
    }
}
