using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows.Presentation;

// Monitoring acknowledgement only: never modifies an operation or its journal.
internal sealed class AttentionReviews
{
    private sealed record Document(int Version, Dictionary<string, string> Entries);
    private readonly string path;
    private Dictionary<string, string> entries = [];
    internal long Revision { get; private set; }
    internal string? Warning { get; private set; }
    internal AttentionReviews(string data)
    {
        path = Path.Combine(Path.GetFullPath(data), "attention-reviews.json");
        if (!File.Exists(path)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), RelayJson.Options);
            if (saved is not { Version: 1, Entries: not null }) throw new JsonException("Unsupported review file");
            entries = saved.Entries;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { Warning = "Les alertes examinées n’ont pas pu être chargées. Elles restent visibles ; le fichier de suivi a été conservé."; }
    }

    private static string Key(WorkRecord operation) => operation.Request.Scope!.ProjectId.ToString("D") + ":" + operation.Request.Id.ToString("D");
    internal static string Token(WorkRecord operation) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Key(operation) + "|" + operation.State + "|" + operation.UpdatedAt.UtcTicks + "|" + operation.ErrorCode)));
    internal bool IsReviewed(WorkRecord operation) => entries.TryGetValue(Key(operation), out var token) && token == Token(operation);
    internal void Mark(WorkRecord current, string? expectedToken)
    {
        if (!MonitorProjection.NeedsAttention(current.State) || expectedToken != Token(current))
            throw new RelayException("review_stale", "L’opération a évolué. Consultez son état actuel avant de la marquer comme examinée.");
        if (Warning is not null) throw new IOException(Warning);
        var next = new Dictionary<string, string>(entries) { [Key(current)] = Token(current) };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { JsonSerializer.Serialize(stream, new Document(1, next), RelayJson.Options); stream.Flush(true); }
            File.Move(temporary, path, overwrite: true);
            entries = next; Revision++;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
