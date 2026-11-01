using System.Runtime.InteropServices;
using System.Text;
using AgentRelay.Core;

namespace AgentRelay.Infrastructure;

internal static class FilePublication
{
    internal static void DemandSameVolume(string stagingDirectory, string destination)
    {
        static string Existing(string path)
        {
            var current = Path.GetFullPath(path);
            while (!Directory.Exists(current) && !File.Exists(current)) current = Path.GetDirectoryName(current) ?? throw new IOException("Storage volume unavailable.");
            return current;
        }
        static string Volume(string path)
        {
            var mount = new StringBuilder(32768); var volume = new StringBuilder(1024);
            if (!GetVolumePathName(Existing(path), mount, mount.Capacity) || !GetVolumeName(mount.ToString(), volume, volume.Capacity))
                throw new RelayException("publication_unavailable", "The destination filesystem cannot be qualified for atomic publication from private var staging.", effectsPossible: false);
            return volume.ToString();
        }
        if (!Volume(stagingDirectory).Equals(Volume(destination), StringComparison.OrdinalIgnoreCase))
            throw new RelayException("cross_volume_publication", "Private var staging and the destination must share a filesystem for atomic publication.", effectsPossible: false);
    }

    [DllImport("kernel32.dll", EntryPoint="GetVolumePathNameW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathName(string file, StringBuilder volume, int length);
    [DllImport("kernel32.dll", EntryPoint="GetVolumeNameForVolumeMountPointW", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeName(string mount, StringBuilder volume, int length);
}
