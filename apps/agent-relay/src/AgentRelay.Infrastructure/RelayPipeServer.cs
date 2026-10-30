using System.IO.Pipes;
using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class RelayPipeServer : IDisposable
{
    private readonly string name;
    private readonly ExchangeService service;
    private readonly Action<Exception> reportError;
    private readonly MvpCoordinator? mvp;
    private readonly ConnectionStore? connections;
    private readonly ProjectCoordinator? projects;
    private readonly string? dataDirectory;
    private readonly Func<bool>? showWindow;
    private NamedPipeServerStream listener;

    public RelayPipeServer(string name, ExchangeService service, Action<Exception> reportError, MvpCoordinator? mvp = null,
        ConnectionStore? connections = null, ProjectCoordinator? projects = null, string? dataDirectory = null, Func<bool>? showWindow = null)
    {
        this.name = name;
        this.service = service;
        this.reportError = reportError;
        this.mvp = mvp;
        this.connections = connections; this.projects = projects;
        this.dataDirectory = dataDirectory;
        this.showWindow = showWindow;
        listener = CreateListener();
    }

    public async Task RunAsync(CancellationToken stop)
    {
        var requests = new List<Task>();
        try
        {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await listener.WaitForConnectionAsync(stop).ConfigureAwait(false);
                var connected = listener;
                listener = CreateListener(first: false);
                requests.RemoveAll(task => task.IsCompleted);
                requests.Add(HandleAsync(connected, stop));
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        }
        }
        finally { await Task.WhenAll(requests).ConfigureAwait(false); }
    }

    private async Task HandleAsync(NamedPipeServerStream connection, CancellationToken stop)
    {
        using (connection)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            try
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(10));
                PipeResponse response;
                try
                {
                    var request = await PipeFrames.ReadAsync<PipeRequest>(connection, deadline.Token).ConfigureAwait(false);
                    deadline.CancelAfter(PipeDeadlines.For(request.Operation));
                    if (request.Version != 1) throw new RelayException("unsupported_version", "Unsupported local protocol version.");
                    var authenticated = connections is null ? null : await connections.AuthenticateAsync(request.AccessToken, deadline.Token);
                    if (request.Operation == "show_window")
                    {
                        if (authenticated is not { Administrator: true }) throw new RelayException("capability_denied", "Only the local application can activate its window.");
                        if (showWindow?.Invoke() != true) throw new RelayException("unavailable", "No application window is attached to this relay host.");
                        response = new();
                    }
                    else if (authenticated is { Administrator: true } && request.Operation is "configure_projects" or "connection" && projects is not null && dataDirectory is not null)
                    {
                        if (request.Operation == "configure_projects" && (projects.Work.IsActive || mvp?.Work.IsActive == true)) throw new RelayException("busy", "Wait for active work before changing central project policy.");
                        response = await ProjectMaintenance.ApplyAsync(dataDirectory, request, deadline.Token);
                    }
                    else if (request.Operation == "project" && request.ProjectAction is not null && projects is not null && authenticated is not null)
                        response = new(Project: await projects.DispatchAsync(authenticated, request.ProjectAction, deadline.Token));
                    else if (authenticated is { Administrator: false }) throw new RelayException("legacy_unassigned", "This connection must use project-scoped operations; legacy records are not implicitly assigned.");
                    else if (authenticated is not null && request.Operation is "submit" or "start_profile" or "open_session" or "session_send" or "session_transfer" or "session_start" or "session_reopen")
                        throw new RelayException("legacy_unassigned", "Select an authorized project and use its scoped operation; legacy profile actions cannot infer project rights.");
                    else if (request.Operation == "configure" && projects?.Work.IsActive == true) throw new RelayException("busy", "Wait for active project work before changing technical endpoint bindings.");
                    else if (request.Operation is not ("exchange" or "read") && mvp is not null)
                        response = await mvp.DispatchAsync(request, deadline.Token).ConfigureAwait(false);
                    else
                    {
                        var exchange = request.Operation switch
                        {
                            "exchange" when request.Message is not null => await service.ExchangeAsync(request.Message, deadline.Token).ConfigureAwait(false),
                            "read" when request.RequestId is { } id && id != Guid.Empty => await service.ReadAsync(id, deadline.Token).ConfigureAwait(false),
                            _ => throw new RelayException("invalid_operation", "The operation or its arguments are invalid.")
                        };
                        response = new(exchange, null);
                    }
                }
                catch (RelayException error) { response = new(null, new(error.Code, error.Message)); }
                catch (Exception error) when (error is JsonException or InvalidDataException)
                { response = new(null, new("invalid_frame", "The local request is malformed.")); }
                catch (Exception error) when (error is not OperationCanceledException and not IOException)
                {
                    reportError(error);
                    response = new(null, new("internal_error", "The local exchange could not be completed."));
                }
                await PipeFrames.WriteAsync(connection, response, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or OperationCanceledException) { reportError(error); }
        }
    }

    private NamedPipeServerStream CreateListener(bool first = true) => new(name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None), 4096, 4096);

    public void Dispose() => listener.Dispose();
}
