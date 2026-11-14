using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Core;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed record InstallationActivation(string ReleaseRoot, string PreviousPointer, string? PreviousTransport,
    Guid[] TransportInstances, bool StartupEnabled, bool AdmissionReleased = false, bool? WasRunning = null, string? SelectionHash = null,
    InstallationStartupSnapshot? Startup = null, bool ResumeAuthorized = false, DateTimeOffset? AuthorizedAt = null);

public sealed partial class InstallationUpdate
{
    private InstallationUpdateState PrepareSelected(string candidate, string manifestPath, string version, string contractHash, Dictionary<string, string> declared)
    {
        var contract = RelayJson.Deserialize<McpIdentity>(File.ReadAllText(Path.Combine(candidate, "app", "mcp-contract.json")));
        if (contract.Compatibility?.Transport != McpStableProtocol.Requirement)
            throw Failure("transport_incompatible", "Qualify the candidate's exact stable transport requirement before preparation.");
        var pointerPath = Path.Combine(data, "mcp-runtime.json");
        var previous = File.ReadAllText(pointerPath);
        var pointer = JsonNode.Parse(previous)!;
        var currentExecutable = pointer["executable"]!.GetValue<string>();
        if (!Within(currentExecutable, installation) || pointer["distribution"]?.GetValue<string>() != "portable-win-x64")
            throw Failure("update_scope_invalid", "The selected host must belong to this initialized portable installation.");
        var digest = Hash(manifestPath);
        if (File.Exists(journal))
        {
            var prior = Read();
            if (prior.CandidateHash == digest && prior.Phase != "rolled_back")
            {
                if (prior.Phase == "preparing") return StageSelected(prior, candidate, manifestPath);
                if (prior.Phase != "complete") DemandStage(prior);
                if (prior.Phase == "complete") DemandPublished(prior);
                return prior;
            }
            if (prior.Phase is not ("complete" or "rolled_back")) throw Failure("update_conflict", "Resume or roll back the retained transaction before selecting another candidate.");
            if (prior.Version == version && prior.CandidateHash != digest) throw Failure("update_version_conflict", "Different bytes cannot reuse a prepared version.");
            AtomicText(Path.Combine(directory, prior.Id, "journal.json"), RelayJson.Serialize(prior));
        }
        if (McpConnections.List(data).Any(item => item.State is not ("offline" or "closed")))
            throw Failure("update_initial_migration_required", "A legacy MCP connection must first be explicitly migrated to the stable transport; no component was changed.");
        if (!File.Exists(RuntimePaths.TransportSelection(data)))
            throw Failure("update_initial_migration_required", "Prepare and qualify the initial stable-transport installation before using automatic selected-release updates.");
        _ = McpProcessSelections.Read(RuntimePaths.TransportSelection(data));
        _ = runtime.Inspect(currentExecutable);
        DemandNativeCompatibility(declared, currentExecutable);
        var previousVersion = RelayJson.Deserialize<McpIdentity>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(currentExecutable)!, "mcp-contract.json"))).Version;
        if (previousVersion == version) throw Failure("update_version_conflict", "Select a new numbered RC or stable version for different installed bytes.");
        var historyPath = Path.Combine(data, "mcp-contract-history.json");
        _ = ContractHistory(File.Exists(historyPath) ? File.ReadAllText(historyPath) : null, version, contractHash);
        var manifest = Path.Combine(candidate, "app", "mcp-operations.json");
        if (!File.Exists(manifest)) throw Failure("update_candidate_invalid", "A stable-transport release must declare its operation revision manifest.");
        var operations = RelayJson.Deserialize<McpOperationManifest>(File.ReadAllText(manifest));
        var previousOperations = Path.Combine(Path.GetDirectoryName(currentExecutable)!, "mcp-operations.json");
        McpStableProtocol.ValidateRevisionHistory(File.Exists(previousOperations)
            ? RelayJson.Deserialize<McpOperationManifest>(File.ReadAllText(previousOperations)).Operations : [], operations.Operations);
        if (operations.RegistryHash != contract.Compatibility.OperationsHash) throw Failure("update_candidate_invalid", "Operation manifest disagrees with candidate identity.");
        var root = RuntimePaths.ReleaseDirectory(installation, digest);
        var slot = Path.GetRelativePath(installation, root).Replace('\\', '/');
        if (declared.Keys.Count(name => name.StartsWith("transport/", StringComparison.Ordinal)) != 1)
            throw Failure("update_candidate_invalid", "The fixed entry requires the qualified single-file transport payload.");
        var expected = declared.ToDictionary(item => item.Key.StartsWith("app/", StringComparison.Ordinal) || item.Key.StartsWith("transport/", StringComparison.Ordinal)
            ? slot + "/" + item.Key : item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase);
        expected[slot + "/app/installation.json"] = McpStableProtocol.Hash(RuntimePaths.InstallationDescriptor(installation, Path.Combine(root, "app")));
        expected[Path.GetRelativePath(installation, RuntimePaths.TransportBootstrapExecutable(data)).Replace('\\', '/')] = declared["transport/AgentRelay.Transport.exe"];
        expected[Path.GetRelativePath(installation, RuntimePaths.TransportEntryScript(data)).Replace('\\', '/')] = McpStableProtocol.Hash(McpTransportInstallation.EntryScript(data));
        expected.Add(slot + "/release-manifest.json", digest);
        var files = expected.OrderBy(item => item.Key, StringComparer.Ordinal).Select(item => new InstallationUpdateFile(item.Key, OptionalHash(Inside(installation, item.Key)), item.Value)).ToArray();
        var id = Guid.NewGuid().ToString("N")[..12];
        var startup = runtime.CaptureStartup(currentExecutable);
        var state = new InstallationUpdateState(2, id, digest, installation, data, version, contractHash, "preparing", files, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, 0,
            Activation: new(root, previous, File.Exists(RuntimePaths.TransportSelection(data)) ? File.ReadAllText(RuntimePaths.TransportSelection(data)) : null,
                McpTransportPresences.Read(RuntimePaths.TransportStateDirectory(data)).Where(item => item.State is not ("offline" or "closed")).Select(item => item.InstanceId).ToArray(), startup.Enabled, Startup: startup));
        Directory.CreateDirectory(Path.Combine(directory, id));
        CacheRecoveryRunner(state, candidate, declared);
        Save(state);
        return StageSelected(state, candidate, manifestPath);
    }

    private InstallationUpdateState StageSelected(InstallationUpdateState state, string candidate, string manifestPath)
    {
        var slot = Path.GetRelativePath(installation, state.Activation!.ReleaseRoot).Replace('\\', '/');
        var watch = System.Diagnostics.Stopwatch.StartNew();
        foreach (var file in state.Files)
        {
            if (OptionalHash(Inside(Stage(state), file.Name)) == file.After) continue;
            var sourceName = file.Name.StartsWith(slot + "/", StringComparison.Ordinal) ? file.Name[(slot.Length + 1)..] : file.Name;
            if (file.Name == slot + "/app/installation.json")
                AtomicText(Inside(Stage(state), file.Name), RuntimePaths.InstallationDescriptor(installation, Path.Combine(state.Activation.ReleaseRoot, "app")));
            else if (IsBootstrapFile(file.Name))
            {
                if (Same(Inside(installation, file.Name), RuntimePaths.TransportEntryScript(data))) AtomicText(Inside(Stage(state), file.Name), McpTransportInstallation.EntryScript(data));
                else Replace(Inside(Stage(state), file.Name), Inside(candidate, "transport/AgentRelay.Transport.exe"));
            }
            else
            {
            var source = sourceName == "release-manifest.json" ? manifestPath : Inside(candidate, sourceName);
            Replace(Inside(Stage(state), file.Name), source);
            }
            if (OptionalHash(Inside(Stage(state), file.Name)) != file.After) throw Failure("update_candidate_invalid", "Candidate changed during preparation.");
        }
        return Save(state with { Phase = "prepared", BytesPrepared = state.Files.Sum(file => new FileInfo(Inside(Stage(state), file.Name)).Length), PreparationSeconds = state.PreparationSeconds + watch.Elapsed.TotalSeconds });
    }

    private InstallationUpdateState ResumeSelected()
    {
        var state = Read();
        if (state.Activation is not { } activation) throw Failure("update_legacy_transaction", "Finish this legacy transaction with its retained recovery procedure before using selected-release updates.");
        if (state.Phase is "rolling_back" or "files_restored" or "recovery_required") return RollbackSelected(state);
        if (state.Phase == "rolled_back") return state;
        if (state.Phase == "preparing") throw Failure("update_candidate_invalid", "The candidate stage is incomplete. Repeat preparation with the same verified source.");
        if (!activation.ResumeAuthorized)
        {
            state = AuthorizeSelected();
            activation = state.Activation!;
        }
        if (state.Phase == "complete")
        {
            DemandPublished(state);
            DemandSelection(state);
            ReleaseSelectedBarrier(state);
            return state;
        }
        if (state.Phase == "reopening") return FinishSelectedUpdate(state);
        var marker = Path.Combine(data, "promotion.lock");
        if (!File.Exists(marker))
        {
            if (activation.AdmissionReleased) throw Failure("update_recovery_required", "Admission already reopened; do not recreate an earlier update boundary.");
            using var barrier = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            barrier.Write(System.Text.Encoding.ASCII.GetBytes(state.Id)); barrier.Flush(true);
        }
        DemandBarrier(state);
        var previousExecutable = JsonNode.Parse(activation.PreviousPointer)!["executable"]!.GetValue<string>();
        var snapshot = Path.Combine(directory, state.Id, "data-before");
        try
        {
            if (state.Phase is "prepared" or "awaiting_work")
            {
                var host = runtime.Inspect(previousExecutable);
                state = SaveIfChanged(state with { Phase = "awaiting_work", Activation = activation = activation with { WasRunning = activation.WasRunning ?? (host is not null) } });
                try
                {
                    if (host is not null) runtime.ConfirmDrain();
                    DemandUpdateIdle();
                }
                catch (RelayException error) when (error.Code is "busy" or "update_uncertain_work")
                { return SaveIfChanged(state with { Error = error.Message, ErrorCode = error.Code }); }
                if (host is not null) runtime.Stop(host);
                using (RelayLocation.OpenHostOwnership(data))
                {
                    DemandUpdateIdle();
                    InstallationSnapshot.Capture(data, snapshot);
                    state = Save(state with { Phase = "snapshot_ready", Error = null });
                }
            }
            InstallationSnapshot.Verify(snapshot);
            if (state.Phase is "snapshot_ready" or "applying")
            {
                DemandStage(state);
                using var host = RelayLocation.OpenHostOwnership(data);
                state = Save(state with { Phase = "applying", Error = null, ErrorCode = null });
                foreach (var file in state.Files.Where(file => !IsBootstrapFile(file.Name)))
                {
                    PublishFile(state, file);
                }
                state = Save(state with { Phase = "selecting" });
            }
            if (state.Phase == "selecting")
            {
                using var host = RelayLocation.OpenHostOwnership(data);
                DemandPublished(state, includeBootstrap: false);
                var selected = ComposeSelection(state);
                var selectionText = JsonSerializer.Serialize(selected, McpProcessSelections.Json);
                var current = File.ReadAllText(Path.Combine(data, "mcp-runtime.json"));
                var pointer = SelectedPointer(state, selected);
                if (!EquivalentJson(current, activation.PreviousPointer) && !EquivalentJson(current, pointer)) throw Failure("update_conflict", "Runtime selection changed outside this transaction.");
                // The barrier guards both writes; readers may observe either only while admission is suspended.
                PublishKnownText(Path.Combine(data, "mcp-runtime.json"), activation.PreviousPointer, pointer);
                PublishKnownText(RuntimePaths.TransportSelection(data), activation.PreviousTransport, selectionText);
                PublishKnownText(Path.Combine(data, "mcp-contract.json"), SnapshotText(state, "mcp-contract.json"), File.ReadAllText(Path.Combine(activation.ReleaseRoot, "app", "mcp-contract.json")));
                var beforeHistory = SnapshotText(state, "mcp-contract-history.json");
                PublishKnownText(Path.Combine(data, "mcp-contract-history.json"), beforeHistory, ContractHistory(beforeHistory, state.Version, state.ContractHash));
                state = Save(state with { Phase = "selected", Activation = activation = activation with { SelectionHash = McpStableProtocol.Hash(selectionText) } });
            }
            if (state.Phase is "selected" or "awaiting_transports" or "publishing_bootstrap")
            {
                if (state.Phase != "selected" && !TransportsReady(state)) return AwaitTransports(state);
                DemandSelection(state);
                var executable = Path.Combine(activation.ReleaseRoot, "app", "AgentRelay.Windows.exe");
                if (runtime.Inspect(executable) is null) runtime.Start(executable);
                runtime.DemandReady(executable, state.Version, state.ContractHash);
                InstallationSnapshot.DemandPreserved(data, snapshot);
                if (runtime.StartupEnabled(executable) != activation.StartupEnabled) throw Failure("update_startup_changed", "The existing startup preference changed during activation.");
                if (!TransportsReady(state)) return AwaitTransports(state);
                state = Save(state with { Phase = "publishing_bootstrap", Error = null });
                try { foreach (var file in state.Files.Where(file => IsBootstrapFile(file.Name))) PublishFile(state, file); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { return SaveIfChanged(state with { ErrorCode = "update_bootstrap_pending", Error = "Bootstrap publication is pending; inspect file access or an overlapping launch, then resume. " + error.Message }); }
                DemandPublished(state);
                if (activation.Startup is { } startupBefore) runtime.ReconcileStartup(executable, startupBefore, restore: false);
                state = Save(state with { Phase = "reopening", Error = null, ErrorCode = null, Activation = activation = activation with { AdmissionReleased = true } });
            }
            if (state.Phase == "reopening")
            {
                state = FinishSelectedUpdate(state);
            }
            return state;
        }
        catch (Exception primary)
        {
            state = state with { Error = primary.Message };
            RetainFailure(state, primary);
            if (state.Activation?.AdmissionReleased == true) throw;
            try { RollbackSelected(state); }
            catch (Exception recovery) { throw new AggregateException("Update failed and recovery needs review; admission remains suspended.", primary, recovery); }
            throw;
        }
    }

    private InstallationUpdateState FinishSelectedUpdate(InstallationUpdateState state)
    {
        // Reopening intent is durable before marker removal; a crash here cannot authorize snapshot rollback.
        DemandSelection(state);
        var executable = Path.Combine(state.Activation!.ReleaseRoot, "app", "AgentRelay.Windows.exe");
        if (runtime.Inspect(executable) is null) runtime.Start(executable);
        runtime.DemandReady(executable, state.Version, state.ContractHash);
        ReleaseSelectedBarrier(state);
        return Save(state with { Phase = "complete" });
    }

    private InstallationUpdateState RollbackSelected(InstallationUpdateState state)
    {
        var activation = state.Activation!;
        if (activation.AdmissionReleased || state.Phase is "complete" or "reopening") throw Failure("update_recovery_required", "Admission may have reopened. Never restore an old database over new work.");
        if (state.Phase == "rolled_back") { ReleaseSelectedBarrier(state); return state; }
        DemandBarrier(state);
        if (state.Phase == "files_restored") return CompleteSelectedRollback(state);
        var actualTransport = File.Exists(RuntimePaths.TransportSelection(data)) ? File.ReadAllText(RuntimePaths.TransportSelection(data)) : null;
        var snapshotPath = Path.Combine(directory, state.Id, "data-before");
        if (!Directory.Exists(snapshotPath))
        {
            if (!EquivalentJson(File.ReadAllText(Path.Combine(data, "mcp-runtime.json")), activation.PreviousPointer) || actualTransport != activation.PreviousTransport ||
                state.Files.Any(file => OptionalHash(Inside(installation, file.Name)) != file.Before))
                throw Failure("update_recovery_required", "A coherent before-snapshot is missing after publication may have started.");
            // Cancellation before any publication must never stop a still-working old host.
            if (activation.WasRunning == true && runtime.Inspect(JsonNode.Parse(activation.PreviousPointer)!["executable"]!.GetValue<string>()) is null)
                runtime.Start(JsonNode.Parse(activation.PreviousPointer)!["executable"]!.GetValue<string>());
            state = Save(state with { Phase = "rolled_back" });
            ReleaseSelectedBarrier(state);
            return state;
        }
        var pointer = File.ReadAllText(Path.Combine(data, "mcp-runtime.json"));
        var actualExecutable = JsonNode.Parse(pointer)!["executable"]!.GetValue<string>();
        var priorExecutable = JsonNode.Parse(activation.PreviousPointer)!["executable"]!.GetValue<string>();
        var planned = ComposeSelection(state, false);
        if (!EquivalentJson(pointer, activation.PreviousPointer) && !EquivalentJson(pointer, SelectedPointer(state, planned)) ||
            actualTransport != activation.PreviousTransport && (actualTransport is null || !EquivalentJson(actualTransport, JsonSerializer.Serialize(planned, McpProcessSelections.Json))))
            throw Failure("update_conflict", "Unrecognized runtime selection; no host was stopped.");
        state = Save(state with { Phase = "rolling_back" });
        if (runtime.Inspect(actualExecutable) is { } host) runtime.Stop(host);
        using (RelayLocation.OpenHostOwnership(data))
        {
            InstallationSnapshot.Restore(data, snapshotPath);
            foreach (var file in state.Files.Where(file => !Within(Inside(installation, file.Name), activation.ReleaseRoot)))
            {
                var target = Inside(installation, file.Name);
                var actual = OptionalHash(target);
                if (actual == file.Before) continue;
                if (actual != file.After) throw Failure("update_conflict", "An installed document changed outside this transaction.");
                var backup = file.Before is null ? null : Backup(state, file.Name);
                if (backup is not null && OptionalHash(backup) != file.Before) throw Failure("update_recovery_required", "A rollback before-image is missing.");
                Replace(target, backup);
            }
        }
        state = Save(state with { Phase = "files_restored" });
        return CompleteSelectedRollback(state);
    }

    private InstallationUpdateState CompleteSelectedRollback(InstallationUpdateState state)
    {
        var activation = state.Activation!;
        var priorExecutable = JsonNode.Parse(activation.PreviousPointer)!["executable"]!.GetValue<string>();
        if (activation.WasRunning == true)
        {
            if (runtime.Inspect(priorExecutable) is null) runtime.Start(priorExecutable);
            var previousIdentity = RelayJson.Deserialize<McpIdentity>(File.ReadAllText(Path.Combine(Path.GetDirectoryName(priorExecutable)!, "mcp-contract.json")));
            runtime.DemandReady(priorExecutable, previousIdentity.Version, previousIdentity.ContractHash);
        }
        if (!EquivalentJson(File.ReadAllText(Path.Combine(data, "mcp-runtime.json")), activation.PreviousPointer))
            throw Failure("update_recovery_required", "Previous selection has not been restored.");
        if (activation.PreviousTransport is { } previousTransport)
        {
            if (!EquivalentJson(File.ReadAllText(RuntimePaths.TransportSelection(data)), previousTransport)) throw Failure("update_recovery_required", "Previous transport selection has not been restored.");
            var selected = McpProcessSelections.Parse(previousTransport);
            if (!TransportsReady(selected, activation.TransportInstances))
                return SaveIfChanged(state with { ErrorCode = "update_rollback_transport_pending", Error = "Previous transport identities are still returning; admission remains suspended." });
        }
        if (runtime.StartupEnabled(priorExecutable) != activation.StartupEnabled) throw Failure("update_startup_changed", "Restored startup preference differs.");
        if (activation.Startup is { } startupBefore) runtime.ReconcileStartup(priorExecutable, startupBefore, restore: true);
        state = Save(state with { Phase = "rolled_back", ErrorCode = null });
        ReleaseSelectedBarrier(state);
        return state;
    }

    private void DemandUpdateIdle()
    {
        SqliteWorkStore.DemandUpdateIdleAsync(data, CancellationToken.None).GetAwaiter().GetResult();
    }

    private InstallationUpdateState SaveIfChanged(InstallationUpdateState state)
    {
        var current = Read();
        return RelayJson.Serialize(current) == RelayJson.Serialize(state) ? current : Save(state);
    }

    private InstallationUpdateState AuthorizeSelected()
    {
        var state = Read();
        if (state.Activation is not { } activation || state.Phase == "preparing")
            throw Failure("update_candidate_incomplete", "Only a fully prepared selected-release transaction can be authorized.");
        if (activation.ResumeAuthorized || state.Phase is "complete" or "rolled_back") return state;
        return Save(state with { Activation = activation with { ResumeAuthorized = true, AuthorizedAt = DateTimeOffset.UtcNow } });
    }

    private InstallationUpdateState AwaitTransports(InstallationUpdateState state) => SaveIfChanged(state with { Phase = "awaiting_transports", ErrorCode = "update_transport_pending",
        Error = "Required live transport identities have not acknowledged this selection. Admission remains suspended; resume without a client restart or expiry." });

    private bool TransportsReady(InstallationUpdateState state)
        => TransportsReady(ComposeSelection(state, false), state.Activation!.TransportInstances);

    /// <summary>Same readiness invariant for publication and rollback; only the selected target differs.</summary>
    /// <seealso cref="CompleteSelectedRollback"/>
    private bool TransportsReady(McpProcessSelection selected, IReadOnlyList<Guid> required)
    {
        var leases = McpTransportPresences.Read(RuntimePaths.TransportStateDirectory(data));
        var gateway = McpProcessSelections.Identity(selected.Gateway); var worker = McpProcessSelections.Identity(selected.Worker);
        bool Current(McpTransportPresence item) => item.State == "current" && item.GatewayIdentity == gateway && item.SelectedWorkerIdentity == worker &&
            (item.ActiveWorkerIdentity is null || item.ActiveWorkerIdentity == item.SelectedWorkerIdentity);
        // A confirmed closed/dead process no longer owns client stdio; stale-but-live is never retired.
        return required.All(id => leases.Any(item => item.InstanceId == id && (Current(item) || item.State is "offline" or "closed"))) &&
            !leases.Any(item => item.State is not ("offline" or "closed") && !Current(item));
    }

    private McpProcessSelection ComposeSelection(InstallationUpdateState state, bool validate = true)
    {
        var root = state.Activation!.ReleaseRoot;
        McpProcessImage Image(string folder, string executable, string[] arguments)
        {
            var directory = Path.Combine(root, folder);
            var prefix = Path.GetRelativePath(installation, directory).Replace('\\', '/') + "/";
            return new(Path.Combine(directory, executable), arguments, state.Files.Where(file => file.Name.StartsWith(prefix, StringComparison.Ordinal))
                .ToDictionary(file => file.Name[prefix.Length..], file => file.After!));
        }
        var worker = Image("app", "AgentRelay.Windows.exe", ["mcp-stable-worker", "--data-dir", data]);
        var selected = new McpProcessSelection(1, McpStableProtocol.Requirement, worker, Image("transport", "AgentRelay.Transport.exe", []), worker with { Arguments = ["tray", "--data-dir", data] });
        if (validate) { McpProcessSelections.Validate(selected.Worker); McpProcessSelections.Validate(selected.Gateway); }
        return selected;
    }

    private string SelectedPointer(InstallationUpdateState state, McpProcessSelection selected)
    {
        var pointer = JsonNode.Parse(state.Activation!.PreviousPointer)!;
        pointer["executable"] = selected.Worker.Executable;
        pointer["mcpExecutable"] = RuntimePaths.TransportBootstrapExecutable(data);
        pointer["transportSelection"] = RuntimePaths.TransportSelection(data);
        return pointer.ToJsonString();
    }

    private void DemandSelection(InstallationUpdateState state)
    {
        var selected = ComposeSelection(state);
        if (!EquivalentJson(File.ReadAllText(Path.Combine(data, "mcp-runtime.json")), SelectedPointer(state, selected)) ||
            !EquivalentJson(File.ReadAllText(RuntimePaths.TransportSelection(data)), JsonSerializer.Serialize(selected, McpProcessSelections.Json)))
            throw Failure("update_conflict", "Active selection does not match this complete transaction.");
    }

    private static bool EquivalentJson(string left, string right) => McpStableProtocol.HashJson(JsonSerializer.Deserialize<JsonElement>(left)) == McpStableProtocol.HashJson(JsonSerializer.Deserialize<JsonElement>(right));
    private static void PublishKnownText(string path, string? before, string after)
    {
        var actual = File.Exists(path) ? File.ReadAllText(path) : null;
        if (actual != before && actual != after) throw Failure("update_conflict", "Managed selection metadata changed outside this transaction: " + Path.GetFileName(path));
        if (actual != after) AtomicText(path, after);
    }

    private string? SnapshotText(InstallationUpdateState state, string name)
    {
        var path = Path.Combine(directory, state.Id, "data-before", name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static string ContractHistory(string? before, string version, string hash)
    {
        var history = before is null ? new JsonObject() : JsonNode.Parse(before)!.AsObject();
        if (history[version] is { } known && known.GetValue<string>() != hash) throw Failure("update_version_conflict", "This version already identifies another MCP contract.");
        history[version] = hash;
        return history.ToJsonString();
    }
    private void ReleaseSelectedBarrier(InstallationUpdateState state)
    {
        var marker = Path.Combine(data, "promotion.lock");
        if (File.Exists(marker)) { DemandBarrier(state); File.Delete(marker); }
    }

    private void ValidateActivation(InstallationUpdateState state)
    {
        if (state.CandidateHash.Length != 64 || !state.CandidateHash.All(Uri.IsHexDigit) || state.Activation is not { } activation ||
            !Same(activation.ReleaseRoot, RuntimePaths.ReleaseDirectory(installation, state.CandidateHash)) || activation.TransportInstances is null ||
            activation.TransportInstances.Any(id => id == Guid.Empty) || activation.TransportInstances.Distinct().Count() != activation.TransportInstances.Length ||
            activation.AdmissionReleased && state.Phase is not ("reopening" or "complete") ||
            state.Files.Any(file => file.Name.StartsWith("var/", StringComparison.OrdinalIgnoreCase) && !Within(Inside(installation, file.Name), activation.ReleaseRoot) && !IsBootstrapFile(file.Name)))
            throw Failure("update_recovery_required", "Release selection ownership or admission boundary is invalid.");
    }

    private bool IsBootstrapFile(string name) => Same(Inside(installation, name), RuntimePaths.TransportBootstrapExecutable(data)) || Same(Inside(installation, name), RuntimePaths.TransportEntryScript(data));

    private void DemandNativeCompatibility(Dictionary<string, string> declared, string executable)
    {
        if (!declared.Where(item => item.Key.StartsWith("app/wsl/", StringComparison.Ordinal))
            .Any(item => OptionalHash(Path.Combine(Path.GetDirectoryName(executable)!, item.Key[4..])) != item.Value)) return;
        using var database = new RelayDatabase(data, readOnly: true).Open();
        using var command = database.CreateCommand(); command.CommandText = "SELECT count(*) FROM sessions";
        if (Convert.ToInt64(command.ExecuteScalar()) != 0) throw Failure("update_adapter_sync_required", "Changed native adapters require owned-session synchronization qualification before update.");
    }
}
