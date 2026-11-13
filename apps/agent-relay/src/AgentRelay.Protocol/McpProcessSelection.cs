using System.Security.Cryptography;
using System.Diagnostics;
using System.Text.Json;

namespace AgentRelay.Protocol;

public static class McpProcessIdentity
{
    public static DateTimeOffset StartedAt { get; } = ReadStart();
    private static DateTimeOffset ReadStart()
    {
        using var process = Process.GetCurrentProcess();
        return new(process.StartTime.ToUniversalTime());
    }
}

public sealed record McpProcessImage(string Executable, string[] Arguments, IReadOnlyDictionary<string, string> Files);
public sealed record McpProcessSelection(int Version, McpTransportRequirement Transport, McpProcessImage Worker, McpProcessImage Gateway,
    McpProcessImage? Host = null);

/// <summary>Checked launch metadata supplied by installation composition, never by an MCP request.</summary>
public static class McpProcessSelections
{
    public static JsonSerializerOptions Json { get; } = new(JsonSerializerDefaults.Web);

    public static McpProcessSelection Read(string path)
    {
        McpPrivateFiles.Demand(path);
        if (new FileInfo(path).Length > McpWireLimits.MaximumFrameBytes) throw new IOException("Transport selection is too large.");
        return Parse(McpPrivateFiles.ReadText(path));
    }

    public static McpProcessSelection Parse(string text)
    {
        var value = JsonSerializer.Deserialize<McpProcessSelection>(text, Json)
            ?? throw new IOException("Transport selection is missing.");
        if (value.Version != 1 || value.Transport != McpStableProtocol.Requirement)
            throw new McpProtocolException("transport_incompatible", "The selected wire contract requires a qualified connection migration.");
        Validate(value.Worker);
        Validate(value.Gateway);
        if (value.Host is not null) Validate(value.Host);
        return value;
    }

    public static void Validate(McpProcessImage image)
    {
        if (!Path.IsPathFullyQualified(image.Executable) || image.Arguments is null || image.Files is null || image.Files.Count == 0)
            throw new IOException("Process selection needs explicit executable, arguments and file hashes.");
        var root = Path.GetDirectoryName(image.Executable)!;
        if (!image.Files.ContainsKey(Path.GetFileName(image.Executable))) throw new IOException("Executable is not in the selected file manifest.");
        foreach (var (relative, hash) in image.Files)
        {
            var path = Path.GetFullPath(relative, root);
            if (Path.IsPathFullyQualified(relative) || !path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("A selected image escapes its bundle.");
            McpPrivateFiles.Demand(path);
            using var stream = File.OpenRead(path);
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(stream)), hash, StringComparison.Ordinal))
                throw new IOException("A selected process file does not match its manifest: " + relative);
        }
    }

    public static string Identity(McpProcessImage image) => McpStableProtocol.HashJson(JsonSerializer.SerializeToElement(new { image.Executable, image.Arguments, image.Files }, Json));
}

public static class McpPrivateFiles
{
    public static string ReadText(string path, int maximumBytes = McpWireLimits.MaximumFrameBytes)
    {
        Demand(path);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var body = new MemoryStream();
        var bytes = new byte[4096];
        int count;
        while ((count = source.Read(bytes)) > 0)
        {
            if (body.Length + count > maximumBytes) throw new IOException("Private metadata exceeds its bound.");
            body.Write(bytes, 0, count);
        }
        return new System.Text.UTF8Encoding(false, true).GetString(body.ToArray()).TrimStart('\uFEFF');
    }

    public static void Demand(string path)
    {
        for (var current = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(path))!); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Private relay storage cannot traverse a reparse point.");
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Private relay files cannot be reparse points.");
    }
}
