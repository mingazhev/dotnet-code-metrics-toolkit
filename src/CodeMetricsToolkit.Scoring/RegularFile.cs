using System.Runtime.InteropServices;

namespace CodeMetricsToolkit.Scoring;

internal static class RegularFile
{
    private const int S_Ifmt = 0xF000;
    private const int S_Ififo = 0x1000;
    private const int S_Ifchr = 0x2000;
    private const int S_Ifdir = 0x4000;
    private const int S_Ifblk = 0x6000;
    private const int S_Ifreg = 0x8000;
    private const int S_Iflnk = 0xA000;
    private const int S_Ifsock = 0xC000;

    public static bool IsRegularFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileAttributes attributes;

        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
        {
            return false;
        }

        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        return UnixLStatIsRegularFile(path);
    }

    private static bool UnixLStatIsRegularFile(string path)
    {
        var buffer = Marshal.AllocHGlobal(256);
        try
        {
            if (LStat(path, buffer) != 0)
            {
                return false;
            }

            var mode = ReadUnixMode(buffer);
            return (mode & S_Ifmt) == S_Ifreg;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int ReadUnixMode(IntPtr buffer)
    {
        if (OperatingSystem.IsMacOS() ||
            OperatingSystem.IsMacCatalyst() ||
            OperatingSystem.IsIOS() ||
            OperatingSystem.IsTvOS())
        {
            return Marshal.ReadInt16(buffer, 4) & 0xFFFF;
        }

        if (OperatingSystem.IsLinux())
        {
            var x64Mode = Marshal.ReadInt32(buffer, 24);
            if (IsKnownFileType(x64Mode))
            {
                return x64Mode;
            }

            var armMode = Marshal.ReadInt32(buffer, 16);
            if (IsKnownFileType(armMode))
            {
                return armMode;
            }
        }

        return Marshal.ReadInt16(buffer, 4) & 0xFFFF;
    }

    private static bool IsKnownFileType(int mode)
    {
        return (mode & S_Ifmt) is S_Ififo or S_Ifchr or S_Ifdir or S_Ifblk or S_Ifreg or S_Iflnk or S_Ifsock;
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "lstat", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern int LStat(string path, IntPtr buffer);
}
