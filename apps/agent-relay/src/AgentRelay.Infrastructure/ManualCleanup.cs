using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using AgentRelay.Protocol;

namespace AgentRelay.Infrastructure;

public sealed record CleanupEntry(string Path, string Category, long Bytes, string? Protection);
public sealed record CleanupPreview(Guid PlanId, DateTimeOffset ObservedAt, DateOnly ProtectedFrom,
    long ObservedBytes, long ProtectedBytes, IReadOnlyList<CleanupEntry> Entries, IReadOnlyList<CleanupEntry> Protected, bool Partial);
public sealed record CleanupOutcome(string Path, string State, string? Reason, long RemovedBytes = 0);
public sealed record CleanupResult(Guid PlanId, string Report, IReadOnlyList<CleanupOutcome> Items, string? Error = null);

/// <summary>Manual maintenance of known files in one data scope. A preview is single-use and never expands on confirmation.</summary>
public sealed class ManualCleanup(string dataDirectory, TimeProvider? clock = null)
{
    private const int VisibleLimit = 100;
    private const int ScanLimit = 2000;
    private const int MaximumFileSize = 2 * 1024 * 1024;
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly string data = Path.GetFullPath(dataDirectory);
    private readonly SemaphoreSlim gate = new(1, 1);
    private Plan? pending;

    private sealed record FileCandidate(string Path, string Category, WindowsCleanupFile.Stamp Identity, string Digest);
    private sealed record Plan(Guid Id, IReadOnlyList<FileCandidate> Files);

    public async Task<CleanupPreview> AnalyzeAsync(bool logs, bool diagnostics, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { return Analyze(logs, diagnostics, token); }
        finally { gate.Release(); }
    }

    private CleanupPreview Analyze(bool logs, bool diagnostics, CancellationToken token)
    {
        pending = null;
        RuntimePaths.DemandActive(data);
        var found = new List<FileCandidate>();
        var excluded = new List<CleanupEntry>();
        var now = time.GetUtcNow();
        var protectedFrom = ProtectedFrom(now);
        var partial = false;
        long observed = 0;
        long protectedBytes = 0;
        foreach (var owner in new[] { (Enabled: logs, Category: "logs", Root: Path.Combine(data, "logs", "mcp"), Pattern: "*.log"),
            (Enabled: diagnostics, Category: "diagnostics", Root: McpTransportPresences.DirectoryFor(RuntimePaths.TransportStateDirectory(data)), Pattern: "*.json") })
        {
            if (!owner.Enabled) continue;
            RuntimePaths.DemandPrivatePath(owner.Root);
            if (!Directory.Exists(owner.Root)) continue;
            foreach (var path in Directory.EnumerateFiles(owner.Root, owner.Pattern).Take(ScanLimit + 1).Select((file,index) => (file,index)))
            {
                token.ThrowIfCancellationRequested();
                if (path.index == ScanLimit) { partial = true; break; }
                var relative = Path.GetRelativePath(data, path.file);
                long bytes = 0;
                try
                {
                    using var file = new WindowsCleanupFile(path.file);
                    bytes = file.Identity.Bytes;
                    observed += bytes;
                    var reason = Protection(path.file, owner.Category, file, now, protectedFrom);
                    if (reason is not null) Protect(relative, owner.Category, bytes, reason);
                    else if (found.Count == VisibleLimit) partial = true;
                    else found.Add(new(relative, owner.Category, file.Identity, Digest(file)));
                }
                catch (Exception error) when (IsFileError(error))
                { Protect(relative, owner.Category, bytes, "Fichier utilisé ou invérifiable. " + error.Message); }
            }
        }
        var plan = new Plan(Guid.NewGuid(), found);
        pending = plan;
        return new(plan.Id, TimeZoneInfo.ConvertTime(now, time.LocalTimeZone), protectedFrom, observed, protectedBytes,
            found.Select(item => new CleanupEntry(item.Path, item.Category, item.Identity.Bytes, null)).ToArray(), excluded, partial);

        void Protect(string path, string category, long bytes, string reason)
        {
            protectedBytes += bytes;
            if (excluded.Count == VisibleLimit) { partial = true; return; }
            excluded.Add(new(path, category, bytes, reason));
        }
    }

