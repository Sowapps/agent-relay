using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record NativeAdapters(IRelaySessions Sessions, IScopedWorkAdapter Executor);

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
        var fileAdapters = new Dictionary<string, FileWorkspaceKind>(StringComparer.Ordinal)
        {
            ["local-files"] = FileWorkspaceKind.Host, ["wsl-agent"] = FileWorkspaceKind.Wsl, ["wsl-command"] = FileWorkspaceKind.Wsl
        };
        var routes = new ScopedFileRoutes(database, technical, projects, bridge, files, fileAdapters);
        var executor = new ScopedWorkExecutor(database, projects, sessions, bridge, store, files, routes, inbox, commands, context);
        var adapter = new ScopedSessions(sessions, agents, projects, journal, inbox, context, active);
        var installed = new Dictionary<string, IRelaySessions>(StringComparer.Ordinal) { ["wsl-agent"] = adapter };
        var router = new SessionAdapterRouter(adapter, installed,
            async (id, token) => (await context.RecordAsync(id, token)).Definition!.Endpoint.AdapterId);
        var work = new ScopedWorkRouter(projects, new Dictionary<string, IScopedWorkAdapter>(StringComparer.Ordinal)
        {
            ["wsl-agent"] = executor, ["wsl-command"] = executor
        }, executor);
        return new(router, work);
    }
}
