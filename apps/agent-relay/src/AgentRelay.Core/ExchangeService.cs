using System.Text;

namespace AgentRelay.Core;

public sealed class ExchangeService(IExchangeStore store, IMessageResponder responder, IReadOnlyCollection<RelayRoute> routes) : IDisposable
{
    private readonly SemaphoreSlim active = new(1, 1);

    public async Task<RelayExchange> ExchangeAsync(RelayMessage message, CancellationToken cancellationToken)
    {
        Validate(message);
        if (!routes.Contains(new RelayRoute(message.ProjectId, message.Sender, message.Recipient)))
            throw new RelayException("route_denied", "The requested local route is not configured.");
        if (!await active.WaitAsync(0, cancellationToken))
            throw new RelayException("busy", "An exchange is already active.");
        try
        {
            var existing = await store.FindAsync(message.Id, cancellationToken);
            if (existing is not null)
            {
                if (existing.Request != message) throw new RelayException("id_conflict", "This identifier already belongs to different content.");
                return existing;
            }
            var reply = await responder.ReplyAsync(message, cancellationToken);
            if (reply.Id == Guid.Empty || reply.InReplyTo != message.Id || reply.Sender != message.Recipient || reply.Recipient != message.Sender)
                throw new RelayException("invalid_reply", "The responder returned an uncorrelated reply.");
            var exchange = new RelayExchange(message, reply, DateTimeOffset.UtcNow);
            await store.SaveAsync(exchange, cancellationToken);
            return exchange;
        }
        finally { active.Release(); }
    }

    public async Task<RelayExchange> ReadAsync(Guid requestId, CancellationToken cancellationToken) =>
        await store.FindAsync(requestId, cancellationToken) ?? throw new RelayException("not_found", "No recorded exchange has this identifier.");

    private static void Validate(RelayMessage message)
    {
        static bool Identity(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');
        if (message.Id == Guid.Empty || !Identity(message.ProjectId) || !Identity(message.Sender) || !Identity(message.Recipient) ||
            string.IsNullOrWhiteSpace(message.Body) || Encoding.UTF8.GetByteCount(message.Body) > 16_384)
            throw new RelayException("invalid_message", "A message requires an identifier, valid endpoint identities and 1–16384 UTF-8 bytes of text.");
    }

    public void Dispose() => active.Dispose();
}
