using System.Runtime.InteropServices;

namespace OnlineVideoManager.Core.Downloads;

public static partial class DiskSpace
{
    public const long DefaultReserve = 2L * 1024 * 1024 * 1024;
    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpaceEx(string directory, out ulong available, out ulong total, out ulong free);

    public static long Available(string directory)
    {
        var path = Path.GetFullPath(directory);
        while (!Directory.Exists(path)) path = Path.GetDirectoryName(path) ?? throw new IOException("Cannot find the output drive.");
        if (OperatingSystem.IsWindows())
        {
            if (!GetDiskFreeSpaceEx(path, out var available, out _, out _))
                throw new IOException("Cannot check free disk space.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            return checked((long)available);
        }
        return new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace;
    }

    public static void Ensure(string directory, long reserve = DefaultReserve) => Validate(Available(directory), reserve);

    public static void Validate(long available, long reserve)
    {
        if (available < reserve)
            throw new IOException($"Low disk space: {available / 1073741824d:F1} GB available; OVM keeps {reserve / 1073741824d:F1} GB free. Free some space or choose another output folder, then retry.");
    }
}
