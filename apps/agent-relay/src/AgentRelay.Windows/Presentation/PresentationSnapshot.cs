using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;
using AgentRelay.Windows.Monitoring;

namespace AgentRelay.Windows.Presentation;

// A passive, renderer-independent presentation contract. No credential or native binding is exposed.
internal static class PresentationSnapshot
{
    private static string Key(string prefix, string value) => prefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
    private static string TaskKey(Guid projectId, Guid taskId) => projectId.ToString("D") + ":" + taskId.ToString("D");
    private static string State(string state) => state == "awaiting_approval" ? "attention" : state;
    private static string Stamp(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-ddTHH:mm:sszzz");
    private static string Initial(string name) => string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => part[..1])).ToUpperInvariant();
    private static readonly string[] Colors = ["blue", "purple", "green"];

    public static JsonElement Build(ProjectConfiguration configuration, IReadOnlyList<EndpointDescriptor> descriptions,
        IReadOnlyList<ProjectObservation> observations, bool online, string? error, DateTimeOffset observedAt, bool startupEnabled = false, AttentionReviews? reviews = null, IReadOnlyDictionary<Guid, ProjectObservation>? threads = null)
    {
        var operations = observations.SelectMany(item => item.Operations).OrderByDescending(item => item.CreatedAt).ToArray();
        var messages = observations.SelectMany(item => item.Messages).OrderByDescending(item => item.ReceivedAt).ToArray();
        var sessions = observations.SelectMany(item => item.Sessions).ToArray();
        var descriptors = configuration.Endpoints.ToDictionary(item => item.Id, item => descriptions.FirstOrDefault(d => d.EndpointId == item.Id)
            ?? new EndpointDescriptor(item.Id, item.AdapterId, "Non renseignée"));
        string ConnectorId(Guid id) => Key("connector-", descriptors[id].Connector + "|" + string.Join(',', descriptors[id].Capabilities ?? []));
        string ConnectionId(Guid id) => Key("connection-", descriptors[id].Connection + "|" + descriptors[id].Environment);
        string[] UiCapabilities(IEnumerable<string> values)
        {
            var source = values.ToHashSet(StringComparer.Ordinal); var capabilities = new List<string>();
            if (source.Contains(RelayCapabilities.ManageSession)) capabilities.AddRange(["sessions.manage", "operations.interrupt"]);
            if (source.Contains(RelayCapabilities.SendMessage)) capabilities.Add("messages.send");
            if (source.Contains(RelayCapabilities.ReadHistory)) capabilities.Add("messages.read");
            if (source.Contains("session.inspect")) capabilities.Add("sessions.inspect");
            return capabilities.ToArray();
        }
        string[] SessionCapabilities(SessionSnapshot session)
        {
            if (!configuration.Projects.Any(item => item.Id == session.Scope.ProjectId && item.Enabled)) return [];
            var member = configuration.Memberships.FirstOrDefault(item => item.ProjectId == session.Scope.ProjectId && item.EndpointId == session.Scope.SenderId);
            var installed = descriptors.GetValueOrDefault(session.Scope.RecipientId)?.Capabilities ?? [];
            var granted = member?.Capabilities ?? [];
            var effective = installed.Where(capability => capability == "session.inspect" ? granted.Contains(RelayCapabilities.ReadHistory) : granted.Contains(capability));
            return UiCapabilities(effective);
        }
        var tasks = MonitorProjection.Tasks(operations, messages);
        var taskByRequest = tasks.SelectMany(task => task.Operations.Select(operation => (operation.Request.Id, Task: task)))
            .ToDictionary(item => item.Id, item => item.Task);
        var response = new
        {
            schemaVersion = 1,
            revision = observations.Sum(item => item.Revision),
            referenceDate = observedAt.ToLocalTime().ToString("yyyy-MM-dd"),
            observedAt = Stamp(observedAt),
            relay = online ? "ready" : "offline",
            relayError = error,
            instances = 1,
            startupEnabled,
            hasMore = observations.Any(item => item.MoreOperations || item.MoreMessages || item.MoreSessions),
            more = new { operations = observations.Any(item=>item.MoreOperations), messages=observations.Any(item=>item.MoreMessages), sessions=observations.Any(item=>item.MoreSessions) },
            sessionHistory = threads?.ToDictionary(item=>item.Key.ToString("D"),item=>new { loaded=item.Value.Loaded, hasMore=item.Value.MoreOperations||item.Value.MoreMessages, error=item.Value.Error }),
            unreadAvailable = false,
            mcpState = "unverified",
            workspaces = configuration.Workspaces.Select(item => new { item.Id, item.EndpointId, item.Name, item.Location }).ToArray(),
            projects = configuration.Projects.Select((project, index) => new
            {
                id = project.Id.ToString("D"), project.Name, initial = Initial(project.Name), color = Colors[index % Colors.Length],
                description = "Suivi du projet relayé.",
                hasMore = observations.Any(item=>item.ProjectId==project.Id && (item.MoreOperations || item.MoreMessages || item.MoreSessions)),
                more = observations.Where(item=>item.ProjectId==project.Id).Select(item=>new { operations=item.MoreOperations,messages=item.MoreMessages,sessions=item.MoreSessions }).FirstOrDefault(),
                participantIds = configuration.Memberships.Where(item => item.ProjectId == project.Id).Select(item => item.EndpointId.ToString("D")).ToArray(),
                permissions = configuration.Memberships.Where(item => item.ProjectId == project.Id).Select(item => new { item.EndpointId, item.Role, item.Capabilities, item.Workspaces, item.CommandIds }).ToArray()
            }).ToArray(),
            participants = configuration.Endpoints.Select((endpoint, index) => new
            {
                id = endpoint.Id.ToString("D"), endpoint.Name,
                role = configuration.Memberships.FirstOrDefault(item => item.EndpointId == endpoint.Id)?.Role ?? "observer",
                roles = configuration.Memberships.Where(item => item.EndpointId == endpoint.Id).ToDictionary(item => item.ProjectId.ToString("D"), item => item.Role),
                initial = Initial(endpoint.Name), color = Colors[index % Colors.Length], connectorId = ConnectorId(endpoint.Id), connectionId = ConnectionId(endpoint.Id)
            }).ToArray(),
            connectors = configuration.Endpoints.GroupBy(item => ConnectorId(item.Id)).Select(group => new
            {
                id = group.Key, name = descriptors[group.First().Id].Connector,
                state = "unverified", capabilities = UiCapabilities(descriptors[group.First().Id].Capabilities ?? []),
                limitation = (descriptors[group.First().Id].Capabilities?.Count ?? 0) == 0 ? "Aucune action native n’est déclarée pour ce connecteur." : ""
            }).ToArray(),
            connections = configuration.Endpoints.GroupBy(item => ConnectionId(item.Id)).Select(group => new
            {
                id = group.Key, name = descriptors[group.First().Id].Environment ?? descriptors[group.First().Id].Connection,
                kind = descriptors[group.First().Id].Connection, state = "unverified"
            }).ToArray(),
            sessions = sessions.Select(session => new
            {
                id = session.Id.ToString("D"), project = session.Scope.ProjectId.ToString("D"), session.Name,
                participantId = session.Scope.RecipientId.ToString("D"), ownerId = session.Scope.SenderId.ToString("D"),
                workspace = configuration.Workspaces.FirstOrDefault(item => item.Id == session.Scope.WorkspaceId)?.Location ?? "Non renseigné",
                workspaceId = session.Scope.WorkspaceId, state = session.WorkActive ? "running" : State(session.State),
                time = "État conservé", conversation = session.NativeConversationId, capabilities = SessionCapabilities(session),
                diagnostic = session.Diagnostic
            }).ToArray(),
            tasks = tasks.Select(task => new
            {
                id = TaskKey(task.ProjectId, task.Id), nativeId = task.Id, project = task.ProjectId.ToString("D"),
                title = MonitorComponents.OperationTitle(task.Operations[0]),
                description = task.Operations[0].Request.Text.Length > 0 ? task.Operations[0].Request.Text : task.Operations[0].Metadata?.Context ?? "Opérations liées à cette tâche.",
                participantId = task.Operations[0].Request.Scope!.RecipientId.ToString("D")
            }).ToArray(),
            operations = operations.Select(operation => Operation(operation, configuration, reviews, previewOnly: true)).ToArray(),
            messages = messages.Select(message =>
            {
                var linked = message.InReplyTo is { } requestId && taskByRequest.TryGetValue(requestId, out var candidate)
                    && candidate.ProjectId == message.ProjectId && candidate.Messages.Any(item => item.Id == message.Id) ? candidate : null;
                return new
                {
                    id = message.Id.ToString("D"), project = message.ProjectId?.ToString("D"), session = message.SessionId?.ToString("D"),
                    senderId = message.SenderEndpointId?.ToString("D"), recipientId = message.RecipientEndpointId?.ToString("D"),
                    kind = message.InReplyTo is null ? "independent" : "reply", operation = linked is null ? null : message.InReplyTo?.ToString("D"),
                    taskId = linked is null ? null : TaskKey(linked.ProjectId, linked.Id), message.Text,
                    stamp = Stamp(message.ReceivedAt), time = message.ReceivedAt.ToLocalTime().ToString("HH:mm"), unread = false,
                    technical = message
                };
            }).ToArray()
        };
        return JsonSerializer.SerializeToElement(response, RelayJson.Options);
    }

    public static object Operation(WorkRecord operation, ProjectConfiguration configuration, AttentionReviews? reviews = null, bool previewOnly = false)
    {
        var scope = operation.Request.Scope ?? throw new ArgumentException("A presentation operation requires explicit scope.");
        return new
        {
            id = operation.Request.Id.ToString("D"), project = scope.ProjectId.ToString("D"), session = operation.Request.SessionId?.ToString("D"),
            taskId = TaskKey(scope.ProjectId, operation.Request.TaskId ?? operation.Request.Id),
            participantId = scope.RecipientId.ToString("D"), initiatorId = scope.SenderId.ToString("D"),
            operation.Request.Kind, title = MonitorComponents.OperationTitle(operation),
            state = State(operation.State), stamp = Stamp(operation.CreatedAt), time = operation.CreatedAt.ToLocalTime().ToString("HH:mm"),
            updatedAt = Stamp(operation.UpdatedAt), request = operation.Request.Text,
            result = operation.Error is not null ? UserMessages.Error(operation.ErrorCode) : operation.Result?.Output ?? "En attente de confirmation…",
            workspace = operation.Result?.Details?.GetValueOrDefault("workspace") ?? operation.Metadata?.Context ?? configuration.Workspaces.FirstOrDefault(item => item.Id == scope.WorkspaceId)?.Location,
            program = operation.Result?.Details?.GetValueOrDefault("program"), arguments = CommandArguments(operation),
            progress = operation.Progress is { TotalBytes: > 0 } progress ? (int?)Math.Clamp(progress.Bytes * 100 / progress.TotalBytes, 0, 100) : null,
            progressPhase = operation.Progress?.Phase, errorCode = operation.ErrorCode, transfer = operation.Request.Artifact,
            reviewToken = AttentionReviews.Token(operation), reviewed = reviews?.IsReviewed(operation) == true, reviewable = MonitorProjection.NeedsAttention(operation.State),
            hasResult = operation.Result is not null, previewOnly = previewOnly || operation.Result?.Details?.ContainsKey("previewOnly") == true, technical = operation
        };
    }

    private static string[]? CommandArguments(WorkRecord operation)
    {
        if (operation.Result?.Details?.GetValueOrDefault("arguments") is not { } text) return null;
        try { return JsonSerializer.Deserialize<string[]>(text); } catch (JsonException) { return null; }
    }
}
