using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

// Private bounded diagnostics; never log a request body, token or credential. Failure to log must not alter the outcome.
public static class McpDiagnostics
{
    private static readonly object gate = new();
    public static string? Record(string data, Exception error)
    {
        if (error is RelayException) return null; // Expected failures already carry an explicit safe code/message.
        return RecordText(data, error.ToString());
    }
    public static string? RecordText(string data, string text)
    {
        try
        {
            if (!Directory.Exists(data) || !File.Exists(Path.Combine(data, "mcp-runtime.json")) || File.Exists(Path.Combine(data, "migrated-to.json"))) return null;
            var day = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
            var id = "mcp-" + day + "-" + Guid.NewGuid().ToString("N")[..12];
            var path = Path.Combine(data, "logs", "mcp", day + ".log"); RuntimePaths.DemandPrivatePath(path);
            lock (gate)
            {
                if (File.Exists(path) && new FileInfo(path).Length >= 1024 * 1024) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, $"{DateTimeOffset.UtcNow:o} {id}\n{text[..Math.Min(text.Length, 8000)]}\n");
            }
            return id;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }
}
