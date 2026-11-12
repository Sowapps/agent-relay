using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record InstallationUpdateRequest(string Action, string Installation, string Data, string? Candidate = null);
public sealed record InstallationUpdateFile(string Name, string? Before, string? After);
public sealed record InstallationUpdateClient(Guid? BindingId, string Client);
public sealed record InstallationUpdateState(int Schema, string Id, string CandidateHash, string Installation, string Data, string Version,
    string ContractHash, string Phase, InstallationUpdateFile[] Files, InstallationUpdateClient[] Clients,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, long BytesPrepared, double PreparationSeconds,
    string? Error = null, double ApplySeconds = 0, double RollbackSeconds = 0);

/// <summary>Owns the complete installed-file transaction, its durable intent and crash reconciliation.</summary>
/// <remarks>Host drain/backup and client control are adapters. They cannot infer completion from a runtime swap.</remarks>
/// <seealso cref="McpInstallation"/>
public sealed class InstallationUpdate
{
    private readonly string installation;
    private readonly string data;
    private readonly string directory;
    private readonly string journal;

    public InstallationUpdate(string installation, string data)
    {
        this.installation = Absolute(installation);
        this.data = Absolute(data);
        if (!Same(this.data, RuntimePaths.PortableDataDirectory(this.installation)))
            throw Failure("update_scope_invalid", "Update the existing portable installation and its retained data scope.");
        directory = Path.Combine(RuntimePaths.ApplicationDirectory(this.installation), "update");
        journal = Path.Combine(directory, "current.json");
        RuntimePaths.DemandPrivatePath(journal);
    }

