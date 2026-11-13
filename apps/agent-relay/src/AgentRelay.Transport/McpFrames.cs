using System.Text;
using AgentRelay.Protocol;

namespace AgentRelay.Transport;

/// <summary>One reader; explicitly transfer unread bytes with the pending frame before succession.</summary>
public sealed class McpFrames(Stream input, byte[]? pending = null)
{
    private readonly byte[] buffer = new byte[4096];
    private byte[] unread = pending ?? [];
    private int offset;
    private readonly MemoryStream line = new();
    private bool oversized;
    private static readonly UTF8Encoding utf8 = new(false, true);
    public bool CanTransfer => !oversized;
    public long BufferedBytes => line.Length + unread.Length - offset;

    public byte[] ExportUnread() => line.ToArray().Concat(unread[offset..]).ToArray();

    public async Task<string?> ReadAsync(CancellationToken token = default)
    {
        while (true)
        {
            if (offset == unread.Length)
            {
                var count = await input.ReadAsync(buffer, token);
                if (count == 0) break;
                unread = buffer[..count];
                offset = 0;
            }
            var next = unread[offset++];
            if (next == 10)
            {
                var tooLarge = oversized;
                var value = line.ToArray();
                line.SetLength(0);
                oversized = false;
                if (tooLarge) throw new McpProtocolException("invalid_request", "MCP frame exceeds its limit.");
                return utf8.GetString(value).TrimEnd('\r');
            }
            if (line.Length < McpWireLimits.MaximumFrameBytes) line.WriteByte(next);
            else oversized = true;
        }
        var tooLargeAtEnd = oversized;
        oversized = false;
        var tail = line.ToArray();
        line.SetLength(0);
        if (tooLargeAtEnd) throw new McpProtocolException("invalid_request", "MCP frame exceeds its limit.");
        return tail.Length == 0 ? null : utf8.GetString(tail);
    }

    public static async Task WriteAsync(Stream output, string value, CancellationToken token = default)
    {
        var body = utf8.GetBytes(value + "\n");
        if (body.Length - 1 > McpWireLimits.MaximumFrameBytes) throw new McpProtocolException("result_too_large", "MCP result exceeds its limit.");
        await output.WriteAsync(body, token);
        await output.FlushAsync(token);
    }
}
