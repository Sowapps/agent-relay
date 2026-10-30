using System.Text.Json;

namespace AgentRelay.Infrastructure;

public static class RelayJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    { var options = new JsonSerializerOptions(JsonSerializerDefaults.Web); options.Converters.Add(new LegacyTransferConverter()); return options; }
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new JsonException("JSON payload cannot be null.");
}
