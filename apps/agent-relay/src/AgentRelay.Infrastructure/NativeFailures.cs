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
            return new(code is not null && Regex.IsMatch(code, "\\A[a-z][a-z0-9_]{0,79}\\z") ? code : "wsl_error", Limit(message), effects);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return new("wsl_error", Limit(string.IsNullOrWhiteSpace(output) ? standardError : output));
        }
    }

    private static string Limit(string message) => message[..Math.Min(4000, message.Length)];
}
