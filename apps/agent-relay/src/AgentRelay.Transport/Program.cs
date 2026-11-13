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
            for (var index = 0; index < arguments.Length; index += 2)
                if (index + 1 >= arguments.Length || arguments[index] is not ("--selection" or "--state-dir" or "--resume" or "--control-read" or "--control-write") ||
                    !values.TryAdd(arguments[index], arguments[index + 1])) throw new ArgumentException("Unknown, duplicate or incomplete transport option.");
            var selection = values["--selection"];
            var state = values["--state-dir"];
            if (!Path.IsPathFullyQualified(selection) || !Path.IsPathFullyQualified(state)) throw new ArgumentException("Transport paths must be absolute.");
            McpTransferState? transfer = null;
            if (values.TryGetValue("--resume", out var resume)) transfer = await McpSuccession.ResumeAsync(selection, state, resume, values["--control-read"], values["--control-write"]);
            else
            {
                if (values.ContainsKey("--control-read") || values.ContainsKey("--control-write")) throw new ArgumentException("Control requires an explicit transfer state.");
                var selected = McpProcessSelections.Read(selection);
                if (!string.Equals(Path.GetFullPath(selected.Gateway.Executable), Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                    throw new IOException("This process is not the selected gateway.");
                // Capture loaded generation before the first blocking input read.
                transfer = new(selection, state, McpProcessSelections.Identity(selected.Gateway), null, null, [], Guid.NewGuid());
            }
            await using var gateway = new McpGateway(selection, state, Console.OpenStandardInput(), Console.OpenStandardOutput(), transfer);
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
