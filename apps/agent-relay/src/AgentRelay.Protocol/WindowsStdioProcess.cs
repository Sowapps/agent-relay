using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace AgentRelay.Protocol;

/// <summary>Pass only duplicated standard handles; the successor must await the succession commit.</summary>
public static class WindowsStdioProcess
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public string? Reserved, Desktop, Title;
        public int X, Y, XSize, YSize, XChars, YChars, Fill, Flags;
        public short Show, ReservedBytes;
        public IntPtr ReservedPointer, Input, Output, Error;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx { public StartupInfo Info; public IntPtr Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo { public IntPtr Process, Thread; public int ProcessId, ThreadId; }

    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool DuplicateHandle(IntPtr process, IntPtr source, IntPtr target, out IntPtr copy, uint access, bool inherit, uint options);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool InitializeProcThreadAttributeList(IntPtr attributes, int count, int flags, ref IntPtr size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool UpdateProcThreadAttribute(IntPtr attributes, uint flags, IntPtr attribute, IntPtr value, IntPtr size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")] private static extern void DeleteProcThreadAttributeList(IntPtr attributes);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inherit, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);

    /// <summary>Launch independently of the caller's redirected MCP/CLI channels.</summary>
    /// <seealso cref="Start"/>
    public static Process StartDetached(string executable, IEnumerable<string> arguments)
    {
        using var sink = File.OpenHandle(@"\\.\NUL", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var handle = sink.DangerousGetHandle();
        return Start(executable, _ => arguments, [], [handle, handle, handle]);
    }

    public static Process Start(string executable, Func<IReadOnlyList<IntPtr>, IEnumerable<string>> arguments,
        IReadOnlyList<IntPtr> controlHandles, IReadOnlyList<IntPtr>? standardHandles = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows stdio succession requires its Windows adapter.");
        var handles = new IntPtr[3 + controlHandles.Count];
        IntPtr attributes = IntPtr.Zero, list = IntPtr.Zero;
        var initialized = false;
        try
        {
            for (var index = 0; index < handles.Length; index++)
                if (!DuplicateHandle(GetCurrentProcess(), index < 3 ? standardHandles?[index] ?? GetStdHandle(-10 - index) : controlHandles[index - 3], GetCurrentProcess(), out handles[index], 0, true, 2))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            var size = IntPtr.Zero;
            InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
            attributes = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error());
            initialized = true;
            list = Marshal.AllocHGlobal(IntPtr.Size * handles.Length);
            Marshal.Copy(handles, 0, list, handles.Length);
            if (!UpdateProcThreadAttribute(attributes, 0, new IntPtr(0x00020002), list, new IntPtr(IntPtr.Size * handles.Length), IntPtr.Zero, IntPtr.Zero))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var startup = new StartupInfoEx { Attributes = attributes, Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>(),
                Flags = 0x100, Input = handles[0], Output = handles[1], Error = handles[2] } };
            var command = new StringBuilder(string.Join(" ", new[] { executable }.Concat(arguments(handles[3..])).Select(Quote)));
            if (!CreateProcessW(executable, command, IntPtr.Zero, IntPtr.Zero, true, 0x08080000, IntPtr.Zero,
                Path.GetDirectoryName(executable)!, ref startup, out var created)) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { return Process.GetProcessById(created.ProcessId); }
            finally { CloseHandle(created.Thread); CloseHandle(created.Process); }
        }
        finally
        {
            if (initialized) DeleteProcThreadAttributeList(attributes);
            if (attributes != IntPtr.Zero) Marshal.FreeHGlobal(attributes);
            if (list != IntPtr.Zero) Marshal.FreeHGlobal(list);
            foreach (var handle in handles) if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    private static string Quote(string argument)
    {
        var result = new StringBuilder("\"");
        var slashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\') { slashes++; continue; }
            if (character == '"') result.Append('\\', slashes * 2 + 1);
            else result.Append('\\', slashes);
            result.Append(character);
            slashes = 0;
        }
        return result.Append('\\', slashes * 2).Append('"').ToString();
    }
}
