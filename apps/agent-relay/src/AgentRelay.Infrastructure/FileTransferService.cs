using System.Security.Cryptography;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record TransferReceipt(Guid Id, Guid SessionId, TransferRequest Request, WorkResult Result, ArtifactTransfer? Artifact = null, OperationScope? Scope = null);

public sealed class FileTransferService(SessionService sessions, WslBridge bridge, RelayDatabase database, SqliteWorkStore? progressStore = null, TransferLimits? configuredLimits = null, string? protectedDataDirectory = null)
{
    public const long MaximumBytes = 256L * 1024 * 1024;
    private readonly SqliteDocuments<TransferReceipt> receipts = new(database, "transfers");
    private readonly TransferLimits limits = configuredLimits ?? new();

    private void ValidateProtectedPath(string path)
    {
        foreach (var protectedRoot in new[] { protectedDataDirectory, AppContext.BaseDirectory }.Where(value => value is not null))
        {
            var root = Path.GetFullPath(protectedRoot!).TrimEnd(Path.DirectorySeparatorChar);
            if (path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new RelayException("capability_denied", "Application installation/data files cannot be exposed through a project file transfer.", effectsPossible: false);
        }
    }

    public static string WorkspacePath(string workspace, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Length > 500 || Path.IsPathRooted(relative) ||
            relative.Split('/', '\\').Any(part => part.Length == 0 || part is "." or ".." || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new RelayException("invalid_path", "Use a relative file path inside the session workspace.");
        var root = Path.GetFullPath(workspace);
        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (!target.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new RelayException("invalid_path", "The path escaped the session workspace.");
        for (var current = new DirectoryInfo(root); current is not null; current = current.Parent)
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new RelayException("invalid_path", "Workspace ancestors cannot be symbolic links/reparse points.");
        var path = root;
        foreach (var part in Path.GetRelativePath(root, target).Split(Path.DirectorySeparatorChar))
        {
            path = Path.Combine(path, part);
            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new RelayException("invalid_path", "Transfer paths cannot contain symbolic links/reparse points."); }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return target;
    }

    public async Task<WorkResult?> ReceiptAsync(WorkRequest request, CancellationToken token)
    {
        var receipt = await receipts.FindAsync(request.Id, token);
        return receipt is not null && receipt.SessionId == request.SessionId && receipt.Scope == request.Scope &&
            (request.Artifact is not null ? receipt.Artifact == request.Artifact : receipt.Request == request.Transfer) ? receipt.Result : null;
    }

    public async Task<TransferPartial> PartialAsync(WorkRequest request, string direction, string destination, string localWorkspace, WslProfile profile, bool remove, CancellationToken token)
    {
        if (direction == "upload")
        {
            var response = await bridge.CallAsync(profile, "transfer_partial", new { request.Id, Direction = direction, LinuxPath = destination, Remove = remove }, token);
            return new(request.Id, destination, response.GetProperty("bytes").GetInt64(), response.GetProperty("exists").GetBoolean(), response.GetProperty("removed").GetBoolean());
        }
        var target = WorkspacePath(localWorkspace, destination);
        ValidateProtectedPath(target);
        var relative = Path.GetRelativePath(localWorkspace, Path.Combine(Path.GetDirectoryName(target)!, ".relay-transfer-" + request.Id.ToString("D") + ".part"));
        var partial = WorkspacePath(localWorkspace, relative); var info = new FileInfo(partial);
        var exists = info.Exists; var bytes = exists ? info.Length : 0;
        if (remove && exists) File.Delete(partial);
        return new(request.Id, destination, bytes, exists, remove && exists);
    }

    public async Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token, WslProfile? selectedProfile = null, string? localWorkspace = null, bool uploadAuthorized = false)
    {
        var session = await sessions.FindAsync(request.SessionId!.Value, token);
        var profile = selectedProfile ?? await sessions.ResolveAsync(session.Id, request.ProfileId, token);
        var transfer = request.Transfer!;
        if (transfer.Direction is not ("upload" or "download")) throw new RelayException("invalid_work", "Expected upload or download.");
        if (transfer.Direction == "upload" && !profile.AllowLocalWork && !uploadAuthorized) throw new RelayException("capability_denied", "Uploading requires explicit file-write authorization.");
        var windowsWorkspace = localWorkspace ?? session.WindowsWorkspace;
        if (windowsWorkspace is null) throw new RelayException("unavailable", "This session has no assigned Windows workspace.");
        if (!Directory.Exists(windowsWorkspace)) throw new RelayException("unavailable", "The declared local workspace must already exist; it is not created implicitly.", effectsPossible: false);
        if (request.Scope is not null)
        {
            var identity = await LocalProcess.RunAsync("wsl.exe", ["-d", profile.Distribution, "--exec", "id", "-un"], null, TimeSpan.FromSeconds(20), token);
            if (identity.ExitCode != 0 || identity.Output.Trim() != profile.User)
                throw new RelayException("capability_denied", "The WSL UNC filesystem identity differs from the configured endpoint user. Select a matching endpoint or a qualified helper-owned transport.");
        }
        var localRelative = transfer.Direction == "upload" ? transfer.SourcePath : transfer.DestinationPath;
        var linuxRelative = transfer.Direction == "upload" ? transfer.DestinationPath : transfer.SourcePath;
        var windowsPath = WorkspacePath(windowsWorkspace, localRelative);
        if (request.Scope is not null)
        {
            ValidateProtectedPath(windowsPath);
        }
        if (await ReceiptAsync(request, token) is { } retained) return retained;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(limits.WaitSeconds)); token = deadline.Token;
        var native = await bridge.CallAsync(profile, "transfer_inspect", new { request.Id, transfer.Direction, LinuxPath = linuxRelative, transfer.Overwrite, limits.MaximumBytes }, token, TimeSpan.FromSeconds(limits.WaitSeconds));
        var linuxPath = native.GetProperty("path").GetString()!;
        if (native.GetProperty("workspace").GetString() != AgentControl.Context(profile).Workspace)
            throw new InvalidDataException("Transfer workspace identity was not confirmed.");
        string Unc(string linux) => @"\\wsl.localhost\" + profile.Distribution + linux.Replace('/', '\\');
        var upload = transfer.Direction == "upload";
        var source = upload ? windowsPath : Unc(linuxPath);
        var destination = upload ? Unc(native.GetProperty("temporary").GetString()!) :
            Path.Combine(Path.GetDirectoryName(windowsPath)!, ".relay-transfer-" + request.Id.ToString("D") + ".part");
        if (!upload)
        {
            WorkspacePath(windowsWorkspace, localRelative);
            if (!transfer.Overwrite && File.Exists(windowsPath)) throw new RelayException("destination_exists", "Destination exists; overwrite must be explicit.");
            Directory.CreateDirectory(Path.GetDirectoryName(windowsPath)!);
            WorkspacePath(windowsWorkspace, Path.GetRelativePath(windowsWorkspace, destination));
        }
        long bytes = 0; string sha256;
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (input.Length > limits.MaximumBytes) throw new RelayException("file_too_large", "The file exceeds the configured transfer limit.", effectsPossible: false);
            if (progressStore is not null) await progressStore.ProgressAsync(request.Id, new(0, input.Length, "copying", DateTimeOffset.UtcNow), token);
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[1024 * 1024]; int count;
            var update = System.Diagnostics.Stopwatch.StartNew(); var total = input.Length;
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            {
                bytes += count;
                if (bytes > limits.MaximumBytes) throw new RelayException("file_too_large", "The growing source exceeds the configured transfer limit.", effectsPossible: true);
                await output.WriteAsync(buffer.AsMemory(0, count), token); hash.AppendData(buffer, 0, count);
                if (progressStore is not null && update.ElapsedMilliseconds >= 500)
                { await progressStore.ProgressAsync(request.Id, new(bytes, total, "copying", DateTimeOffset.UtcNow), token); update.Restart(); }
            }
            await output.FlushAsync(token); output.Flush(flushToDisk: true);
            sha256 = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (progressStore is not null) await progressStore.ProgressAsync(request.Id, new(bytes, bytes, "verifying", DateTimeOffset.UtcNow), token);
        }
        if (upload)
        {
            var receipt = await bridge.CallAsync(profile, "transfer_commit", new { request.Id, transfer.Direction, LinuxPath = linuxRelative, transfer.Overwrite, Bytes = bytes, Sha256 = sha256, limits.MaximumBytes }, token, TimeSpan.FromSeconds(limits.WaitSeconds));
            if (receipt.GetProperty("id").GetGuid() != request.Id || receipt.GetProperty("path").GetString() != linuxPath ||
                receipt.GetProperty("sha256").GetString() != sha256 || receipt.GetProperty("bytes").GetInt64() != bytes)
                throw new InvalidDataException("The Linux transfer receipt did not match.");
        }
        else
        {
            if (native.GetProperty("bytes").GetInt64() != bytes || native.GetProperty("sha256").GetString() != sha256)
                throw new InvalidDataException("The downloaded file changed or failed its SHA-256 check.");
            WorkspacePath(windowsWorkspace, localRelative);
            File.Move(destination, windowsPath, transfer.Overwrite);
        }
        var result = new WorkResult("file-transfer", transfer.Direction + " completed", Details: new Dictionary<string, string>
        { ["windowsPath"] = windowsPath, ["linuxPath"] = linuxPath, ["bytes"] = bytes.ToString(System.Globalization.CultureInfo.InvariantCulture), ["sha256"] = sha256 });
        await receipts.SaveAsync(request.Id, new(request.Id, session.Id, transfer, result, request.Artifact, request.Scope), CancellationToken.None);
        return result;
    }
}

public sealed class RelayWorkExecutor(WslWorkExecutor work, FileTransferService files) : IWorkExecutor
{
    public Task<WorkResult> ExecuteAsync(WorkRequest request, CancellationToken token) => request.Kind == "transfer"
        ? files.ExecuteAsync(request, token) : work.ExecuteAsync(request, token);
}
