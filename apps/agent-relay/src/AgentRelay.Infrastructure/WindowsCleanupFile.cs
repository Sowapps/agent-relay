using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace AgentRelay.Infrastructure;

/// <summary>Pin and validate one ordinary file. Deletion applies to the checked handle, never to a later pathname lookup.</summary>
internal sealed class WindowsCleanupFile : IDisposable
{
    private readonly SafeFileHandle handle;
    internal record Stamp(uint Volume, ulong Id, long Bytes, DateTimeOffset Created, DateTimeOffset Modified);
    internal Stamp Identity { get; }

    public WindowsCleanupFile(string path, bool writable = false)
    {
        RuntimePaths.DemandPrivatePath(path);
        handle = CreateFile(path, 0x80000000u | (writable ? 0x10000u : 0), 0, IntPtr.Zero, 3, 0x00200000u, IntPtr.Zero);
        try
        {
            if (handle.IsInvalid || !GetFileInformationByHandle(handle, out var details)) throw LastError();
            if ((details.Attributes & (uint)(FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 || details.Links != 1)
                throw new IOException("Fichier lié ou non ordinaire.");
            var target = new StringBuilder(32768);
            var length = GetFinalPathNameByHandle(handle, target, (uint)target.Capacity, 0);
            if (length == 0 || length >= target.Capacity ||
                !string.Equals(target.ToString(), @"\\?\" + Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                throw new IOException("L’emplacement du fichier a changé.");
            Identity = new(details.Volume, Join(details.IndexHigh, details.IndexLow), checked((long)Join(details.SizeHigh, details.SizeLow)),
                FromFileTime(details.Created), FromFileTime(details.Modified));
        }
        catch { handle.Dispose(); throw; }
    }

    public FileStream Read()
    {
        var stream = new FileStream(new SafeFileHandle(handle.DangerousGetHandle(), ownsHandle: false), FileAccess.Read);
        stream.Position = 0;
        return stream;
    }

    public void Delete()
    {
        var request = new Disposition { DeleteFile = 1 };
        if (!SetFileInformationByHandle(handle, 4, ref request, 1)) throw LastError();
    }

    public void Dispose() => handle.Dispose();

    private static Win32Exception LastError() => new(Marshal.GetLastWin32Error());
    private static ulong Join(uint high, uint low) => ((ulong)high << 32) | low;
    private static DateTimeOffset FromFileTime(FILETIME value) => DateTimeOffset.FromFileTime(checked((long)Join(value.High, value.Low))).ToUniversalTime();

    [StructLayout(LayoutKind.Sequential)] private struct FILETIME { public uint Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct FileInfoByHandle
    {
        public uint Attributes;
        public FILETIME Created, Accessed, Modified;
        public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Disposition { public byte DeleteFile; }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInfoByHandle information);
    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder name, uint length, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, ref Disposition information, uint length);
}
