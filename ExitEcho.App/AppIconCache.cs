using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ExitEcho.App;

internal static class AppIconCache
{
    private const int MaxEntries = 64;
    private static readonly Dictionary<string, BitmapSource?> Entries = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Queue<string> InsertionOrder = new();

    public static BitmapSource? Get(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return null;

        try
        {
            var path = Path.GetFullPath(executablePath);
            if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
                return null;
            var drive = new DriveInfo(Path.GetPathRoot(path)!);
            if (drive.DriveType == DriveType.Network)
                return null;

            lock (Entries)
            {
                if (Entries.TryGetValue(path, out var cached))
                    return cached;
            }

            var icon = Extract(path);
            lock (Entries)
            {
                if (Entries.TryGetValue(path, out var cached))
                    return cached;
                if (Entries.Count == MaxEntries)
                    Entries.Remove(InsertionOrder.Dequeue());
                Entries.Add(path, icon);
                InsertionOrder.Enqueue(path);
            }
            return icon;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private static BitmapSource? Extract(string path)
    {
        if (!File.Exists(path))
            return null;
        var count = PrivateExtractIcons(path, 0, 64, 64, out var handle, out _, 1, 0);
        if (count == 0 || count == uint.MaxValue || handle == IntPtr.Zero)
            return null;
        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint PrivateExtractIcons(string file, int index, int width, int height,
        out IntPtr icon, out uint iconId, uint iconCount, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
