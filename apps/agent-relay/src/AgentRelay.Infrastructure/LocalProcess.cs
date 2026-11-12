using System.Diagnostics;
using System.Text;

namespace AgentRelay.Infrastructure;

public sealed record ProcessOutput(int ExitCode, string Output, string Error, bool InputCompleted = true);

public sealed class LocalProcessTimeoutException(string output, string error, bool inputCompleted, double seconds)
    : TimeoutException("The local process exceeded its wait limit; completion is not confirmed.")
{
    public string Output { get; } = LocalProcess.DiagnosticTail(output);
    public string Error { get; } = LocalProcess.DiagnosticTail(error);
    public string ErrorPrefix { get; } = error[..Math.Min(error.Length, 256)];
    public bool InputCompleted { get; } = inputCompleted;
    public double Seconds { get; } = seconds;
}

public static class LocalProcess
{
    internal static string DiagnosticTail(string value) => value.Length > 1800 ? value[^1800..] : value;

    public static async Task<ProcessOutput> RunAsync(string executable, IEnumerable<string> arguments, string? input,
        TimeSpan timeout, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        var watch = Stopwatch.StartNew();
        using var process = Process.Start(start) ?? throw new IOException("The local process could not start.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        var remaining = timeout - watch.Elapsed;
        deadline.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
        using var captureStop = new CancellationTokenSource();
        var output = new Capture();
        var error = new Capture();
        var readers = Task.WhenAll(output.ReadAsync(process.StandardOutput.BaseStream, captureStop.Token),
            error.ReadAsync(process.StandardError.BaseStream, captureStop.Token));
        var inputCompleted = false;
        try
        {
            IOException? inputFailure = null;
            if (input is not null)
            {
                try { await process.StandardInput.BaseStream.WriteAsync(Encoding.UTF8.GetBytes(input), deadline.Token); }
                catch (IOException failure) { inputFailure = failure; }
            }
            process.StandardInput.Close();
            inputCompleted = inputFailure is null;
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            await readers.WaitAsync(deadline.Token);
            if (inputFailure is not null && process.ExitCode == 0)
                throw new IOException("The process closed its input before accepting the request. Completion is not confirmed. " + DiagnosticTail(error.Text), inputFailure);
            return new(process.ExitCode, output.Text, error.Text, inputCompleted);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            StopOwned(process);
            try { await readers.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception captureError) when (captureError is TimeoutException or IOException) { }
            throw new LocalProcessTimeoutException(output.Text, error.Text,
                inputCompleted, watch.Elapsed.TotalSeconds);
        }
        finally
        {
            StopOwned(process);
            captureStop.Cancel();
            try { await readers; }
            catch (Exception captureError) when (captureError is OperationCanceledException or IOException) { }
        }
    }

    private static void StopOwned(Process process)
    {
        try { if (!process.HasExited) process.Kill(true); }
        catch (InvalidOperationException) when (process.HasExited) { }
    }

    private sealed class Capture
    {
        private readonly MemoryStream bytes = new();

        public string Text
        {
            get
            {
                byte[] value;
                lock (bytes) value = bytes.ToArray();
                if (value.Length >= 2 && (value[0] == 0xff && value[1] == 0xfe || value[1] == 0))
                    return Encoding.Unicode.GetString(value).TrimStart('\ufeff');
                try { return new UTF8Encoding(false, true).GetString(value).TrimStart('\ufeff'); }
                catch (DecoderFallbackException) { return Encoding.Latin1.GetString(value); }
            }
        }

        public async Task ReadAsync(Stream stream, CancellationToken token)
        {
            var buffer = new byte[4096];
            int length;
            while ((length = await stream.ReadAsync(buffer, token)) != 0)
            {
                lock (bytes) bytes.Write(buffer, 0, length);
            }
        }
    }
}
