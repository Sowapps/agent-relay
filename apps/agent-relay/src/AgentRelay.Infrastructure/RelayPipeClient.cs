using System.IO.Pipes;
using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

internal sealed class RelayHostNotListeningException : Exception { }

public sealed class RelayPipeClient(string dataDirectory, Guid? connectionId = null)
{
    public Task<RelayExchange> SendAsync(RelayMessage message, CancellationToken token) => CallAsync(new("exchange", message), token);
    public Task<RelayExchange> ReadAsync(Guid id, CancellationToken token) => CallAsync(new("read", RequestId: id), token);

    private async Task<RelayExchange> CallAsync(PipeRequest request, CancellationToken token)
        => (await RequestAsync(request, token)).Exchange ?? throw new InvalidDataException("The host returned no exchange.");

    public async Task<PipeResponse> RequestAsync(PipeRequest request, CancellationToken token)
        => (await RequestRawAsync(request, token)).Deserialize<PipeResponse>(RelayJson.Options)
            ?? throw new JsonException("The host returned no response envelope.");

    public Task<JsonElement> RequestRawAsync(PipeRequest request, CancellationToken token) => RequestRawAsync(request, token, null);

    internal async Task<JsonElement> RequestRawAsync(PipeRequest request, CancellationToken token, int? connectionTimeoutMs)
    {
        if (request.AccessToken is null) request = request with { AccessToken = ConnectionStore.ReadSecret(dataDirectory, connectionId) };
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(PipeDeadlines.For(request.Operation) + TimeSpan.FromSeconds(2) +
            TimeSpan.FromMilliseconds(connectionTimeoutMs ?? 0));
        try
        {
            await using var pipe = new NamedPipeClientStream(".", RelayLocation.PipeName(dataDirectory), PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            if (connectionTimeoutMs is { } limit)
            {
                try { await pipe.ConnectAsync(limit, deadline.Token).ConfigureAwait(false); }
                catch (TimeoutException) { throw new RelayHostNotListeningException(); }
            }
            else await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await PipeFrames.WriteAsync(pipe, request, deadline.Token).ConfigureAwait(false);
            var response = await PipeFrames.ReadAsync<JsonElement>(pipe, deadline.Token).ConfigureAwait(false);
            if (response.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var failure = error.Deserialize<RelayFailure>(RelayJson.Options)
                    ?? throw new JsonException("The host returned no failure envelope.");
                throw new RelayException(failure.Code, failure.Message, failure.EffectsPossible);
            }
            return response;
        }
        catch (IOException)
        {
            throw new RelayException("relay_unavailable", "The relay connection was lost. Inspect retained results before resubmitting work.", effectsPossible: true);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new RelayException("relay_timeout", "The relay wait limit expired. Completion may still be pending; inspect the operation or agent status before retrying.");
        }
    }
}
