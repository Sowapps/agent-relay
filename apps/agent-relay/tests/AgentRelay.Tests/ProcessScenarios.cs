using System.Diagnostics;
using AgentRelay.Infrastructure;

internal static class ProcessScenarios
{
    public static async Task FixtureAsync(string mode)
    {
        Console.InputEncoding = new System.Text.UTF8Encoding(false);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        if (mode == "reject")
        {
            Console.Error.Write("early-refusal-É");
            Environment.ExitCode = 7;
            return;
        }
        if (mode == "echo") { Console.Write(await Console.In.ReadToEndAsync()); return; }
        Console.Write("partial-output");
        Console.Error.Write("partial-error");
        Console.Out.Flush();
        Console.Error.Flush();
        await Task.Delay(TimeSpan.FromSeconds(10));
    }

    public static async Task RunAsync()
    {
        var arguments = new[] { typeof(ProcessScenarios).Assembly.Location, "process-fixture", "echo" };
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("The test host executable is unavailable.");
        var normal = await LocalProcess.RunAsync(executable, arguments, "Échange 😀", TimeSpan.FromSeconds(5), CancellationToken.None);
        if (normal.ExitCode != 0 || normal.Output != "Échange 😀") throw new Exception("Process UTF-8 round trip failed.");
        arguments[^1] = "reject";
        var rejected = await LocalProcess.RunAsync(executable, arguments, new string('x', 2_000_000), TimeSpan.FromSeconds(5), CancellationToken.None);
        if (rejected.ExitCode != 7 || rejected.Error != "early-refusal-É" || rejected.InputCompleted)
            throw new Exception("Early process refusal lost its diagnostic or claimed input acceptance.");
        arguments[^1] = "wait";
        foreach (var input in new string?[] { null, new string('x', 2_000_000) })
        {
            var watch = Stopwatch.StartNew();
            try
            {
                await LocalProcess.RunAsync(executable, arguments, input, TimeSpan.FromSeconds(1), CancellationToken.None);
                throw new Exception("Process timeout was not enforced.");
            }
            catch (LocalProcessTimeoutException error)
            {
                if (!error.Output.Contains("partial-output", StringComparison.Ordinal) ||
                    !error.Error.Contains("partial-error", StringComparison.Ordinal) ||
                    error.InputCompleted != (input is null) || watch.Elapsed > TimeSpan.FromSeconds(6))
                    throw new Exception("Deadline lost output, misreported input or exceeded bounded cleanup.");
            }
        }
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        try
        {
            await LocalProcess.RunAsync(executable, arguments, null, TimeSpan.FromSeconds(5), cancel.Token);
            throw new Exception("Caller cancellation was ignored.");
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        Console.WriteLine("LOCAL_PROCESS=PASS (UTF-8, bounded stdin, partial timeout diagnostics, caller cancellation)");
    }
}
