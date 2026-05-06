using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopCapsules.Services;

public sealed class IconCache : IDisposable
{
    private readonly ConcurrentDictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ImageSource _fallbackIcon;

    public IconCache()
    {
        _fallbackIcon = CreateFallbackIcon();
    }

    public ImageSource FallbackIcon => _fallbackIcon;

    public ImageSource GetIcon(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return _fallbackIcon;
        }

        return _cache.GetOrAdd(path, ExtractIcon);
    }

    public Task<ImageSource> GetIconAsync(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? Task.FromResult(_fallbackIcon)
            : Task.Run(() => GetIcon(path));
    }

    public void Dispose()
    {
        _cache.Clear();
    }

    private ImageSource ExtractIcon(string path)
    {
        if (TryResolveExplicitIcon(path, out var explicitIconPath, out var explicitIconIndex) &&
            TryExtractResourceIcon(explicitIconPath, explicitIconIndex, out var explicitIcon))
        {
            return explicitIcon;
        }

        if (TryResolveTargetPath(path, out var targetPath) &&
            !string.Equals(path, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveExplicitIcon(targetPath, out var targetIconPath, out var targetIconIndex) &&
                TryExtractResourceIcon(targetIconPath, targetIconIndex, out var targetExplicitIcon))
            {
                return targetExplicitIcon;
            }

            var targetIcon = ExtractShellIcon(targetPath);
            if (targetIcon is not null)
            {
                return targetIcon;
            }
        }

        return ExtractShellIcon(path) ?? _fallbackIcon;
    }

    private ImageSource? ExtractShellIcon(string path)
    {
        var attributes = Directory.Exists(path) ? FileAttributeDirectory : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiLargeIcon;

        if (!File.Exists(path) && !Directory.Exists(path))
        {
            flags |= ShgfiUseFileAttributes;
        }

        var result = SHGetFileInfo(path, attributes, out var info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
        if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                info.hIcon,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));

            bitmap.Freeze();
            return bitmap;
        }
        finally
        {
            DestroyIcon(info.hIcon);
        }
    }

    private static bool TryResolveTargetPath(string path, out string targetPath)
    {
        targetPath = "";

        if (ShellLinkService.TryReadShortcut(path, out var shortcut) &&
            !string.IsNullOrWhiteSpace(shortcut.TargetPath))
        {
            targetPath = ExpandPath(shortcut.TargetPath, Path.GetDirectoryName(path));
            return File.Exists(targetPath) || Directory.Exists(targetPath);
        }

        return false;
    }

    private static bool TryResolveExplicitIcon(string path, out string iconPath, out int iconIndex)
    {
        iconPath = "";
        iconIndex = 0;

        if (ShellLinkService.TryReadShortcut(path, out var shortcut))
        {
            if (!string.IsNullOrWhiteSpace(shortcut.IconPath))
            {
                iconPath = ExpandPath(shortcut.IconPath, shortcut.WorkingDirectory);
                iconIndex = shortcut.IconIndex;
                return File.Exists(iconPath);
            }

            if (!string.IsNullOrWhiteSpace(shortcut.TargetPath))
            {
                path = ExpandPath(shortcut.TargetPath, shortcut.WorkingDirectory);
            }
        }

        if (string.Equals(Path.GetExtension(path), ".url", StringComparison.OrdinalIgnoreCase))
        {
            return TryReadInternetShortcutIcon(path, out iconPath, out iconIndex);
        }

        return false;
    }

    private static bool TryReadInternetShortcutIcon(string path, out string iconPath, out int iconIndex)
    {
        iconPath = "";
        iconIndex = 0;

        if (!File.Exists(path))
        {
            return false;
        }

        foreach (var line in File.ReadLines(path))
        {
            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();

            if (string.Equals(key, "IconFile", StringComparison.OrdinalIgnoreCase))
            {
                iconPath = ExpandPath(value, Path.GetDirectoryName(path));
            }
            else if (string.Equals(key, "IconIndex", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(value, out var parsedIndex))
            {
                iconIndex = parsedIndex;
            }
        }

        return File.Exists(iconPath);
    }

    private static bool TryExtractResourceIcon(string iconPath, int iconIndex, out ImageSource image)
    {
        image = null!;

        if (!File.Exists(iconPath))
        {
            return false;
        }

        var largeIcons = new[] { IntPtr.Zero };
        var smallIcons = new[] { IntPtr.Zero };

        try
        {
            var extracted = ExtractIconEx(iconPath, iconIndex, largeIcons, smallIcons, 1);
            var handle = largeIcons[0] != IntPtr.Zero ? largeIcons[0] : smallIcons[0];
            if (extracted == 0 || handle == IntPtr.Zero)
            {
                return false;
            }

            var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));

            bitmap.Freeze();
            image = bitmap;
            return true;
        }
        finally
        {
            if (largeIcons[0] != IntPtr.Zero)
            {
                DestroyIcon(largeIcons[0]);
            }

            if (smallIcons[0] != IntPtr.Zero)
            {
                DestroyIcon(smallIcons[0]);
            }
        }
    }

    private static string ExpandPath(string path, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

        if (!Path.IsPathRooted(path) && !string.IsNullOrWhiteSpace(baseDirectory))
        {
            path = Path.Combine(baseDirectory, path);
        }

        return path;
    }

    private static ImageSource CreateFallbackIcon()
    {
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var rect = new Rect(5, 4, 34, 40);
            context.DrawRoundedRectangle(
                new SolidColorBrush(Color.FromRgb(235, 240, 246)),
                new Pen(new SolidColorBrush(Color.FromRgb(76, 89, 104)), 2),
                rect,
                4,
                4);

            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(119, 133, 149)), 2), new Point(12, 18), new Point(31, 18));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(119, 133, 149)), 2), new Point(12, 26), new Point(31, 26));
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(119, 133, 149)), 2), new Point(12, 34), new Point(25, 34));
        }

        var bitmap = new RenderTargetBitmap(48, 48, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiUseFileAttributes = 0x000000010;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        out SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(
        string lpszFile,
        int nIconIndex,
        [Out] IntPtr[] phiconLarge,
        [Out] IntPtr[] phiconSmall,
        uint nIcons);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }
}
