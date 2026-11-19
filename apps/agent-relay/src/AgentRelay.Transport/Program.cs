using AgentRelay.Protocol;

namespace AgentRelay.Transport;

internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Use the qualified Windows transport adapter.");
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            var hostLaunch = false; var show = false; var wslStdio = false; var locateWslEntry = false;
            for (var index = 0; index < arguments.Length; index++)
            {
                if (arguments[index] == "--launch-host" && !hostLaunch) { hostLaunch = true; continue; }
                if (arguments[index] == "--show" && !show) { show = true; continue; }
                if (arguments[index] == "--wsl-stdio" && !wslStdio) { wslStdio = true; continue; }
                if (arguments[index] == "--locate-wsl-entry" && !locateWslEntry) { locateWslEntry = true; continue; }
                if (index + 1 >= arguments.Length || arguments[index] is not ("--selection" or "--state-dir" or "--connection-id" or "--resume" or "--control-read" or "--control-write") ||
                    !values.TryAdd(arguments[index], arguments[++index])) throw new ArgumentException("Unknown, duplicate or incomplete transport option.");
            }
            var selection = values["--selection"];
            var state = values["--state-dir"];
            Guid? binding = values.TryGetValue("--connection-id", out var identity) ? Guid.Parse(identity) : null;
            if (!Path.IsPathFullyQualified(selection) || !Path.IsPathFullyQualified(state)) throw new ArgumentException("Transport paths must be absolute.");
            if (show && !hostLaunch) throw new ArgumentException("Show requires host launch.");
            if (wslStdio || locateWslEntry)
            {
                if (hostLaunch || binding is null || values.Count != 3 || (wslStdio && locateWslEntry))
                    throw new ArgumentException("WSL stdio requires an explicit participant binding and a normal MCP launch.");
                if (locateWslEntry)
                {
                    var selected = McpProcessSelections.Read(selection).Gateway;
                    McpProcessSelections.Validate(selected);
                    Console.WriteLine(selected.Executable);
                    return 0;
                }
                await InteropStdio.RunAsync(Environment.ProcessPath ?? throw new IOException("Cannot identify the MCP entry."),
                    ["--selection", selection, "--state-dir", state, "--connection-id", binding.Value.ToString("D")],
                    Console.OpenStandardInput(), Console.OpenStandardOutput());
                return 0;
            }
            if (hostLaunch)
            {
                if (values.ContainsKey("--resume") || binding is not null) throw new ArgumentException("Host launch cannot carry an MCP binding or transfer state.");
                var host = McpProcessSelections.Read(selection).Host ?? throw new IOException("Selected installation has no declared host launch.");
                using var process = WindowsStdioProcess.StartDetached(host.Executable, show ? host.Arguments.Concat(["--show"]) : host.Arguments);
                return 0;
            }
            McpTransferState? transfer = null;
            if (values.TryGetValue("--resume", out var resume)) transfer = await McpSuccession.ResumeAsync(selection, state, resume, values["--control-read"], values["--control-write"]);
            else
            {
                if (values.ContainsKey("--control-read") || values.ContainsKey("--control-write")) throw new ArgumentException("Control requires an explicit transfer state.");
                var selected = McpProcessSelections.Read(selection);
                // Capture loaded generation before the first blocking input read.
                transfer = new(selection, state, McpProcessSelections.Identity(selected.Gateway), null, null, [], Guid.NewGuid(), binding);
                if (!string.Equals(Path.GetFullPath(selected.Gateway.Executable), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (!await McpSuccession.TransferAsync(selected.Gateway, transfer, () => Task.CompletedTask)) throw new IOException("Selected gateway did not acknowledge cold-start succession.");
                    return 0;
                }
            }
            if (transfer.BindingId != binding) throw new IOException("Successor binding does not match the transferred identity.");
            McpGateway? gateway = null;
            var input = new WindowsPipeInput(Console.OpenStandardInput(), () => gateway!.PollAsync());
            await using var owned = gateway = new McpGateway(selection, state, input, Console.OpenStandardOutput(), transfer);
            await gateway.RunAsync();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Agent Relay transport could not start: " + error.GetType().Name + ": " + error.Message);
            Console.Error.WriteLine(error.StackTrace);
            return 1;
        }
    }
}