    public InstallationUpdateState Run(string action, string? candidate = null)
    {
        Directory.CreateDirectory(directory);
        using var ownership = new FileStream(Path.Combine(directory, "update.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.None);
        return action switch
        {
            "prepare" => Prepare(candidate ?? throw new ArgumentException("Select an extracted verified candidate.")),
            "status" => Read(),
            "apply" => Apply(),
            "activate" => Activate(),
            "verify" => Verify(),
            "rollback" => Rollback(),
            "rollback-finish" => FinishRollback(),
            _ => throw new ArgumentException("Expected prepare, status, apply, verify or rollback.")
        };
    }

    private InstallationUpdateState Prepare(string candidate)
    {
        var watch = Stopwatch.StartNew();
        candidate = Absolute(candidate);
        if (Within(candidate, installation) || Within(installation, candidate))
            throw Failure("update_scope_invalid", "Candidate preparation must be outside the active installation.");
        var manifestPath = Path.Combine(candidate, "release-manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) ?? throw new JsonException();
        var version = manifest["version"]?.GetValue<string>() ?? throw new JsonException();
        if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+(?:-rc\.[1-9]\d*)?$"))
            throw new JsonException("Expected a stable or RC candidate version.");
        if (manifest["selfContained"]?.GetValue<bool>() != true) throw new JsonException("Self-contained candidate required.");
        var declared = manifest["files"]?.Deserialize<Dictionary<string, string>>(RelayJson.Options) ?? throw new JsonException();
        var expected = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, hash) in declared)
        {
            ValidateName(name);
            if (name.StartsWith("var/", StringComparison.OrdinalIgnoreCase) || name.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                throw Failure("update_scope_invalid", "A release cannot declare user data as an update file.");
            if (!expected.TryAdd(name, hash) || Hash(Inside(candidate, name)) != hash)
                throw Failure("update_candidate_invalid", "Candidate files do not match their manifest.");
        }
        foreach (var file in Directory.EnumerateFiles(candidate, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(candidate, file).Replace('\\', '/');
            if (relative != "release-manifest.json" && !expected.ContainsKey(relative))
                throw Failure("update_candidate_invalid", "Candidate contains an unmanifested file.");
        }
        var contract = JsonNode.Parse(File.ReadAllText(Inside(candidate, "app/mcp-contract.json"))) ?? throw new JsonException();
        var contractHash = contract["contractHash"]?.GetValue<string>() ?? throw new JsonException();
        if (contract["version"]?.GetValue<string>() != version || contractHash.Length != 64)
            throw Failure("update_candidate_invalid", "Candidate version and MCP contract disagree.");
        expected.Add("release-manifest.json", Hash(manifestPath));
        AddLauncher(expected, candidate, "mcp-launcher", documentation: true);
        if (Directory.Exists(Path.Combine(data, "startup-launcher")))
            AddLauncher(expected, candidate, "startup-launcher", documentation: false);
        var digest = Hash(manifestPath);
        if (File.Exists(journal))
        {
            var prior = Read();
            if (prior.CandidateHash != digest && prior.Phase is not ("complete" or "rolled_back"))
                throw Failure("update_conflict", "Finish or roll back the existing update before selecting another candidate.");
            if (prior.CandidateHash == digest && prior.Phase != "rolled_back")
            {
                if (prior.Phase == "preparing") return PrepareFiles(prior, candidate, expected);
                DemandStage(prior);
                if (prior.Phase is "complete" or "awaiting_clients") DemandPublished(prior);
                return prior;
            }
            if (prior.Version == version && prior.CandidateHash != digest && prior.Phase != "rolled_back")
                throw Failure("update_version_conflict", "Different candidate bytes cannot reuse an installed version.");
            File.WriteAllText(Path.Combine(directory, prior.Id, "journal.json"), RelayJson.Serialize(prior));
        }
        var selected = Path.Combine(data, "mcp-runtime.json");
        if (!File.Exists(selected)) throw Failure("update_scope_invalid", "Update requires an existing initialized installation.");
        var current = JsonNode.Parse(File.ReadAllText(selected)) ?? throw new JsonException();
        if (current["distribution"]?.GetValue<string>() != "portable-win-x64") throw Failure("update_scope_invalid", "Unsupported installed layout.");
        var currentExecutable = current["executable"]?.GetValue<string>() ?? throw new JsonException();
        if (!Within(currentExecutable, installation)) throw Failure("update_scope_invalid", "Selected runtime escaped the installation.");
        var nativeChanged = declared.Where(item => item.Key.StartsWith("app/wsl/", StringComparison.Ordinal))
            .Any(item => OptionalHash(Path.Combine(Path.GetDirectoryName(currentExecutable)!, item.Key[4..])) != item.Value);
        if (nativeChanged)
        {
            using var database = new RelayDatabase(data, readOnly: true).Open();
            using var command = database.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sessions";
            if (Convert.ToInt64(command.ExecuteScalar()) != 0)
                throw Failure("update_adapter_sync_required", "Native session adapters change in this candidate. Qualify their owned-session synchronization before updating; no component was replaced.");
        }
        var priorContract = JsonNode.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(currentExecutable)!, "mcp-contract.json")));
        // Legacy installations have no root manifest. Never bless different bytes under their current version.
        if (priorContract?["version"]?.GetValue<string>() == version && !File.Exists(Path.Combine(installation, "release-manifest.json")))
        {
            foreach (var (name, hash) in declared.Where(item => item.Key.StartsWith("app/", StringComparison.Ordinal)))
                if (Hash(Path.Combine(Path.GetDirectoryName(currentExecutable)!, name[4..])) != hash)
                    throw Failure("update_version_conflict", "Different candidate bytes cannot reuse the selected stable version.");
        }
        var id = Guid.NewGuid().ToString("N")[..12];
        Directory.CreateDirectory(Path.Combine(directory, id));
        var beforeNames = ManagedFiles().Union(expected.Keys, StringComparer.OrdinalIgnoreCase);
        var files = beforeNames.Order(StringComparer.Ordinal).Select(name =>
            new InstallationUpdateFile(name, OptionalHash(Inside(installation, name)), expected.GetValueOrDefault(name))).ToArray();
        var clients = McpConnections.List(data).Where(item => item.State is not ("offline" or "closed"))
            .Select(item => new InstallationUpdateClient(item.BindingId, item.Client)).Distinct().ToArray();
        var state = new InstallationUpdateState(1, id, digest, installation, data, version, contractHash, "preparing", files, clients,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0, watch.Elapsed.TotalSeconds);
        Save(state);
        return PrepareFiles(state, candidate, expected);
    }

    private InstallationUpdateState PrepareFiles(InstallationUpdateState state, string candidate, Dictionary<string, string> expected)
    {
        var watch = Stopwatch.StartNew();
        foreach (var (name, hash) in expected)
        {
            var target = Inside(Stage(state), name);
            if (OptionalHash(target) == hash) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var source = LauncherSource(name, candidate);
            if (source is not null) File.Copy(source, target, true);
            else File.WriteAllText(target, LauncherMetadata(name));
            if (Hash(target) != hash) throw Failure("update_candidate_invalid", "Candidate changed while staging.");
        }
        return Save(state with { Phase = "prepared", PreparationSeconds = state.PreparationSeconds + watch.Elapsed.TotalSeconds,
            BytesPrepared = Directory.EnumerateFiles(Stage(state), "*", SearchOption.AllDirectories).Sum(file => new FileInfo(file).Length) });
    }

