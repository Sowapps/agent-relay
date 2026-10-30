namespace AgentRelay.Core;

public sealed record RelayMessage(Guid Id, string ProjectId, string Sender, string Recipient, string Body);
public sealed record RelayReply(Guid Id, Guid InReplyTo, string Sender, string Recipient, string Body);
public sealed record RelayExchange(RelayMessage Request, RelayReply Reply, DateTimeOffset CompletedAt);
public sealed record RelayRoute(string ProjectId, string Sender, string Recipient);

public sealed class RelayException(string code, string message, bool? effectsPossible = null) : Exception(message)
{
    public string Code { get; } = code;
    public bool? EffectsPossible { get; } = effectsPossible;
}

public interface IExchangeStore
{
    Task<RelayExchange?> FindAsync(Guid requestId, CancellationToken cancellationToken);
    Task SaveAsync(RelayExchange exchange, CancellationToken cancellationToken);
}

public interface IMessageResponder
{
    Task<RelayReply> ReplyAsync(RelayMessage message, CancellationToken cancellationToken);
}

