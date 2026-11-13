using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentRelay.Protocol;

public sealed record McpInvocation(Guid CallId, string OperationId, int Revision, JsonElement Arguments);
public sealed record McpOperationDescriptor(string Id, int Revision, string Description, JsonElement InputSchema, string SchemaHash);
public sealed record McpOperationManifest(string RegistryHash, IReadOnlyList<McpOperationDescriptor> Operations);
public sealed record McpTransportRequirement(int ProtocolVersion, string WireHash);

/// <summary>Fixed transport surface; application operation schemas never participate in its identity.</summary>
public static class McpStableProtocol
{
    public const int Version = 1;
    private static readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);
    private static object Id() => new { type = "string", format = "uuid" };
    private static object Text(int maximum) => new { type = "string", minLength = 1, maxLength = maximum };
    private static JsonElement Tool(string name, string description, object properties, params string[] required) =>
        JsonSerializer.SerializeToElement(new { name, description, inputSchema = new { type = "object", properties, required, additionalProperties = false } }, json);
    private static readonly IReadOnlyList<JsonElement> tools = Array.AsReadOnly(new[]
    {
        Tool("get_connection_status", "Read the bound relay's identity, transport compatibility and update state.", new { }),
        Tool("discover_operations", "Read current public operation schemas and authorized project context. Discovery does not grant execution rights.",
            new { project_id = Id(), cursor = new { type = "integer", minimum = 0 }, limit = new { type = "integer", minimum = 1, maximum = 100 }, registry_hash = Text(64) }),
        Tool("invoke_operation", "Invoke an explicit supported operation revision. Retain business request_id in arguments for submitted work; call_id is correlation, not permission or a replay guarantee.",
            new { call_id = Id(), operation_id = Text(100), revision = new { type = "integer", minimum = 1, maximum = int.MaxValue },
                arguments = new { type = "object", properties = new { }, required = Array.Empty<string>(), additionalProperties = true } },
            "call_id", "operation_id", "revision", "arguments"),
        Tool("get_result", "Read a retained operation through the existing scoped policy; never replay it.", new { request_id = Id() }, "request_id")
    });

    public static IReadOnlyList<JsonElement> Tools => tools;
    public static string WireHash { get; } = HashJson(JsonSerializer.SerializeToElement(new { version = Version, maximumFrameBytes = McpWireLimits.MaximumFrameBytes,
        schemas = tools.Select(tool => new { name = tool.GetProperty("name").GetString(), inputSchema = tool.GetProperty("inputSchema") }).ToArray() }, json));
    public static McpTransportRequirement Requirement => new(Version, WireHash);

    public static void Validate(string tool, JsonElement arguments)
    {
        var definition = tools.FirstOrDefault(item => item.GetProperty("name").GetString() == tool);
        if (definition.ValueKind == JsonValueKind.Undefined) throw new McpProtocolException("tool_denied", "Unknown stable MCP tool.");
        McpSchema.Validate(arguments, definition.GetProperty("inputSchema"));
    }

    public static McpInvocation Invocation(JsonElement arguments)
    {
        Validate("invoke_operation", arguments);
        return new(arguments.GetProperty("call_id").GetGuid(), arguments.GetProperty("operation_id").GetString()!,
            arguments.GetProperty("revision").GetInt32(), arguments.GetProperty("arguments").Clone());
    }

    public static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string HashJson(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) WriteCanonical(writer, value);
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else value.WriteTo(writer);
    }

    public static void ValidateRevisionHistory(IEnumerable<McpOperationDescriptor> previous, IEnumerable<McpOperationDescriptor> current)
    {
        var before = previous.ToArray();
        var after = current.ToArray();
        foreach (var item in before.Concat(after))
            if (item.Revision < 1 || item.SchemaHash != HashJson(item.InputSchema))
                throw new McpProtocolException("invalid_operation_manifest", "An operation manifest has invalid schema identity.");
        var selected = after.ToDictionary(item => (item.Id, item.Revision));
        foreach (var prior in before)
            if (selected.TryGetValue((prior.Id, prior.Revision), out var next) && prior.SchemaHash != next.SchemaHash)
                throw new McpProtocolException("operation_revision_reused", "An operation input schema changed without a new revision: " + prior.Id);
    }
}
