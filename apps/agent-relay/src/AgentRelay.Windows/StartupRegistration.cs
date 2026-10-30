using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AgentRelay.Windows;

internal static class StartupRegistration
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private static string Name(string data) => "AgentRelay-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(data).ToUpperInvariant())))[..12];
    public static bool Enabled(string data)
    { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue(Name(data)) is string; }
    public static void Set(string data, bool enabled)
    {
        if (!enabled) { using var existing = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true); existing?.DeleteValue(Name(data), throwOnMissingValue: false); return; }
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "mcp-runtime.json")));
        var pointer = document.RootElement;
        if (pointer.TryGetProperty("distribution", out var distribution) && distribution.GetString() == "portable-win-x64")
            throw new InvalidOperationException("Cette archive portable ne configure pas le démarrage automatique. Utilisez Start-AgentRelay.cmd.");
        var python = pointer.GetProperty("python").GetString()!; var launcher = pointer.GetProperty("trayLauncher").GetString()!;
        if (!File.Exists(python) || !File.Exists(launcher)) throw new InvalidOperationException("Préparez le runtime et son lanceur stable avant d’activer cette option.");
        static string Quote(string value) => '"' + value.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';
        var command = Quote(python) + " -B " + Quote(launcher) + " --data-dir " + Quote(Path.GetFullPath(data));
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath); key.SetValue(Name(data), command, RegistryValueKind.String);
    }
}
