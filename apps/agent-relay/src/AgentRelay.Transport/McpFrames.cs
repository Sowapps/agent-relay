using System.Text;
using AgentRelay.Protocol;

namespace AgentRelay.Transport;

/// <summary>One reader; explicitly transfer unread bytes with the pending frame before succession.</summary>
public sealed class McpFrames(Stream input, byte[]? pending = null)
{
    private readonly byte[] buffer = new byte[4096];
    private byte[] unread = pending ?? [];
    private int offset;
    private static readonly UTF8Encoding utf8 = new(false, true);

    public byte[] ExportUnread() => unread[offset..];

    public async Task<string?> ReadAsync(CancellationToken token = default)
    {
        using var line = new MemoryStream();
        var oversized = false;
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
                if (oversized) throw new McpProtocolException("invalid_request", "MCP frame exceeds its limit.");
                return utf8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r');
            }
            if (line.Length < McpWireLimits.MaximumFrameBytes) line.WriteByte(next);
            else oversized = true;
        }
        if (oversized) throw new McpProtocolException("invalid_request", "MCP frame exceeds its limit.");
        return line.Length == 0 ? null : utf8.GetString(line.GetBuffer(), 0, (int)line.Length);
    }

    public static async Task WriteAsync(Stream output, string value, CancellationToken token = default)
    {
        var body = utf8.GetBytes(value + "\n");
        if (body.Length - 1 > McpWireLimits.MaximumFrameBytes) throw new McpProtocolException("result_too_large", "MCP result exceeds its limit.");
        await output.WriteAsync(body, token);
        await output.FlushAsync(token);
    }
}
