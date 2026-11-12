using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record UpdateAdapterFailure(string Type, string Message, string Detail, string? Code = null,
    string[]? RelatedDiagnostics = null);
public sealed record UpdateFailureInfo(string Code, string Message, string DiagnosticId, string Recovery,
    string? DetailsPath, string? DiagnosticStorageError = null, string? InlineDetail = null);
public sealed record UpdateFailureDetail(UpdateFailureInfo Error, string Type, string Detail, DateTimeOffset At,
    string[] RelatedDiagnostics);
public sealed record UpdateFailureSummary(string Code, string Message, string DiagnosticId, string Recovery, DateTimeOffset At);

/// <summary>Retains identifiable update errors, including expected refusals and adapter failures.</summary>
public static class InstallationUpdateDiagnostics
{
    public static UpdateFailureInfo Capture(string? data, Exception error) => Capture(data, new UpdateAdapterFailure(
        error.GetType().FullName ?? error.GetType().Name, error.Message, error.ToString(),
        error is RelayException relay ? relay.Code : error switch
        {
            UnauthorizedAccessException => "update_access_denied",
            IOException => "update_storage_error",
            JsonException => "update_metadata_invalid",
            OperationCanceledException => "update_cancelled",
            ArgumentException => "update_invalid_request",
            _ => "update_failed"
        }));

    public static UpdateFailureInfo Capture(string? data, UpdateAdapterFailure failure)
    {
        var id = "update-" + Guid.NewGuid().ToString("N")[..16];
        var code = failure.Code is { Length: > 0 } codeValue && codeValue.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')
            ? codeValue : "update_adapter_failed";
        var detail = failure.Detail[..Math.Min(failure.Detail.Length, 24000)];
        var message = failure.Message[..Math.Min(failure.Message.Length, 1600)];
        var recovery = "Read this diagnostic and the retained transaction status. Resume or roll back through the update command; never delete a maintenance lock or replay agent work.";
        var info = new UpdateFailureInfo(code, message, id, recovery, null);
        try
        {
            if (data is null || !Path.IsPathFullyQualified(data) || !Directory.Exists(data))
                throw new IOException("No validated installed data scope is available for persistent diagnostics.");
            if (!File.Exists(Path.Combine(data, "mcp-runtime.json")) && !File.Exists(Path.Combine(data, "relay.sqlite"))
                && !File.Exists(Path.Combine(RuntimePaths.ApplicationDirectory(RuntimePaths.PortableInstallationForData(data)), "update", "recovery.json")))
                throw new IOException("No retained installation identity is available for persistent diagnostics.");
            var path = DetailPath(data, id);
            RuntimePaths.DemandPrivatePath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            info = info with { DetailsPath = path };
            var record = new UpdateFailureDetail(info, failure.Type, detail, DateTimeOffset.UtcNow, failure.RelatedDiagnostics ?? []);
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            { JsonSerializer.Serialize(stream, record, RelayJson.Options); stream.Flush(true); }
            var latest = LatestPath(data);
            var pending = latest + ".pending-" + id;
            using (var stream = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, Summary(info), RelayJson.Options); stream.Flush(true); }
            File.Move(pending, latest, true);
            return info;
        }
        catch (Exception storage) when (storage is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return info with { DetailsPath = info.DetailsPath is { } saved && File.Exists(saved) ? saved : null,
                DiagnosticStorageError = storage.Message, InlineDetail = detail };
        }
    }

    public static UpdateFailureDetail Read(string data, string id)
    {
        var path = DetailPath(data, id);
        RuntimePaths.DemandPrivatePath(path);
        return RelayJson.Deserialize<UpdateFailureDetail>(File.ReadAllText(path));
    }

    public static UpdateFailureSummary? Latest(string data)
    {
        var path = LatestPath(data);
        if (!File.Exists(path)) return null;
        try
        {
            RuntimePaths.DemandPrivatePath(path);
            return RelayJson.Deserialize<UpdateFailureSummary>(File.ReadAllText(path));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { return Summary(Capture(data, error)); }
    }

    private static UpdateFailureSummary Summary(UpdateFailureInfo info) => new(info.Code, info.Message, info.DiagnosticId, info.Recovery, DateTimeOffset.UtcNow);
    private static string LatestPath(string data) => Path.Combine(Path.GetFullPath(data), "logs", "update", "latest.json");

    private static string DetailPath(string data, string id)
    {
        if (!id.StartsWith("update-", StringComparison.Ordinal) || id.Length != 23 || !id[7..].All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid update diagnostic identifier.");
        return Path.Combine(Path.GetFullPath(data), "logs", "update", id + ".json");
    }
}
