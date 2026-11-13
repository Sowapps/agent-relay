using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed record McpOperationRegistration(string Id, int Revision, string Description, JsonElement InputSchema,
    Func<JsonElement, ProjectAction> Route);

/// <summary>Version selection/schema validation only; execution and authority stay with project use cases.</summary>
public sealed class McpOperationRegistry
{
    private readonly Dictionary<(string, int), McpOperationRegistration> registrations;
    public IReadOnlyList<McpOperationDescriptor> Descriptors { get; }
    public string Hash { get; }

    public McpOperationRegistry(IEnumerable<McpOperationRegistration> definitions)
    {
        var values = definitions.ToArray();
        if (values.Any(item => string.IsNullOrWhiteSpace(item.Id) || item.Id.Length > 100 || item.Revision < 1))
            throw new ArgumentException("Operation registrations need a bounded ID and positive revision.", nameof(definitions));
        registrations = values.ToDictionary(item => (item.Id, item.Revision));
        Descriptors = Array.AsReadOnly(values.OrderBy(item => item.Id, StringComparer.Ordinal).ThenBy(item => item.Revision)
            .Select(item => new McpOperationDescriptor(item.Id, item.Revision, item.Description,
                item.InputSchema.Clone(), McpStableProtocol.HashJson(item.InputSchema))).ToArray());
        Hash = McpStableProtocol.Hash(JsonSerializer.Serialize(Descriptors.Select(item => new { item.Id, item.Revision, item.SchemaHash }), RelayJson.Options));
    }

    public ProjectAction Resolve(McpInvocation invocation)
    {
        if (!registrations.TryGetValue((invocation.OperationId, invocation.Revision), out var registration))
            throw new RelayException(registrations.Keys.Any(key => key.Item1 == invocation.OperationId)
                ? "operation_revision_unsupported" : "operation_unknown", "Discover a supported operation revision before submitting work.", false);
        Validate(invocation.Arguments, registration.InputSchema);
        return registration.Route(invocation.Arguments);
    }

    internal static void Validate(JsonElement value, JsonElement schema)
    {
        try { McpSchema.Validate(value, schema); }
        catch (McpProtocolException error) { throw new RelayException(error.Code, error.Message, false); }
    }
}
