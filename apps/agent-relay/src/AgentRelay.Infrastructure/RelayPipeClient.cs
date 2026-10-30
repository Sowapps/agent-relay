using System.IO.Pipes;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed class RelayPipeClient(string dataDirectory, Guid? connectionId = null)
{
    public Task<RelayExchange> SendAsync(RelayMessage message, CancellationToken token) => CallAsync(new("exchange", message), token);
    public Task<RelayExchange> ReadAsync(Guid id, CancellationToken token) => CallAsync(new("read", RequestId: id), token);

    private async Task<RelayExchange> CallAsync(PipeRequest request, CancellationToken token)
        => (await RequestAsync(request, token)).Exchange ?? throw new InvalidDataException("The host returned no exchange.");

    public async Task<PipeResponse> RequestAsync(PipeRequest request, CancellationToken token)
    {
        if (request.AccessToken is null) request = request with { AccessToken = new ConnectionStore(dataDirectory).Secret(connectionId) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(PipeDeadlines.For(request.Operation) + TimeSpan.FromSeconds(2));
        try
        {
        await using var pipe = new NamedPipeClientStream(".", RelayLocation.PipeName(dataDirectory), PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
        await PipeFrames.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
        var response = await PipeFrames.ReadAsync<PipeResponse>(pipe, deadline.Token).ConfigureAwait(false);
        if (response.Error is not null) throw new RelayException(response.Error.Code, response.Error.Message);
        return response;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new RelayException("relay_timeout", "The relay wait limit expired. Completion may still be pending; inspect the operation or agent status before retrying."); }
    }
}