    public async Task<CleanupResult> ApplyAsync(Guid planId, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            if (pending is not { } plan || plan.Id != planId || plan.Files.Count == 0)
                throw new InvalidOperationException("Cet aperçu a expiré. Analysez de nouveau la sélection.");
            pending = null;
            return Apply(plan);
        }
        finally { gate.Release(); }
    }

    private CleanupResult Apply(Plan plan)
    {
        RuntimePaths.DemandActive(data);
        var directory = Path.Combine(data, "logs", "maintenance");
        var reportPath = Path.Combine(directory, "cleanup-" + plan.Id.ToString("N") + ".jsonl");
        var lockPath = Path.Combine(directory, "cleanup.lock");
        RuntimePaths.DemandPrivatePath(lockPath);
        RuntimePaths.DemandPrivatePath(reportPath);
        Directory.CreateDirectory(directory);
        RuntimePaths.DemandPrivatePath(reportPath);
        using var lease = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
        using var record = new StreamWriter(new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        record.WriteLine(RelayJson.Serialize(new { At = time.GetUtcNow(), plan.Id, Paths = plan.Files.Select(item => item.Path) }));
        var promotion = new McpDeploymentAdmission(data);
        var outcomes = new List<CleanupOutcome>();
        foreach (var entry in plan.Files)
        {
            CleanupOutcome outcome;
            try
            {
                using var admission = promotion.Acquire();
                promotion.DemandOpen();
                RuntimePaths.DemandActive(data);
                outcome = CheckAndRemove(entry);
            }
            catch (McpDeploymentException) { outcome = new(entry.Path, "skipped", "Mise à jour en cours."); }
            catch (Win32Exception error) when (error.NativeErrorCode is 2 or 3 or 32 or 33)
            { outcome = new(entry.Path, "skipped", "Le fichier a disparu ou est en cours d’utilisation."); }
            catch (Exception error) when (IsFileError(error)) { outcome = new(entry.Path, "failed", error.Message); }
            outcomes.Add(outcome);
            try { record.WriteLine(RelayJson.Serialize(outcome)); }
            catch (IOException error) { return new(plan.Id, Path.GetRelativePath(data, reportPath), outcomes, "Rapport interrompu : " + error.Message); }
        }
        return new(plan.Id, Path.GetRelativePath(data, reportPath), outcomes);
    }

    private CleanupOutcome CheckAndRemove(FileCandidate entry)
    {
        var path = Path.Combine(data, entry.Path);
        using var file = new WindowsCleanupFile(path, writable: true);
        var now = time.GetUtcNow();
        var reason = Protection(path, entry.Category, file, now, ProtectedFrom(now));
        if (reason is not null) return new(entry.Path, "skipped", reason);
        if (file.Identity != entry.Identity || Digest(file) != entry.Digest)
            return new(entry.Path, "skipped", "Le fichier a changé depuis l’analyse.");
        file.Delete();
        return new(entry.Path, "removed", null, entry.Identity.Bytes);
    }

    private DateOnly ProtectedFrom(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, time.LocalTimeZone);
        return DateOnly.FromDateTime(local.DateTime).AddDays(local.Hour < 2 ? -1 : 0);
    }

    private string? Protection(string path, string category, WindowsCleanupFile file, DateTimeOffset now, DateOnly cutoff)
    {
        var stamp = file.Identity;
        if (stamp.Bytes > MaximumFileSize) return "Taille supérieure au périmètre de cette opération.";
        var meaningful = new List<DateTimeOffset> { stamp.Created, stamp.Modified };
        if (category == "logs")
        {
            if (!DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(path), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ||
                day.Year < 1970) return "Journal sans date d’activité vérifiable.";
            var partitionStart = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            if (stamp.Modified < partitionStart || partitionStart > now) return "La date du journal est incohérente.";
        }
        else
        {
            if (stamp.Bytes > 16384) return "Trace MCP trop grande.";
            using var stream = file.Read();
            var lease = JsonSerializer.Deserialize<McpTransportPresence>(stream, RelayJson.Options);
            if (lease is not { State: "closed", Activity: "closed" } ||
                Path.GetFileNameWithoutExtension(path) != lease.InstanceId.ToString("N") ||
                File.Exists(Path.ChangeExtension(path, ".close"))) return "La fermeture de la connexion reste à vérifier.";
            meaningful.Add(lease.SeenAt);
        }
        if (meaningful.Any(at => at.Year < 1970 || at > now)) return "Date d’activité invérifiable.";
        return meaningful.Any(at => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, time.LocalTimeZone).DateTime) >= cutoff)
            ? "Journée protégée." : null;
    }

    private static string Digest(WindowsCleanupFile file)
    {
        using var stream = file.Read();
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static bool IsFileError(Exception error) => error is IOException or UnauthorizedAccessException or Win32Exception or JsonException or ArgumentException;
}
