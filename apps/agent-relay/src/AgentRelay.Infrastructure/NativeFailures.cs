using System.Text.Json;
using System.Text.RegularExpressions;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

/// <summary>Translates the native bridge envelope; prose is retained only as bounded diagnostic text.</summary>
public static class NativeFailures
{
    public static RelayException Read(string output, string standardError)
    {
        try
        {
            using var document = JsonDocument.Parse(output);
            var value = document.RootElement;
            var message = value.TryGetProperty("error", out var error) ? error.GetString() ?? "Native operation failed." : "Native operation failed.";
            var code = value.TryGetProperty("code", out var item) ? item.GetString() : null;
            bool? effects = value.TryGetProperty("effectsPossible", out var possible)
                ? possible.ValueKind switch { JsonValueKind.False => false, JsonValueKind.Null => null, _ => true }
                : true;
            // Unrefreshed legacy helpers have no typed code: preserve uncertainty, never infer from prose.
            var turnEnded = value.TryGetProperty("nativeTurnEnded", out var ended) && ended.ValueKind == JsonValueKind.True;
            return new(code is not null && Regex.IsMatch(code, "\\A[a-z][a-z0-9_]{0,79}\\z") ? code : "wsl_error", Limit(message), effects,
                turnEnded, turnEnded && effects == false ? ReadRetryDelay(value) : null);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return new("wsl_error", Limit(string.IsNullOrWhiteSpace(output) ? standardError : output));
        }
    }

    private static TimeSpan? ReadRetryDelay(JsonElement value)
    {
        if (!value.TryGetProperty("retrySafe", out var safe) || safe.ValueKind != JsonValueKind.True) return null;
        if (!value.TryGetProperty("retryAfterSeconds", out var delay) || delay.ValueKind == JsonValueKind.Null) return TimeSpan.Zero;
        return delay.ValueKind == JsonValueKind.Number && delay.TryGetInt64(out var seconds) && seconds is >= 0 and <= 9_999_999_999
            ? TimeSpan.FromSeconds(seconds) : null;
    }

    private static string Limit(string message) => message[..Math.Min(4000, message.Length)];
}
