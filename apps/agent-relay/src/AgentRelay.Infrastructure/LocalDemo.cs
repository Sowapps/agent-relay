using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public static class LocalDemo
{
    public static RelayRoute Route { get; } = new("local-demo", "local-client", "local-demo");
    public static RelayMessage Message(string body, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Route.ProjectId, Route.Sender, Route.Recipient, body);
}

public sealed class LocalEchoResponder : IMessageResponder
{
    public Task<RelayReply> ReplyAsync(RelayMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new RelayReply(Guid.NewGuid(), message.Id, message.Recipient, message.Sender, message.Body));
    }
}