    /// <summary>Reconciles each durable file intent; repeated calls never rewrite an already matching file.</summary>
    /// <seealso cref="Rollback"/>
    private InstallationUpdateState Apply()
    {
        var state = Read();
        if (state.Phase is "complete" or "awaiting_clients") return Verify();
        if (state.Phase == "files_applied") { DemandStage(state); return state; }
        if (state.Phase is not ("prepared" or "applying")) throw Failure("update_recovery_required", "This update must be rolled back before retrying.");
        DemandStage(state);
        DemandBarrier(state);
        using var host = RelayLocation.OpenHostOwnership(data);
        var watch = Stopwatch.StartNew();
        Save(state = state with { Phase = "applying", Error = null });
        try
        {
            foreach (var file in state.Files)
            {
                var target = Inside(installation, file.Name);
                var actual = OptionalHash(target);
                var backup = Backup(state, file.Name);
                if (actual == file.After) continue;
                if (actual != file.Before) throw Failure("update_conflict", "Installed file changed during update: " + file.Name);
                if (file.Before is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    if (!File.Exists(backup)) File.Copy(target, backup);
                    if (Hash(backup) != file.Before) throw Failure("update_recovery_required", "Rollback copy is inconsistent: " + file.Name);
                }
                Replace(target, file.After is null ? null : Inside(Stage(state), file.Name));
            }
            return Save(state with { Phase = "files_applied", ApplySeconds = state.ApplySeconds + watch.Elapsed.TotalSeconds });
        }
        catch (Exception error)
        {
            Save(state with { Error = error.Message, ApplySeconds = state.ApplySeconds + watch.Elapsed.TotalSeconds });
            throw;
        }
    }

    private InstallationUpdateState Activate()
    {
        var state = Read();
        if (state.Phase != "files_applied") throw Failure("update_activation_required", "Apply all components before activating the runtime.");
        DemandBarrier(state);
        using var host = RelayLocation.OpenHostOwnership(data);
        DemandPublished(state);
        var pointerPath = Path.Combine(data, "mcp-runtime.json");
        var pointer = JsonNode.Parse(File.ReadAllText(pointerPath)) ?? throw new JsonException();
        pointer["executable"] = Path.Combine(installation, "app", "AgentRelay.Windows.exe");
        pointer["mcpExecutable"] = Path.Combine(data, "mcp-launcher", "AgentRelay.Windows.exe");
        var contractPath = Path.Combine(installation, "app", "mcp-contract.json");
        var historyPath = Path.Combine(data, "mcp-contract-history.json");
        var history = File.Exists(historyPath) ? JsonNode.Parse(File.ReadAllText(historyPath))!.AsObject() : new JsonObject();
        if (history[state.Version] is { } known && known.GetValue<string>() != state.ContractHash)
            throw Failure("update_version_conflict", "This version already identifies another MCP contract.");
        history[state.Version] = state.ContractHash;
        AtomicText(pointerPath, pointer.ToJsonString());
        AtomicText(Path.Combine(data, "mcp-contract.json"), File.ReadAllText(contractPath));
        AtomicText(historyPath, history.ToJsonString());
        return state;
    }

    /// <summary>Restores only a stopped, admission-frozen installation, never a database after new work.</summary>
    /// <seealso cref="Apply"/>
    private InstallationUpdateState Rollback()
    {
        var state = Read();
        if (state.Phase is "rolled_back" or "files_restored") return state;
        if (state.Phase is "complete" or "awaiting_clients")
            throw Failure("update_recovery_required", "Admission was reopened; do not restore an old snapshot over newer work.");
        DemandBarrier(state);
        using var host = RelayLocation.OpenHostOwnership(data);
        var watch = Stopwatch.StartNew();
        Save(state = state with { Phase = "rolling_back" });
        try
        {
            foreach (var file in state.Files.Reverse())
            {
                var target = Inside(installation, file.Name);
                var actual = OptionalHash(target);
                if (actual == file.Before) continue;
                if (actual != file.After) throw Failure("update_conflict", "File changed outside this transaction: " + file.Name);
                var source = file.Before is null ? null : Backup(state, file.Name);
                if (source is not null && OptionalHash(source) != file.Before)
                    throw Failure("update_recovery_required", "A verified rollback file is missing: " + file.Name);
                Replace(target, source);
            }
            return Save(state with { Phase = "files_restored", Error = null, RollbackSeconds = watch.Elapsed.TotalSeconds });
        }
        catch (Exception error)
        {
            Save(state with { Phase = "recovery_required", Error = error.Message, RollbackSeconds = watch.Elapsed.TotalSeconds });
            throw;
        }
    }

