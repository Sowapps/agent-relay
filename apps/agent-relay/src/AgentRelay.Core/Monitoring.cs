namespace AgentRelay.Core;

// Presentation projections consume retained facts; they never inspect or execute an agent.
public sealed record MonitorTask(Guid ProjectId, Guid Id, IReadOnlyList<WorkRecord> Operations, IReadOnlyList<InboxMessage> Messages)
{
    public DateTimeOffset UpdatedAt => Operations.Select(item => item.UpdatedAt).Concat(Messages.Select(item => item.ReceivedAt)).Max();
    public string State => MonitorProjection.AggregateState(Operations.Select(item => item.State));
    public bool Active => Operations.Any(item => MonitorProjection.InProgress(item.State));
}

public sealed record MonitorEvent(DateTimeOffset At, WorkRecord? Operation = null, InboxMessage? Message = null);
public sealed record EndpointDescriptor(Guid EndpointId, string Connector, string Connection, string? Environment = null, IReadOnlyList<string>? Capabilities = null);

public static class MonitorProjection
{
    public static bool InProgress(string state) => state is "running" or "pending" or "awaiting_reply" or "retry_wait";
    public static bool NeedsAttention(string state) => state is "unknown" or "failed" or "awaiting_approval" or "approval_expired" or "blocked";
    public static string AggregateState(IEnumerable<string> states)
    {
        var values = states.ToHashSet(StringComparer.Ordinal);
        foreach (var state in new[] { "unknown", "blocked", "awaiting_approval", "approval_expired", "failed", "running", "awaiting_reply", "retry_wait", "pending" })
            if (values.Contains(state)) return state;
        return values.Count == 0 ? "idle" : "completed";
    }

    public static IReadOnlyList<MonitorTask> Tasks(IEnumerable<WorkRecord> operations, IEnumerable<InboxMessage> messages)
    {
        var retained = operations.Where(item => item.Request.Scope is not null).ToArray();
        var index = retained.ToDictionary(item => item.Request.Id);
        var linked = messages.Where(message => message.InReplyTo is { } id && index.TryGetValue(id, out var request) &&
            request.Request.Kind == "message" && request.Request.Scope!.ProjectId == message.ProjectId &&
            request.Request.SessionId == message.SessionId).ToArray();
        return retained.GroupBy(item => (Project: item.Request.Scope!.ProjectId, Task: item.Request.TaskId ?? item.Request.Id))
            .Select(group => new MonitorTask(group.Key.Project, group.Key.Task, group.OrderBy(item => item.CreatedAt).ToArray(),
                linked.Where(message => group.Any(operation => operation.Request.Id == message.InReplyTo)).ToArray()))
            .OrderByDescending(item => item.UpdatedAt).ToArray();
    }

    public static IReadOnlyList<MonitorEvent> Timeline(MonitorTask task) => task.Operations.Select(item => new MonitorEvent(item.CreatedAt, Operation: item))
        .Concat(task.Messages.Select(item => new MonitorEvent(item.ReceivedAt, Message: item)))
        .OrderBy(item => item.At).ThenBy(item => item.Operation is null ? 1 : 0).ToArray();
}
