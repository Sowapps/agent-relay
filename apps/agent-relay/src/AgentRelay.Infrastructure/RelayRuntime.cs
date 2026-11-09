using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class RelayAlreadyRunningException(Exception inner) : IOException("This data directory already has a relay host.", inner);

public sealed class RelayRuntime : IAsyncDisposable
{
    private readonly FileStream ownership;
    private readonly ExchangeService service;
    private readonly RelayPipeServer server;
    private readonly CancellationTokenSource stop = new();
    private readonly Task listener;
    private readonly MvpCoordinator mvp;
    private readonly ProjectCoordinator projects;
    public event Action? ShowWindowRequested;

    private RelayRuntime(string dataDirectory, Action<Exception> reportError, bool onDemand)
    {
        RuntimePaths.DemandActive(dataDirectory);
        Directory.CreateDirectory(dataDirectory);
        try { ownership = RelayLocation.OpenHostOwnership(dataDirectory); }
        catch (IOException error) when (RelayLocation.IsSharingConflict(error)) { throw new RelayAlreadyRunningException(error); }
        try
        {
            if (onDemand) PromotionAdmission.DemandStartupAllowed(dataDirectory);
            service = new(new SqliteExchangeStore(dataDirectory), new LocalEchoResponder(), [LocalDemo.Route]);
            mvp = new(dataDirectory);
            var connections = new ConnectionStore(dataDirectory); connections.EnsureAdministrator();
            var promotion = new PromotionAdmission(dataDirectory);
            projects = new(dataDirectory, reportError, promotion);
            server = new(RelayLocation.PipeName(dataDirectory), service, reportError, mvp, connections, projects, dataDirectory, () =>
            { var handler = ShowWindowRequested; if (handler is null) return false; handler(); return true; }, promotion);
            listener = server.RunAsync(stop.Token);
        }
        catch
        {
            try { projects?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            finally
            {
                try { mvp?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
                finally { service?.Dispose(); stop.Dispose(); ownership.Dispose(); }
            }
            throw;
        }
    }

    public static RelayRuntime Start(string dataDirectory, Action<Exception> reportError, bool onDemand = false) =>
        new(RelayLocation.Normalize(dataDirectory), reportError, onDemand);
    public Task Completion => listener;

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { await listener.ConfigureAwait(false); }
            finally { await projects.DisposeAsync(); await mvp.DisposeAsync(); server.Dispose(); service.Dispose(); stop.Dispose(); ownership.Dispose(); }
    }
}
