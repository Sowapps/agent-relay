using System.Diagnostics;

namespace AgentRelay.Transport;

/// <summary>Keep the WSL interop process alive while the normal Windows gateway transfers its selected pipe handles.</summary>
internal static class InteropStdio
{
    internal static async Task RunAsync(string executable, IEnumerable<string> arguments, Stream input, Stream output)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var child = Process.Start(start) ?? throw new IOException("The local MCP gateway could not start.");

        async Task ForwardInputAsync()
        {
            try { await input.CopyToAsync(child.StandardInput.BaseStream); }
            finally { child.StandardInput.Close(); }
        }

        var incoming = ForwardInputAsync();
        var outgoing = child.StandardOutput.BaseStream.CopyToAsync(output);
        if (await Task.WhenAny(incoming, outgoing) == incoming) await incoming;
        await outgoing;
        await output.FlushAsync();
        // The first child may already have exited after handing its handles to the selected gateway.
    }
}
