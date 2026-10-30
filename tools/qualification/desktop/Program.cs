using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

internal static class Program
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Notify(uint action, ref IconData data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size;
        public IntPtr Window;
        public uint Id, Flags, Callback;
        public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Title;
        public uint InfoFlags;
        public Guid Guid;
        public IntPtr BalloonIcon;
    }

    [STAThread]
    private static int Main()
    {
        var window = new NativeWindow();
        using var context = new ApplicationContext();
        using var timer = new System.Windows.Forms.Timer { Interval = 300 };
        window.CreateHandle(new CreateParams { Caption = "Relay toolchain qualification", Parent = new IntPtr(-3) });
        var data = new IconData
        {
            Size = (uint)Marshal.SizeOf<IconData>(), Window = window.Handle, Id = 1,
            Flags = 7, Callback = 0x8001, Icon = SystemIcons.Application.Handle,
            Tip = "Agent Relay qualification", Info = "", Title = ""
        };
        bool added = false, removed = false, pumped = false;
        try
        {
            added = Notify(0, ref data);
            if (added)
            {
                timer.Tick += (_, _) => { pumped = true; timer.Stop(); context.ExitThread(); };
                timer.Start();
                Application.Run(context);
            }
        }
        finally
        {
            if (added) removed = Notify(2, ref data);
            window.DestroyHandle();
        }
        Console.WriteLine(JsonSerializer.Serialize(new { DesktopPumpPassed = pumped, TrayAddPassed = added, TrayRemovePassed = removed }));
        return pumped && added && removed ? 0 : 1;
    }
}
