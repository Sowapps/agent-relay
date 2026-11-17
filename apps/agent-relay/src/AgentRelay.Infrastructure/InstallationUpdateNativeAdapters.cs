using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed partial class InstallationUpdate
{
    private sealed record NativeHelperImage(Guid SessionId, Dictionary<string, string?> Files);
    private sealed record NativePublication(string TransactionId, Dictionary<Guid, string> BaseImages);

    /// <summary>Reconcile checked helper bytes while deployment owns the stopped host, using the same durable before-images for rollback.</summary>
    private void ReconcileNativeAdapters(InstallationUpdateState state, string snapshot, bool rollback)
    {
        var root = Path.Combine(directory, state.Id, "native-adapters");
        var intent = Path.Combine(root, "publication-authorized");
        bool authorized = File.Exists(intent);
        if (rollback && !authorized) return;
        var publication = authorized ? RelayJson.Deserialize<NativePublication>(McpPrivateFiles.ReadText(intent)) : null;
        if (publication is not null && publication.TransactionId != state.Id)
            throw Failure("update_recovery_required", "Native helper publication intent belongs to another update.");
        InstallationSnapshot.Verify(snapshot);
        var sessions = new SqliteDocuments<RelaySession>(new RelayDatabase(snapshot, readOnly: true), "sessions")
            .ListAsync(CancellationToken.None).GetAwaiter().GetResult().OrderBy(session => session.Id).ToArray();
        if (sessions.Length == 0) return;
        if (sessions.Any(session => session.Id == Guid.Empty || session.Template is null || !NativeLayout.IsSession(session.Root, session.Id)) ||
            sessions.Select(session => (session.Template.Distribution.ToUpperInvariant(), session.Root)).Distinct().Count() != sessions.Length)
            throw Failure("update_adapter_scope_invalid", "A retained native session does not identify a unique owned root.");

        var prefix = Path.GetRelativePath(installation, state.Activation!.ReleaseRoot).Replace('\\', '/') + "/app/wsl/";
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
        NativeHelperImage ReadImage(RelaySession session)
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

        string Run(RelaySession session, string action, Dictionary<string, string?>? expected, Dictionary<string, string?>? desired)
        {
            var request = RelayJson.Serialize(new
            {
                action, session = new { session.Id, session.Root, template = new { session.Template.User } },
                names, layoutSource = layout, layoutDefinition, expected, files = desired
            });
            var outcome = WslMaintenance.RunAsync(new(session.Template.Distribution, session.Template.User, script, request, 25), CancellationToken.None)
                .GetAwaiter().GetResult();
            if (outcome.ExitCode != 0)
                throw Failure("update_adapter_sync_failed", $"Native helper {action} could not be confirmed for session {session.Id:D}. {outcome.Phase}: {outcome.Error}");
            using var response = JsonDocument.Parse(outcome.Output);
            if (response.RootElement.GetProperty("sessionId").GetGuid() != session.Id)
                throw Failure("update_adapter_sync_failed", "Native helper maintenance reported a different session.");
            return outcome.Output;
        }

        var images = sessions.Select(ReadImage).ToArray();
        if (!authorized) AtomicText(intent, RelayJson.Serialize(new NativePublication(state.Id, hashes)));
        else if (publication!.BaseImages.Count != sessions.Length)
            throw Failure("update_recovery_required", "The retained session inventory has changed.");
        for (var index = 0; index < sessions.Length; index++)
        {
            var before = images[index].Files;
            Run(sessions[index], "replace", rollback ? files : before, rollback ? before : files);
        }
    }
}
