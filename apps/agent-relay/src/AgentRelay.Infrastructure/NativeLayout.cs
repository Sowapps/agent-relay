using System.Text.Json;
using System.Text.RegularExpressions;

namespace AgentRelay.Infrastructure;

/// <summary>Facade over the packaged native-layout.json shared with Python provisioning and control.</summary>
/// <seealso cref="RuntimePaths.NativeSession"/>
public static class NativeLayout
{
    private static readonly JsonElement definition = Read();
    private static string Base => definition.GetProperty("base").GetString()!;
    private static string Sessions => Base + "/" + definition.GetProperty("sessions").GetString() + "/";
    private static string Profiles => Base + "/" + definition.GetProperty("profiles").GetString() + "/";
    private static string LegacyPrefix => definition.GetProperty("legacyPrefix").GetString()!;
    private static string LegacySessionPrefix => definition.GetProperty("legacySessionPrefix").GetString()!;

    private static JsonElement Read()
    {
        using var stream = typeof(NativeLayout).Assembly.GetManifestResourceStream("AgentRelay.Infrastructure.native-layout.json")
            ?? throw new InvalidDataException("Native layout definition is missing.");
        using var document = JsonDocument.Parse(stream);
        var value = document.RootElement.Clone();
        if (value.GetProperty("version").GetInt32() != 1) throw new InvalidDataException("Unsupported native layout version.");
        return value;
    }

    public static string Session(Guid id, bool legacy = false) => (legacy ? LegacySessionPrefix : Sessions) + id.ToString("N");
    public static bool IsSession(string root, Guid id) => root == Session(id) || root == Session(id, true);
    public static bool IsRoot(string root) => Regex.IsMatch(root, "\\A(?:" + Regex.Escape(Sessions) + "[a-f0-9]{32}|" +
        Regex.Escape(Profiles) + "[a-zA-Z0-9-]+|" + Regex.Escape(LegacyPrefix) + "[a-zA-Z0-9-]+)\\z");
}
