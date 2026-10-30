using System.Diagnostics;
using System.Text;

namespace AgentRelay.Infrastructure;

public sealed record ProcessOutput(int ExitCode, string Output, string Error);

public static class LocalProcess
{
    public static async Task<ProcessOutput> RunAsync(string executable, IEnumerable<string> arguments, string? input,
        TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("The local process could not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(timeout);
        try
        {
            var output = process.StandardOutput.ReadToEndAsync(deadline.Token);
            var error = process.StandardError.ReadToEndAsync(deadline.Token);
            if (input is not null) await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output, await error);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { throw new TimeoutException($"The local process exceeded its {timeout.TotalSeconds:0}s wait limit; completion is not confirmed."); }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
