using System.Security.Cryptography;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record NativeInboxCursor(long After, string? LastError = null);

// Owns bounded cursor import and reply correlation; execution reuses the same import owner.
public sealed class ScopedInbox(SessionService sessions, WslBridge bridge, SqliteWorkStore store, ProjectJournal journal,
    SqliteDocuments<NativeInboxCursor> cursors, ScopedSessionContext context)
{
    public async Task<IReadOnlyList<string>> RefreshAsync(Guid projectId, Guid recipientId, Guid? sessionId, CancellationToken token)
    {
        // Bounded refresh. Per-session cursors advance only after Windows durably imports each page.
        var refreshKey = new Guid(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("inbox-refresh:" + projectId + ":" + recipientId)).AsSpan(0, 16));
        var rotation = sessionId is null ? await cursors.FindAsync(refreshKey, token) : null;
        var page = await journal.SessionsAsync(new(projectId, SessionId: sessionId, Cursor: rotation is { After: > 0 } ? rotation.After : null, Limit: 4), recipientId, token);
        if (page.Items.Count == 0 && rotation is { After: > 0 }) page = await journal.SessionsAsync(new(projectId, SessionId: sessionId, Limit: 4), recipientId, token);
        var warnings = new List<string>();
        foreach (var record in page.Items.Where(item => item.State is not ("preparing" or "setup_failed")))
        {
            var cursor = await cursors.FindAsync(record.Id, token) ?? new(0);
            try
            {
                context.ValidateDefinition(record.Definition!);
                var profile = await sessions.ResolveAsync(record.Id, record.ProfileId, token, allowClosed: true);
                var response = await bridge.CallAsync(profile, "inbox", new { After = cursor.After, Limit = 50 }, token);
                await ImportAsync(record, response, token);
                if (response.TryGetProperty("nextCursor", out var next)) await cursors.SaveAsync(record.Id, new(next.GetInt64()), token);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                var code = error is RelayException relay ? relay.Code : error is TimeoutException ? "timeout" : "import_unconfirmed";
                warnings.Add(record.Name + ": " + code + "; retained Windows messages remain readable.");
                await cursors.SaveAsync(record.Id, cursor with { LastError = code }, token);
            }
        }
        if (sessionId is null) await cursors.SaveAsync(refreshKey, new(page.NextCursor ?? 0), token);
        return warnings;
    }
    public async Task ImportAsync(RelaySession record, System.Text.Json.JsonElement response, CancellationToken token)
    {
        var scope = record.Definition!.Scope;
        foreach (var value in response.GetProperty("messages").EnumerateArray())
        {
            var message = RelayJson.Deserialize<InboxMessage>(value.GetRawText());
            if (message.ProjectId != scope.ProjectId || message.SenderEndpointId != scope.RecipientId || message.RecipientEndpointId != scope.SenderId || message.SessionId != record.Id)
                throw new InvalidDataException("Native message project/session/origin binding was not confirmed.");
            message = message with { ProfileId = scope.RecipientId.ToString("D") };
            if (message.InReplyTo is { } replyId)
            {
                var original = await store.FindAsync(replyId, token);
                if (original?.Request.Scope != scope || original.Request.SessionId != record.Id) throw new InvalidDataException("Native reply does not belong to the selected task/project.");
                if (original.State is "unknown" or "awaiting_approval") await store.SaveAsync(original with { State = "completed", Result = new(message.Sender, message.Text), Error = null, ErrorCode = null, UpdatedAt = DateTimeOffset.UtcNow }, token);
            }
            await store.AddInboxAsync(message, token);
        }
    }
}