    private InstallationUpdateState FinishRollback()
    {
        var state = Read();
        if (state.Phase == "rolled_back") return state;
        if (state.Phase != "files_restored") throw Failure("update_recovery_required", "Restore files and the stopped data snapshot before finishing recovery.");
        DemandBarrier(state);
        if (state.Files.Any(file => OptionalHash(Inside(installation, file.Name)) != file.Before))
            throw Failure("update_recovery_required", "Restored components do not match their before-images.");
        return Save(state with { Phase = "rolled_back", Error = null });
    }

    private InstallationUpdateState Verify()
    {
        var state = Read();
        if (state.Phase is not ("files_applied" or "awaiting_clients" or "complete"))
            throw Failure("update_recovery_required", "Apply the complete candidate before verifying activation.");
        DemandPublished(state);
        var pointer = JsonNode.Parse(File.ReadAllText(Path.Combine(data, "mcp-runtime.json"))) ?? throw new JsonException();
        if (!Same(pointer["executable"]?.GetValue<string>() ?? "", Path.Combine(installation, "app", "AgentRelay.Windows.exe")))
            throw Failure("update_activation_required", "The selected runtime has not activated the complete installation.");
        if (!Guid.TryParse(pointer["connectionId"]?.GetValue<string>(), out var binding))
            throw Failure("update_activation_required", "Verify the retained scoped MCP identity before completing activation.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var response = new RelayPipeClient(data, binding).RequestAsync(new("mcp_handshake"), timeout.Token).GetAwaiter().GetResult();
        if (response.Mcp?.Identity.Version != state.Version || response.Mcp.Identity.ContractHash != state.ContractHash)
            throw Failure("update_activation_required", "The authenticated host has not activated the selected version and contract.");
        var active = McpConnections.List(data).Where(item => item.State is not ("offline" or "closed")).ToArray();
        bool Current(McpConnectionLease item) => item.BrokerVersion == state.Version && item.Version == state.Version
            && item.ContractHash == state.ContractHash && item.CatalogContract == state.ContractHash && item.State == "current";
        var pending = active.Any(item => !Current(item)) || state.Clients.Any(client =>
            !active.Any(item => item.BindingId == client.BindingId && item.Client == client.Client && Current(item)));
        return Save(state with { Phase = pending ? "awaiting_clients" : "complete", Error = pending
            ? "Reload the listed owning MCP clients; no automatic acknowledgement or request replay is permitted." : null });
    }

    private IEnumerable<string> ManagedFiles()
    {
        foreach (var folder in new[] { Path.Combine(installation, "app"), Path.Combine(data, "mcp-launcher"), Path.Combine(data, "startup-launcher") })
            if (Directory.Exists(folder))
                foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                    yield return Path.GetRelativePath(installation, file).Replace('\\', '/');
        var manifest = Path.Combine(installation, "release-manifest.json");
        if (File.Exists(manifest))
        {
            var value = JsonNode.Parse(File.ReadAllText(manifest)) ?? throw new JsonException();
            foreach (var name in value["files"]!.AsObject().Select(item => item.Key)) { ValidateName(name); yield return name; }
            yield return "release-manifest.json";
        }
    }

    private void AddLauncher(Dictionary<string, string> expected, string candidate, string launcher, bool documentation)
    {
        var prefix = Path.GetRelativePath(installation, Path.Combine(data, launcher)).Replace('\\', '/') + "/";
        foreach (var (name, hash) in expected.Where(item => item.Key.StartsWith("app/", StringComparison.Ordinal)).ToArray())
        {
            var relative = name[4..];
            if (!RetainedLauncherPayload.Includes(relative, documentation) || relative == "installation.json") continue;
            expected.Add(prefix + relative, hash);
        }
        foreach (var relative in new[] { "installation.json", launcher + ".ready" })
            expected.Add(prefix + relative, Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(LauncherMetadata(prefix + relative)))));
    }

    private string? LauncherSource(string name, string candidate)
    {
        var prefix = Path.GetRelativePath(installation, data).Replace('\\', '/') + "/";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return Inside(candidate, name);
        var relative = name[(name.IndexOf('/', prefix.Length) + 1)..];
        return relative == "installation.json" || relative.EndsWith(".ready", StringComparison.Ordinal) ? null : Inside(candidate, "app/" + relative);
    }

    private string LauncherMetadata(string name) => name.EndsWith("installation.json", StringComparison.Ordinal)
        ? RelayJson.Serialize(new { version = 1, root = Path.GetRelativePath(Path.GetDirectoryName(Inside(installation, name))!, installation) })
        : "Managed retained launcher; updated with the complete installation.\n";

    private InstallationUpdateState Read()
    {
        var state = RelayJson.Deserialize<InstallationUpdateState>(File.ReadAllText(journal));
        if (state.Schema != 1 || state.Id.Length != 12 || !state.Id.All(Uri.IsHexDigit) || !Same(state.Installation, installation) || !Same(state.Data, data))
            throw Failure("update_recovery_required", "Update journal ownership or schema is invalid.");
        foreach (var file in state.Files) ValidateManagedName(file.Name);
        return state;
    }

    private InstallationUpdateState Save(InstallationUpdateState state)
    {
        state = state with { UpdatedAt = DateTimeOffset.UtcNow };
        var pending = journal + ".pending";
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, state, RelayJson.Options);
            stream.Flush(flushToDisk: true);
        }
        File.Move(pending, journal, true);
        return state;
    }

    private void DemandStage(InstallationUpdateState state)
    {
        foreach (var file in state.Files.Where(file => file.After is not null))
            if (OptionalHash(Inside(Stage(state), file.Name)) != file.After)
                throw Failure("update_candidate_invalid", "Prepared candidate is missing or changed: " + file.Name);
    }

    private void DemandPublished(InstallationUpdateState state)
    {
        if (state.Files.Any(file => OptionalHash(Inside(installation, file.Name)) != file.After))
            throw Failure("update_conflict", "The complete installed payload does not match the transaction.");
    }

    private void DemandBarrier(InstallationUpdateState state)
    {
        var marker = Path.Combine(data, "promotion.lock");
        if (!File.Exists(marker) || File.ReadAllText(marker) != state.Id)
            throw Failure("update_barrier_required", "The complete-update admission barrier is missing or belongs to another operation.");
    }

    private string Stage(InstallationUpdateState state) => Path.Combine(directory, state.Id, "stage");
    private string Backup(InstallationUpdateState state, string name) => Inside(Path.Combine(directory, state.Id, "before"), name);
    private static string? OptionalHash(string path) => File.Exists(path) ? Hash(path) : null;
    private static string Hash(string path)
    {
        RuntimePaths.DemandPrivatePath(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void Replace(string target, string? source)
    {
        RuntimePaths.DemandPrivatePath(target);
        if (source is null) { File.Delete(target); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var pending = target + ".update-pending";
        using (var input = File.OpenRead(source))
        using (var output = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        { input.CopyTo(output); output.Flush(flushToDisk: true); }
        try { File.Move(pending, target, true); }
        finally { if (File.Exists(pending)) File.Delete(pending); }
    }

    private static void AtomicText(string path, string text)
    {
        RuntimePaths.DemandPrivatePath(path);
        var pending = path + ".update-pending";
        using (var stream = new FileStream(pending, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true))
        { writer.Write(text); writer.Flush(); stream.Flush(true); }
        File.Move(pending, path, true);
    }

    private void ValidateManagedName(string name)
    {
        ValidateName(name);
        var launcherRoots = new[] { "mcp-launcher", "startup-launcher" }
            .Select(launcher => Path.GetRelativePath(installation, Path.Combine(data, launcher)).Replace('\\', '/') + "/");
        if (name.StartsWith("var/", StringComparison.OrdinalIgnoreCase)
            && !launcherRoots.Any(root => name.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            throw Failure("update_scope_invalid", "Application data is not a distribution-owned update file.");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Split('/').Any(part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.')
            || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains('\\'))
            || Path.IsPathRooted(name) || name.StartsWith("local/", StringComparison.OrdinalIgnoreCase))
            throw Failure("update_scope_invalid", "Unsafe update file name.");
    }

    private static string Inside(string root, string name)
    {
        ValidateName(name);
        var path = Path.GetFullPath(name.Replace('/', Path.DirectorySeparatorChar), root);
        if (!Within(path, root)) throw Failure("update_scope_invalid", "Update file escaped its owner.");
        RuntimePaths.DemandPrivatePath(path);
        return path;
    }

    private static string Absolute(string path) => Path.IsPathFullyQualified(path) ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
        : throw new ArgumentException("Update paths must be absolute.");
    private static bool Same(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    private static bool Within(string path, string root) => Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static RelayException Failure(string code, string message) => new(code, message, false);
}
