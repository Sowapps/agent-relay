using System.Text.Json;
using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed partial class InstallationUpdate
{
    private sealed record NativeHelperImage(Guid SessionId, Dictionary<string, string?> Files);
    private sealed record NativeSession(Guid Id, string Root, string Distribution, string User);
    private sealed record NativePublication(string TransactionId, string InventoryHash, Dictionary<Guid, string> BaseImages);

    private string NativeAdapterDirectory(InstallationUpdateState state) => Path.Combine(directory, state.Id, "native-adapters");

    private void CaptureNativeSessionInventory(InstallationUpdateState state)
    {
        // The live database is stopped and owned here. Opening the verified SQLite backup can create WAL sidecars.
        var sessions = new SqliteDocuments<RelaySession>(new RelayDatabase(data, readOnly: true), "sessions")
            .ListAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (sessions.Any(session => session.Id == Guid.Empty || session.Template is null || !NativeLayout.IsSession(session.Root, session.Id)))
            throw Failure("update_adapter_scope_invalid", "A retained native session does not identify an owned root.");
        var inventory = sessions.OrderBy(session => session.Id)
            .Select(session => new NativeSession(session.Id, session.Root, session.Template.Distribution, session.Template.User)).ToArray();
        AtomicText(Path.Combine(NativeAdapterDirectory(state), "inventory.json"), RelayJson.Serialize(inventory));
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
            var outcome = WslMaintenance.RunAsync(new(session.Distribution, session.User, script, request, 25), CancellationToken.None)
                .GetAwaiter().GetResult();
            if (outcome.ExitCode != 0)
                throw Failure("update_adapter_sync_failed", $"Native helper {action} could not be confirmed for session {session.Id:D}. {outcome.Phase}: {outcome.Error}");
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
