using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AgentRelay.Windows;

internal sealed record StartupPreferenceState(bool? Enabled, string? Error = null);

internal interface IStartupPreference
{
    StartupPreferenceState Read();
    StartupPreferenceState Set(bool enabled);
}

internal sealed class WindowsStartupPreference(string data) : IStartupPreference
{
    public StartupPreferenceState Read()
    {
        try
        {
            var enabled = StartupRegistration.Enabled(data);
            if (enabled)
            {
                try { StartupRegistration.ValidateInstalledLauncher(data); _ = StartupRegistration.ResolveRuntime(data); }
                catch (Exception error) when (error is IOException or JsonException or KeyNotFoundException or UnauthorizedAccessException)
                { return new(true, "Le démarrage est enregistré, mais son installation doit être réparée. " + error.Message); }
            }
            return new(enabled);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return new(null, "Impossible de vérifier le démarrage automatique. " + error.Message); }
    }
    public StartupPreferenceState Set(bool enabled)
    {
        StartupRegistration.Set(data, enabled);
        var confirmed = Read();
        if (confirmed.Enabled != enabled) throw new IOException("Le changement du démarrage automatique n’a pas pu être confirmé.");
        return confirmed;
    }
}

internal static class StartupRegistration
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal static string Name(string data) => "AgentRelay-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)).ToUpperInvariant())))[..12];
    private static string Launcher(string data) => Path.Combine(Path.GetFullPath(data), "startup-launcher", "AgentRelay.Windows.exe");
    private static string Quote(string value) => '"' + value + new string('\\', value.Length - value.TrimEnd('\\').Length) + '"';
    internal static string Command(string data) => Quote(Launcher(data)) + " startup";
    public static bool Enabled(string data)
    { using var key = Registry.CurrentUser.OpenSubKey(RegistryPath); return key?.GetValue(Name(data)) is string; }
    internal static void ValidateInstalledLauncher(string data)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        if (key?.GetValue(Name(data)) is string command && command == Command(data) && !File.Exists(Launcher(data)))
            throw new IOException("Le lanceur de démarrage conservé est introuvable.");
    }
    public static void Set(string data, bool enabled)
    {
        if (!enabled) { using var existing = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: true); existing?.DeleteValue(Name(data), throwOnMissingValue: false); }
        else
        {
            if (Command(data).Length > 260) throw new IOException("Le chemin du dossier de données est trop long pour le démarrage Windows. Choisissez un emplacement plus court.");
            _ = ResolveRuntime(data);
            PrepareLauncher(data);
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key.SetValue(Name(data), Command(data), RegistryValueKind.String);
        }
        if (Enabled(data) != enabled) throw new IOException("Le changement du démarrage automatique n’a pas pu être confirmé.");
    }

    internal static ProcessStartInfo ResolveRuntime(string data)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(data, "mcp-runtime.json")));
        var pointer = document.RootElement;
        static string FilePath(JsonElement value, string key)
        {
            var path = value.GetProperty(key).GetString();
            if (path is null || !Path.IsPathFullyQualified(path) || !File.Exists(path))
                throw new IOException("Le lanceur du relais est introuvable. Préparez à nouveau l’installation avant d’activer le démarrage automatique.");
            return path;
        }
        var start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (pointer.TryGetProperty("executable", out _)) start.FileName = FilePath(pointer, "executable");
        else { start.FileName = FilePath(pointer, "dotnet"); start.ArgumentList.Add(FilePath(pointer, "assembly")); }
        if (Path.GetFullPath(start.FileName).Equals(Launcher(data), StringComparison.OrdinalIgnoreCase)) throw new IOException("Le lanceur de démarrage ne peut pas se désigner lui-même.");
        start.ArgumentList.Add("tray"); start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(Path.GetFullPath(data));
        return start;
    }

    internal static void Launch(string data, int? lifetimeMilliseconds = null)
    {
        try
        {
            var start = ResolveRuntime(data);
            if (lifetimeMilliseconds is { } limit) { start.ArgumentList.Add("--lifetime-ms"); start.ArgumentList.Add(limit.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
            using var process = Process.Start(start) ?? throw new IOException("Le relais n’a pas pu démarrer.");
        }
        catch (Exception error)
        {
            Directory.CreateDirectory(data);
            File.AppendAllText(Path.Combine(data, "startup.log"), $"{DateTimeOffset.UtcNow:o} {error.Message}\n");
            throw;
        }
    }

    private static void PrepareLauncher(string data)
    {
        var destination = Path.GetDirectoryName(Launcher(data))!;
        if (File.Exists(Path.Combine(destination, "startup-launcher.ready")) && File.Exists(Launcher(data))) return;
        if (Directory.Exists(destination)) throw new IOException("Le lanceur de démarrage local est incomplet. Faites réparer l’installation avant d’activer cette option.");
        var source = AppContext.BaseDirectory;
        if (!File.Exists(Path.Combine(source, "hostfxr.dll")) || !File.Exists(Path.Combine(source, "AgentRelay.Windows.exe")))
            throw new IOException("Activez cette option depuis une installation autonome d’Agent Relay. La version de développement seule ne fournit pas le lanceur Windows nécessaire.");
        Directory.CreateDirectory(data);
        var stage = Path.Combine(Path.GetFullPath(data), "startup-launcher.pending-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            // Keep the native bootstrap independent of versioned payload cleanup. Hard links avoid
            // duplicate runtime bytes on the same volume; other filesystems fall back to copying.
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(source, file);
                if (relative.Split(Path.DirectorySeparatorChar)[0] is "documentation" or "presentation" or "wsl" or "tools") continue;
                if (Path.GetExtension(file).ToLowerInvariant() is not (".dll" or ".exe" or ".json")) continue;
                var target = Path.Combine(stage, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(Path.Combine(source, "runtime.json")) || !CreateHardLink(target, file, IntPtr.Zero)) File.Copy(file, target);
            }
            File.WriteAllText(Path.Combine(stage, "startup-launcher.ready"), "Native startup bootstrap; retain while startup is enabled.\n");
            Directory.Move(stage, destination);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true); }
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string newFileName, string existingFileName, IntPtr securityAttributes);
}
