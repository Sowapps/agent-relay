using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AgentRelay.Infrastructure;

public static class RelayLocation
{
    public static string Normalize(string dataDirectory) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(dataDirectory));

    public static FileStream OpenHostOwnership(string dataDirectory) => OpenExclusive(dataDirectory, "host.lock");

    internal static FileStream OpenExclusive(string dataDirectory, string name) =>
        new(Path.Combine(dataDirectory, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    internal static bool IsSharingConflict(IOException error) => (error.HResult & 0xffff) is 32 or 33;

    public static string PipeName(string dataDirectory)
    {
        var identity = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("Windows account identity is unavailable.");
        var key = Encoding.UTF8.GetBytes(identity + "|" + Normalize(dataDirectory).ToUpperInvariant());
        return "agent-relay-" + Convert.ToHexString(SHA256.HashData(key))[..32];
    }
}

