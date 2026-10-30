using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

public sealed record ConnectionBinding(Guid Id, Guid EndpointId, Guid[] ProjectIds, string Digest, bool Administrator = false, bool Revoked = false);
public sealed record AuthenticatedConnection(CallerIdentity Caller, bool Administrator);

public sealed class ConnectionStore(string dataDirectory)
{
    private readonly string directory = Path.Combine(dataDirectory, "connections");
    private readonly SqliteDocuments<ConnectionBinding> bindings = new(new RelayDatabase(dataDirectory), "connections");
    private static readonly Guid AdministratorId = Guid.Parse("28c10e25-b802-42da-92f5-98723e6b54ae");

    public void EnsureAdministrator()
    {
        if (!File.Exists(Path.Combine(directory, AdministratorId.ToString("N") + ".bin")))
            CreateAsync(AdministratorId, Guid.Empty, [], administrator: true, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task<ConnectionBinding> CreateAsync(Guid id, Guid endpointId, Guid[] projects, bool administrator, CancellationToken token)
    {
        if (id == Guid.Empty || (!administrator && (endpointId == Guid.Empty || projects.Length == 0))) throw new RelayException("invalid_configuration", "A scoped connection needs a participant and projects.");
        if (await bindings.FindAsync(id, token) is not null) throw new RelayException("id_conflict", "Connection already exists; use explicit rotation/revocation.");
        Directory.CreateDirectory(directory);
        var secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
        var binding = new ConnectionBinding(id, endpointId, projects.Distinct().ToArray(), Hash(secret), administrator);
        var file = Path.Combine(directory, id.ToString("N") + ".bin");
        // A crash may leave an unbound protected file; it never grants access on its own.
        await File.WriteAllBytesAsync(file, ProtectedSecret.Protect(Encoding.UTF8.GetBytes(secret)), token);
        await bindings.SaveAsync(id, binding, token); return binding;
    }

    public string Secret(Guid? connectionId)
    {
        var id = connectionId ?? AdministratorId;
        var path = Path.Combine(directory, id.ToString("N") + ".bin");
        if (!File.Exists(path)) throw new RelayException("authentication_required", "This MCP connection has no scoped credential. Configure its endpoint/project binding.");
        return Encoding.UTF8.GetString(ProtectedSecret.Unprotect(File.ReadAllBytes(path)));
    }

    public async Task<AuthenticatedConnection> AuthenticateAsync(string? secret, CancellationToken token)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length != 64) throw new RelayException("authentication_required", "A scoped connection credential is required.");
        var digest = Encoding.ASCII.GetBytes(Hash(secret));
        foreach (var binding in await bindings.ListAsync(token))
            if (!binding.Revoked && CryptographicOperations.FixedTimeEquals(digest, Encoding.ASCII.GetBytes(binding.Digest)))
                return new(new(binding.EndpointId, binding.ProjectIds), binding.Administrator);
        throw new RelayException("authentication_required", "The connection credential is invalid or revoked.");
    }

    public async Task RevokeAsync(Guid id, CancellationToken token)
    {
        var binding = await bindings.FindAsync(id, token) ?? throw new RelayException("not_found", "Unknown connection.");
        if (binding.Administrator) throw new RelayException("capability_denied", "Administrative connection requires a separate maintenance rotation.");
        await bindings.SaveAsync(id, binding with { Revoked = true }, token);
    }
    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

public static class ProtectedSecret
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Protect(byte[] value) => Transform(value, true);
    public static byte[] Unprotect(byte[] value) => Transform(value, false);
    private static byte[] Transform(byte[] value, bool protect)
    {
        var input = new Blob { Length = value.Length, Data = Marshal.AllocHGlobal(value.Length) }; Blob output = default;
        try
        {
            Marshal.Copy(value, 0, input.Data, value.Length);
            var success = protect ? CryptProtectData(ref input, "Agent Relay scoped connection", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output) :
                CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), "The current Windows account could not protect/unprotect the relay credential.");
            var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, output.Length); return result;
        }
        finally
        {
            for (var index = 0; index < input.Length; index++) Marshal.WriteByte(input.Data, index, 0);
            Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data);
        }
    }
}
