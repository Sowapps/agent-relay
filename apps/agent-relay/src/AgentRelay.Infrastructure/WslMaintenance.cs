using System.Diagnostics;
using System.Text;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record WslMaintenanceRequest(string Distribution, string User, string Script, string? Input, int TimeoutSeconds);
public sealed record WslMaintenanceResult(int? ExitCode, string Output, string Error, bool TimedOut,
    string Phase, bool InputCompleted, double Seconds, bool? EffectsPossible);

/// <summary>Run one explicit local maintenance script, preserving the launch/operation boundary without replay.</summary>
/// <seealso cref="LocalProcess"/>
public static class WslMaintenance
{
    public static async Task<WslMaintenanceResult> RunAsync(WslMaintenanceRequest request, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(request.Distribution) || string.IsNullOrWhiteSpace(request.User) ||
            string.IsNullOrWhiteSpace(request.Script) || request.TimeoutSeconds is < 1 or > 300)
            throw new RelayException("invalid_request", "Explicit WSL distribution, user, script and a 1–300 second deadline are required.", false);
        var marker = "relay-maintenance-started-" + Guid.NewGuid().ToString("N");
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Script));
        var wrapper = $"import sys,base64;print('{marker}',file=sys.stderr,flush=True);exec(compile(base64.b64decode('{encoded}'),'<relay-maintenance>','exec'))";
        var watch = Stopwatch.StartNew();
        try
        {
            var result = await LocalProcess.RunAsync("wsl.exe", ["-d", request.Distribution, "-u", request.User,
                "--exec", "/usr/bin/python3", "-u", "-c", wrapper], request.Input, TimeSpan.FromSeconds(request.TimeoutSeconds), token);
            var started = result.Error.StartsWith(marker, StringComparison.Ordinal);
            return new(result.ExitCode, result.ExitCode == 0 ? result.Output : LocalProcess.DiagnosticTail(result.Output),
                LocalProcess.DiagnosticTail(RemoveMarker(result.Error, marker)), false,
                started ? "native_operation" : "native_start_unconfirmed", true, watch.Elapsed.TotalSeconds,
                result.ExitCode == 0 ? true : null);
        }
        catch (LocalProcessTimeoutException error)
        {
            return new(null, error.Output, RemoveMarker(error.Error, marker), true,
                error.ErrorPrefix.StartsWith(marker, StringComparison.Ordinal) ? "native_operation" : "native_start_unconfirmed",
                error.InputCompleted, error.Seconds, true);
        }
    }

    private static string RemoveMarker(string error, string marker) => error.Replace(marker + "\n", "", StringComparison.Ordinal);
}
