using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed partial class InstallationUpdate
{
    private const int NativePublicationDeadlineSeconds = 60;
    private sealed record NativeHelperImage(Guid SessionId, Dictionary<string, string?> Files);
    private sealed record NativeSession(Guid Id, string Root, string Distribution, string User);
    private sealed record NativePublication(string TransactionId, string InventoryHash, Dictionary<Guid, string> BaseImages);

    private string NativeAdapterDirectory(InstallationUpdateState state) => Path.Combine(directory, state.Id, "native-adapters");

    private string NativeHelperPrefix(InstallationUpdateState state) =>
        Path.GetRelativePath(installation, state.Activation!.ReleaseRoot).Replace('\\', '/') + "/app/wsl/";

    private byte[] ReadStagedNativeHelper(InstallationUpdateState state, string name)
    {
        var staged = state.Files.SingleOrDefault(item => item.Name == NativeHelperPrefix(state) + name)
            ?? throw Failure("update_adapter_payload_invalid", "A staged native helper is missing.");
        var path = Inside(Stage(state), staged.Name);
        if (staged.After is null || OptionalHash(path) != staged.After)
            throw Failure("update_adapter_payload_invalid", "A staged native helper has changed.");
        return File.ReadAllBytes(path);
    }

    private bool IsHistoricalApproval(InstallationUpdateState state, WorkRecord work)
    {
        if (work.Request is not { Kind: "message", SessionId: { } id }) return false;
        var session = new SqliteDocuments<RelaySession>(new RelayDatabase(data, readOnly: true), "sessions")
            .FindAsync(id, CancellationToken.None).GetAwaiter().GetResult();
        if (session?.Definition is not { } definition || !ProjectPolicy.IsSessionRequest(work.Request, definition) ||
            session.Template.Client is not ("codex" or "claude") || !NativeLayout.IsSession(session.Root, id)) return false;
        var retiredStop = WorkExecution.ApprovalStopPending(work) && session.RetiredForCancellationId == work.Request.Id &&
                          (work.CancellationRequestedAt is not null || work.ApprovalExpiredAt is not null);
        if (!retiredStop && (work.ApprovalStopRequestedAt is not null || session.RetiredForCancellationId is not null)) return false;
        try
        {
            var request = RelayJson.Serialize(new
            {
                action = "approval-history", session = new { session.Id, session.Root, template = new { session.Template.User } },
                session.Template.Client, requestId = work.Request.Id, scope = definition.Scope, retiredStop,
                updated = ApprovalExpiry.StartedAt(work).ToUnixTimeSeconds(),
                layoutSource = Convert.ToBase64String(ReadStagedNativeHelper(state, "native_layout.py")),
                layoutDefinition = Convert.ToBase64String(ReadStagedNativeHelper(state, "native-layout.json"))
            });
            var script = System.Text.Encoding.UTF8.GetString(ReadStagedNativeHelper(state, "session_adapter_files.py"));
            var response = WslMaintenance.RunAsync(new(session.Template.Distribution, session.Template.User, script, request, 25),
                CancellationToken.None).GetAwaiter().GetResult();
            if (response.ExitCode != 0) return false;
            using var proof = JsonDocument.Parse(response.Output);
            return proof.RootElement.GetProperty("requestId").GetGuid() == work.Request.Id &&
                   proof.RootElement.GetProperty("historical").ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (error is IOException or JsonException or TimeoutException or RelayException)
        { return false; }
    }

    private NativeSession[] ReadNativeSessionInventory()
    {
        // The live database is stopped and owned here. Opening the verified SQLite backup can create WAL sidecars.
        var sessions = new SqliteDocuments<RelaySession>(new RelayDatabase(data, readOnly: true), "sessions")
            .ListAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (sessions.Any(session => session.Id == Guid.Empty || session.Template is null || !NativeLayout.IsSession(session.Root, session.Id)))
            throw Failure("update_adapter_scope_invalid", "A retained native session does not identify an owned root.");
        return sessions.OrderBy(session => session.Id)
            .Select(session => new NativeSession(session.Id, session.Root, session.Template.Distribution, session.Template.User)).ToArray();
    }

    private void CaptureNativeSessionInventory(InstallationUpdateState state) =>
        AtomicText(Path.Combine(NativeAdapterDirectory(state), "inventory.json"), RelayJson.Serialize(ReadNativeSessionInventory()));

    private void DemandNativeSessionsIdle(InstallationUpdateState state)
    {
        var sessions = ReadNativeSessionInventory();
        if (sessions.Length == 0) return;
        var script = System.Text.Encoding.UTF8.GetString(ReadStagedNativeHelper(state, "session_adapter_files.py"));
        var layoutSource = Convert.ToBase64String(ReadStagedNativeHelper(state, "native_layout.py"));
        var layoutDefinition = Convert.ToBase64String(ReadStagedNativeHelper(state, "native-layout.json"));
        foreach (var session in sessions)
        {
            var request = RelayJson.Serialize(new { action = "check-idle", session = new { session.Id, session.Root,
                template = new { session.User } }, layoutSource, layoutDefinition });
            try
            {
                var result = WslMaintenance.RunAsync(new(session.Distribution, session.User, script, request, 15), CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (result.ExitCode != 0)
                    throw Failure("update_adapter_preflight_unverified", $"Cannot verify native session {session.Id:D}. {result.Phase}: {result.Error}");
                using var response = JsonDocument.Parse(result.Output);
                if (!response.RootElement.TryGetProperty("sessionId", out var id) || !id.TryGetGuid(out var checkedId) || checkedId != session.Id ||
                    !response.RootElement.TryGetProperty("idle", out var idle) || idle.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw Failure("update_adapter_preflight_unverified", $"Native session {session.Id:D} returned an invalid preflight receipt.");
                if (!idle.GetBoolean())
                    throw Failure("update_native_session_open", $"Native session {session.Id:D} is still open. Close that conversation after its work finishes, then resume the update.");
            }
            catch (Exception error) when (error is IOException or JsonException or TimeoutException)
            { throw Failure("update_adapter_preflight_unverified", $"Cannot verify native session {session.Id:D}: {error.Message}"); }
        }
    }

    /// <summary>Reconcile checked helper bytes while deployment owns the stopped host, using the same durable before-images for rollback.</summary>
    private void ReconcileNativeAdapters(InstallationUpdateState state, bool rollback)
    {
        var root = NativeAdapterDirectory(state);
        var intent = Path.Combine(root, "publication-authorized");
        bool authorized = File.Exists(intent);
        if (rollback && !authorized) return;
        var publication = authorized ? RelayJson.Deserialize<NativePublication>(McpPrivateFiles.ReadText(intent)) : null;
        var inventory = McpPrivateFiles.ReadText(Path.Combine(root, "inventory.json"));
        var inventoryHash = McpStableProtocol.Hash(inventory);
        if (publication is not null && (publication.TransactionId != state.Id || publication.InventoryHash != inventoryHash))
            throw Failure("update_recovery_required", "The native helper publication intent or its inventory has changed.");
        var sessions = RelayJson.Deserialize<NativeSession[]>(inventory);
        if (sessions.Length == 0) return;
        if (sessions.Any(session => session.Id == Guid.Empty || !NativeLayout.IsSession(session.Root, session.Id) ||
                string.IsNullOrWhiteSpace(session.Distribution) || string.IsNullOrWhiteSpace(session.User)) ||
            sessions.Select(session => (session.Distribution.ToUpperInvariant(), session.Root)).Distinct().Count() != sessions.Length)
            throw Failure("update_adapter_scope_invalid", "A retained native session does not identify a unique owned root.");

        var prefix = NativeHelperPrefix(state);
        var files = state.Files.Where(file => file.Name.StartsWith(prefix, StringComparison.Ordinal)).ToDictionary(file => file.Name[prefix.Length..], file =>
        {
            if (file.After is null) throw Failure("update_adapter_payload_invalid", "The candidate adapter hash is missing.");
            var path = Inside(Stage(state), file.Name);
            if (OptionalHash(path) != file.After)
                throw Failure("update_adapter_payload_invalid", "A staged native helper has changed.");
            return (string?)Convert.ToBase64String(File.ReadAllBytes(path));
        }, StringComparer.Ordinal);
        var layout = files["native_layout.py"];
        var layoutDefinition = files["native-layout.json"];
        var script = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(files["session_adapter_files.py"]!));
        var names = files.Keys.Order(StringComparer.Ordinal).ToArray();

        var hashes = new Dictionary<Guid, string>();
        NativeHelperImage ReadImage(NativeSession session)
        {
            var path = Path.Combine(root, session.Id.ToString("N") + ".json");
            if (!File.Exists(path))
            {
                if (authorized) throw Failure("update_recovery_required", "A native helper before-image is missing.");
                var captured = Run(session, "capture", null, null);
                AtomicText(path, captured);
            }
            var hash = Hash(path);
            if (publication is not null && (!publication.BaseImages.TryGetValue(session.Id, out var expectedHash) || expectedHash != hash))
                throw Failure("update_recovery_required", "A retained native helper before-image has changed.");
            hashes.Add(session.Id, hash);
            var image = RelayJson.Deserialize<NativeHelperImage>(McpPrivateFiles.ReadText(path));
            if (image.SessionId != session.Id || !image.Files.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(names))
                throw Failure("update_recovery_required", "A native helper before-image has a different session identity or file set.");
            return image;
        }

        string Run(NativeSession session, string action, Dictionary<string, string?>? expected, Dictionary<string, string?>? desired)
        {
            var request = RelayJson.Serialize(new
            {
                action, session = new { session.Id, session.Root, template = new { session.User } },
                names, layoutSource = layout, layoutDefinition, expected, files = desired
            });
            // The old relay host is stopped here, so a new WSL process may need a full startup window.
            var outcome = WslMaintenance.RunAsync(new(session.Distribution, session.User, script, request, NativePublicationDeadlineSeconds), CancellationToken.None)
                .GetAwaiter().GetResult();
            if (outcome.ExitCode != 0)
                throw Failure("update_adapter_sync_failed", $"Native helper {action} could not be confirmed for session {session.Id:D}. " +
                    $"{outcome.Phase} after {outcome.Seconds:0.0}s (timeout={outcome.TimedOut}, inputComplete={outcome.InputCompleted}): {outcome.Error}");
            using var response = JsonDocument.Parse(outcome.Output);
            if (response.RootElement.GetProperty("sessionId").GetGuid() != session.Id)
                throw Failure("update_adapter_sync_failed", "Native helper maintenance reported a different session.");
            return outcome.Output;
        }

        var images = sessions.Select(ReadImage).ToArray();
        if (!authorized) AtomicText(intent, RelayJson.Serialize(new NativePublication(state.Id, inventoryHash, hashes)));
        else if (publication!.BaseImages.Count != sessions.Length)
            throw Failure("update_recovery_required", "The retained session inventory has changed.");
        for (var index = 0; index < sessions.Length; index++)
        {
            var before = images[index].Files;
            Run(sessions[index], "replace", rollback ? files : before, rollback ? before : files);
        }
    }
}
