using System.Globalization;

namespace AgentRelay.Windows;

// Human-facing dates only; protocol timestamps and original diagnostic payloads retain their wire format.
internal static class DisplayDateTime
{
    internal static string Date(DateTimeOffset at) => at.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    internal static string Time(DateTimeOffset at, bool seconds = false) => at.ToLocalTime().ToString(seconds ? "HH:mm:ss" : "HH:mm", CultureInfo.InvariantCulture);
    internal static string Format(DateTimeOffset at, bool seconds = false) => Date(at) + " " + Time(at, seconds);
}
