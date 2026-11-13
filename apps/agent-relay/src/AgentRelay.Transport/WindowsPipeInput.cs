using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AgentRelay.Transport;

/// <summary>Poll only the owned pipe: no blocked read is cancelled or shared with another reader.</summary>
public sealed class WindowsPipeInput(Stream input, Func<Task> poll) : Stream
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int id);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(IntPtr pipe, IntPtr buffer, uint size, IntPtr read, out uint available, IntPtr left);

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            await poll();
            if (!PeekNamedPipe(GetStdHandle(-10), IntPtr.Zero, 0, IntPtr.Zero, out var available, IntPtr.Zero))
            {
                var error = Marshal.GetLastWin32Error();
                if (error is 109 or 232) return 0;
                throw new Win32Exception(error);
            }
            if (available > 0) return await input.ReadAsync(buffer[..(int)Math.Min(available, (uint)buffer.Length)], cancellationToken);
            await Task.Delay(100, cancellationToken);
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
