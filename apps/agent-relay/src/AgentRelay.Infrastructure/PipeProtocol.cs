using System.Buffers.Binary;
using System.Text.Json;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record PipeRequest(string Operation, RelayMessage? Message = null, Guid? RequestId = null, int Version = 1,
    WorkRequest? Work = null, RelaySettings? Configuration = null, string? ProfileId = null,
    Guid? SessionId = null, string? Name = null, JournalQuery? Query = null, ProjectAction? ProjectAction = null, string? AccessToken = null,
    ProjectConfiguration? ProjectsConfiguration = null, ConnectionRequest? Connection = null);
public sealed record ConnectionRequest(Guid Id, Guid EndpointId, Guid[] ProjectIds, bool Revoke = false);
public sealed record RelayFailure(string Code, string Message);
public sealed record PipeResponse(RelayExchange? Exchange = null, RelayFailure? Error = null, WorkRecord? Work = null,
    IReadOnlyList<WorkSummary>? History = null, IReadOnlyList<InboxMessage>? Inbox = null, RelaySettings? Configuration = null,
    IReadOnlyList<string>? Warnings = null, AgentStatus? Agent = null,
    IReadOnlyList<ProfileContext>? Profiles = null, SessionDetail? Session = null,
    IReadOnlyList<SessionDetail>? Sessions = null, JournalPage<WorkSummary>? ResultsPage = null,
    JournalPage<InboxMessage>? InboxPage = null, JournalPage<RelaySession>? SessionsPage = null, ProjectReply? Project = null, Guid? ConnectionId = null);

public static class PipeDeadlines
{
    public static TimeSpan For(string operation) => TimeSpan.FromSeconds(operation switch
    {
        "start_profile" or "refresh_inbox" => 90,
        "agent_status" => 30,
        "open_session" => 180,
        "project" => 180,
        "session_start" or "session_reopen" => 100,
        "session_status" or "session_close" or "session_interrupt" => 30,
        _ => 5
    });
}

public static class PipeFrames
{
    public const int MaximumBytes = 1024 * 1024;

    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, RelayJson.Options);
        if (body.Length > MaximumBytes) throw new InvalidDataException("The frame is too large.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, body.Length);
        await stream.WriteAsync(header, token).ConfigureAwait(false);
        await stream.WriteAsync(body, token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }

    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token).ConfigureAwait(false);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length is <= 0 or > MaximumBytes) throw new InvalidDataException("Invalid frame size.");
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, token).ConfigureAwait(false);
        return JsonSerializer.Deserialize<T>(body, RelayJson.Options) ?? throw new JsonException("The request cannot be null.");
    }
}
