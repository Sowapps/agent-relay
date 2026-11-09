using System.Text.Json;
using AgentRelay.Infrastructure;

namespace AgentRelay.Windows;

internal sealed record StartupTransferResult(bool PreviousEnabled, bool CurrentEnabled, bool Transferred);

/// <summary>Moves a reviewed sign-in preference once when the stable data scope changes.</summary>
/// <seealso cref="WindowsStartupPreference"/>
internal static class StartupPreferenceTransfer
{
    public static StartupTransferResult Run(string current, string previous, bool apply)
    {
        var source = RelayLocation.Normalize(previous);
        var target = RelayLocation.Normalize(current);
        if (string.Equals(source, target, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select two distinct initialized relay scopes.");
        foreach (var scope in new[] { source, target })
        {
            var pointer = Path.Combine(scope, "mcp-runtime.json");
            RuntimePaths.DemandPrivatePath(pointer);
            if (!File.Exists(pointer))
                throw new IOException("Only a reviewed relay scope can participate in startup transfer.");
            StartupRegistration.DemandOwnedEntry(scope);
        }
        _ = StartupRegistration.ResolveRuntime(target);
        return Run(new WindowsStartupPreference(source), new WindowsStartupPreference(target), apply);
    }

    internal static StartupTransferResult Run(IStartupPreference source, IStartupPreference target, bool apply)
    {
        var sourceEnabled = ReadEnabled(source);
        var targetEnabled = ReadEnabled(target);
        if (!sourceEnabled || !apply) return new(sourceEnabled, targetEnabled, false);

        try
        {
            if (!targetEnabled) SetVerified(target, true);
            if (target.Read() is not { Enabled: true, Error: null })
                throw new IOException("The stable startup entry could not be verified. The previous entry is retained.");
            SetVerified(source, false);
            return new(false, true, true);
        }
        catch
        {
            // Restore the source first. If that cannot be verified, retain the available target.
            try
            {
                if (source.Read().Enabled != true) SetVerified(source, true);
                if (!targetEnabled && target.Read().Enabled != false) SetVerified(target, false);
            }
            catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException)
            {
                throw new IOException("Startup transfer could not be recovered automatically. Inspect both relay startup entries before retrying.", rollback);
            }
            throw;
        }
    }

    private static bool ReadEnabled(IStartupPreference preference) =>
        preference.Read().Enabled ?? throw new IOException("The startup preference could not be read.");

    private static void SetVerified(IStartupPreference preference, bool enabled)
    {
        var state = preference.Set(enabled);
        if (state.Enabled != enabled || state.Error is not null)
            throw new IOException("The requested startup preference could not be verified.");
    }
}
