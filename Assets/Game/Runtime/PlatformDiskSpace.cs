using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Game.Runtime
{
    public static class PlatformDiskSpace
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);
#endif
        public static long AvailableBytes(string directory)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // Unity 2022 IL2CPP does not implement DriveInfo.GetDriveFormat, called by its constructor.
            if (!GetDiskFreeSpaceEx(Path.GetFullPath(directory), out ulong available, out _, out _))
                throw new IOException("Cannot read free disk space", new Win32Exception(Marshal.GetLastWin32Error()));
            return checked((long)available);
#else
            throw new PlatformNotSupportedException("Provide a disk-space adapter before building for this platform");
#endif
        }
    }
}
