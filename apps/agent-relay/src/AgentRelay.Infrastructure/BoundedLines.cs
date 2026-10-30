using System.Text;

namespace AgentRelay.Infrastructure;

// Read fixed chunks: an unterminated peer line cannot allocate unbounded memory.
public sealed class BoundedLines(TextReader source, int maximumCharacters)
{
    private readonly char[] buffer = new char[4096];
    private int offset, count;

    public async Task<string?> ReadAsync()
    {
        var value = new StringBuilder(Math.Min(maximumCharacters, 4096));
        var oversized = false;
        while (true)
        {
            if (offset == count)
            {
                count = await source.ReadAsync(buffer.AsMemory()); offset = 0;
                if (count == 0)
                {
                    if (oversized) throw new InvalidDataException("MCP input exceeds the frame limit.");
                    return value.Length == 0 ? null : value.ToString();
                }
            }
            var character = buffer[offset++];
            if (character == '\n')
            {
                if (oversized) throw new InvalidDataException("MCP input exceeds the frame limit.");
                if (value.Length > 0 && value[^1] == '\r') value.Length--;
                return value.ToString();
            }
            if (value.Length < maximumCharacters) value.Append(character);
            else oversized = true;
        }
    }
}
