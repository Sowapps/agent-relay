using System.Text;
using System.Text.Json;

namespace AgentRelay.Protocol;

public sealed class McpProtocolException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public static class McpWireLimits
{
    public const int MaximumFrameBytes = 1024 * 1024;
}

/// <summary>The bounded schema subset shared by stable envelopes and operation arguments.</summary>
public static class McpSchema
{
    public static void Validate(JsonElement value, JsonElement schema)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new McpProtocolException("invalid_request", "MCP arguments are required.");
        if (Encoding.UTF8.GetByteCount(value.GetRawText()) > McpWireLimits.MaximumFrameBytes)
            throw new McpProtocolException("invalid_request", "MCP arguments exceed the frame limit.");
        ValidateValue(value, schema);
    }

    private static void ValidateValue(JsonElement value, JsonElement schema)
    {
        var type = schema.GetProperty("type").GetString();
        var matches = type switch
        {
            "object" => value.ValueKind == JsonValueKind.Object,
            "string" => value.ValueKind == JsonValueKind.String,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            _ => throw new McpProtocolException("invalid_schema", "Unsupported MCP schema type.")
        };
        if (!matches) throw new McpProtocolException("invalid_request", "MCP argument type does not match its schema.");
        if (type == "object")
        {
            var properties = schema.GetProperty("properties");
            foreach (var required in schema.GetProperty("required").EnumerateArray())
                if (!value.TryGetProperty(required.GetString()!, out _))
                    throw new McpProtocolException("invalid_request", "Missing argument: " + required.GetString());
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new McpProtocolException("invalid_request", "Duplicate argument: " + property.Name);
                if (properties.TryGetProperty(property.Name, out var child)) ValidateValue(property.Value, child);
                else if (!schema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.True)
                    throw new McpProtocolException("invalid_request", "Unknown argument: " + property.Name);
            }
        }
        if (type == "string")
        {
            if (schema.TryGetProperty("format", out var format) && format.GetString() == "uuid" &&
                (!value.TryGetGuid(out var id) || id == Guid.Empty))
                throw new McpProtocolException("invalid_request", "Expected a nonempty UUID.");
            if ((schema.TryGetProperty("maxLength", out var maximum) && value.GetString()!.Length > maximum.GetInt32()) ||
                (schema.TryGetProperty("minLength", out var minimum) && value.GetString()!.Length < minimum.GetInt32()))
                throw new McpProtocolException("invalid_request", "MCP text is outside its stated bounds.");
        }
        if (type == "integer" && ((schema.TryGetProperty("minimum", out var min) && value.GetInt64() < min.GetInt64()) ||
                                  (schema.TryGetProperty("maximum", out var max) && value.GetInt64() > max.GetInt64())))
            throw new McpProtocolException("invalid_request", "MCP numeric argument is outside its bounds.");
    }
}
