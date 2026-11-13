using System.Diagnostics;
using System.IO.Pipes;
using System.Text.Json;
using AgentRelay.Protocol;

namespace AgentRelay.Transport;

/// <summary>Prepare/ready/commit ownership transfer, paired with ResumeAsync. No request is replayed after commit.</summary>
public static class McpSuccession
{
    private const int MaximumStateBytes = 2 * McpWireLimits.MaximumFrameBytes;

    /// <seealso cref="ResumeAsync"/>
    public static async Task<bool> TransferAsync(McpProcessImage image, McpTransferState transfer, Func<Task> releaseWorker)
    {
        McpProcessSelections.Validate(image);
        var id = Guid.NewGuid().ToString("N");
        var path = Path.Combine(transfer.StateDirectory, "succession", id + ".json");
        McpPrivateFiles.Demand(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(transfer, McpProcessSelections.Json);
        if (bytes.Length > MaximumStateBytes) throw new IOException("Transport transfer state exceeds its bound.");
        using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { file.Write(bytes); file.Flush(true); }
        var journal = path + ".status";
        File.WriteAllText(journal, "prepared");
        using var fromChild = new AnonymousPipeServerStream(PipeDirection.In);
        using var toChild = new AnonymousPipeServerStream(PipeDirection.Out);
        using var child = WindowsStdioProcess.Start(image.Executable, inherited => image.Arguments.Concat(new[] { "--selection", transfer.Selection,
            "--state-dir", transfer.StateDirectory, "--resume", path, "--control-read", inherited[1].ToString(), "--control-write", inherited[0].ToString() })
                .Concat(transfer.BindingId is { } binding ? new[] { "--connection-id", binding.ToString("D") } : []),
            [fromChild.ClientSafePipeHandle.DangerousGetHandle(), toChild.ClientSafePipeHandle.DangerousGetHandle()]);
        fromChild.DisposeLocalCopyOfClientHandle();
        toChild.DisposeLocalCopyOfClientHandle();
        File.WriteAllText(path + ".candidate", child.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        File.WriteAllText(journal, "awaiting_ready");
        var committed = false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var frames = new McpFrames(fromChild);
            var ready = JsonSerializer.Deserialize<Ready>(await frames.ReadAsync().WaitAsync(timeout.Token) ?? throw new IOException("Successor did not acknowledge readiness."), McpProcessSelections.Json);
            if (ready != new Ready(child.Id, transfer.GatewayIdentity)) throw new IOException("Successor identity mismatch.");
            await releaseWorker();
            File.WriteAllText(journal, "commit_intent");
            // Once commit bytes are attempted, the parent must not resume/replay: receipt can be uncertain.
            committed = true;
            await McpFrames.WriteAsync(toChild, "commit", timeout.Token);
            File.WriteAllText(journal, "committed");
            return true;
        }
        catch (Exception error)
        {
            if (committed)
            {
                try { File.WriteAllText(journal, "commit_unconfirmed"); }
                catch (Exception storage) when (storage is IOException or UnauthorizedAccessException)
                { Console.Error.WriteLine("Agent Relay succession diagnostic persistence failed: " + id); }
                Console.Error.WriteLine("Agent Relay succession commit is unconfirmed: " + id + "; no replay.");
                return true;
            }
            if (!child.HasExited) child.Kill();
            await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            File.WriteAllText(journal, "aborted_before_commit:" + error.GetType().Name);
            return false;
        }
    }

    private sealed record Ready(int ProcessId, string GatewayIdentity);

    /// <seealso cref="TransferAsync"/>
    public static async Task<McpTransferState> ResumeAsync(string selection, string stateDirectory, string path, string controlRead, string controlWrite)
    {
        McpPrivateFiles.Demand(path);
        if (new FileInfo(path).Length > MaximumStateBytes) throw new IOException("Transport transfer state exceeds its bound.");
        var state = JsonSerializer.Deserialize<McpTransferState>(File.ReadAllText(path), McpProcessSelections.Json)
            ?? throw new IOException("Missing transfer state.");
        if (state.Selection != Path.GetFullPath(selection) || state.StateDirectory != Path.GetFullPath(stateDirectory) ||
            !Path.GetFullPath(path).StartsWith(Path.Combine(state.StateDirectory, "succession") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Transfer state belongs to another transport scope.");
        var selected = McpProcessSelections.Read(selection);
        if (McpProcessSelections.Identity(selected.Gateway) != state.GatewayIdentity ||
            !string.Equals(Path.GetFullPath(selected.Gateway.Executable), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Successor process is not the selected gateway.");
        using var fromParent = new AnonymousPipeClientStream(PipeDirection.In, controlRead);
        using var toParent = new AnonymousPipeClientStream(PipeDirection.Out, controlWrite);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await McpFrames.WriteAsync(toParent, JsonSerializer.Serialize(new Ready(Environment.ProcessId, state.GatewayIdentity), McpProcessSelections.Json), timeout.Token);
        if (await new McpFrames(fromParent).ReadAsync().WaitAsync(timeout.Token) != "commit") throw new IOException("Parent did not commit stream ownership.");
        File.WriteAllText(path + ".active", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return state;
    }
}
