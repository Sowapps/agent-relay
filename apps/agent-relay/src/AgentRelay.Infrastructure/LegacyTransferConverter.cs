using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Compatibility belongs to the storage/transport boundary, never to the neutral domain.
internal sealed class LegacyTransferConverter : JsonConverter<TransferRequest>
{
    public override TransferRequest Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader); var value = document.RootElement;
        var direction = value.GetProperty("direction").GetString()!;
        var overwrite = value.TryGetProperty("overwrite", out var replace) && replace.GetBoolean();
        if (value.TryGetProperty("sourcePath", out var source)) return new(direction, source.GetString()!, value.GetProperty("destinationPath").GetString()!, overwrite);
        var local = value.GetProperty("windowsPath").GetString()!; var remote = value.GetProperty("linuxPath").GetString()!;
        return direction == "upload" ? new(direction, local, remote, overwrite) : new(direction, remote, local, overwrite);
    }
    public override void Write(Utf8JsonWriter writer, TransferRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject(); writer.WriteString("direction", value.Direction); writer.WriteString("sourcePath", value.SourcePath);
        writer.WriteString("destinationPath", value.DestinationPath); writer.WriteBoolean("overwrite", value.Overwrite); writer.WriteEndObject();
    }
}
