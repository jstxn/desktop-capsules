using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace DesktopCapsules.Services;

public static class ShellLinkService
{
    public static bool TryReadShortcut(string linkPath, out ShellLinkInfo info)
    {
        info = new ShellLinkInfo();

        if (!File.Exists(linkPath) || !string.Equals(Path.GetExtension(linkPath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        object shellLink = new ShellLinkComObject();
        try
        {
            ((IPersistFile)shellLink).Load(linkPath, 0);

            var link = (IShellLinkW)shellLink;
            var target = new StringBuilder(260);
            var iconPath = new StringBuilder(260);
            var arguments = new StringBuilder(2048);
            var workingDirectory = new StringBuilder(260);

            link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
            link.GetIconLocation(iconPath, iconPath.Capacity, out var iconIndex);
            link.GetArguments(arguments, arguments.Capacity);
            link.GetWorkingDirectory(workingDirectory, workingDirectory.Capacity);

            info = new ShellLinkInfo
            {
                TargetPath = target.ToString(),
                IconPath = iconPath.ToString(),
                IconIndex = iconIndex,
                Arguments = arguments.ToString(),
                WorkingDirectory = workingDirectory.ToString()
            };

            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (Marshal.IsComObject(shellLink))
            {
                Marshal.ReleaseComObject(shellLink);
            }
        }
    }

    public static void CreateShortcut(string linkPath, string targetPath, string? description = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);

        object shellLink = new ShellLinkComObject();
        try
        {
            var link = (IShellLinkW)shellLink;
            link.SetPath(targetPath);

            if (File.Exists(targetPath))
            {
                var workingDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrWhiteSpace(workingDirectory))
                {
                    link.SetWorkingDirectory(workingDirectory);
                }
            }

            if (!string.IsNullOrWhiteSpace(description))
            {
                link.SetDescription(description);
            }

            ((IPersistFile)shellLink).Save(linkPath, true);
        }
        finally
        {
            if (Marshal.IsComObject(shellLink))
            {
                Marshal.ReleaseComObject(shellLink);
            }
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLinkComObject
    {
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
            int cchMaxPath,
            IntPtr pfd,
            uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName,
            int cchMaxName);

        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);

        void GetWorkingDirectory(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir,
            int cchMaxPath);

        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);

        void GetArguments(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs,
            int cchMaxPath);

        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation(
            [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
            int cchIconPath,
            out int piIcon);

        void SetIconLocation(
            [MarshalAs(UnmanagedType.LPWStr)] string pszIconPath,
            int iIcon);

        void SetRelativePath(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPathRel,
            uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}

public sealed class ShellLinkInfo
{
    public string TargetPath { get; init; } = "";

    public string IconPath { get; init; } = "";

    public int IconIndex { get; init; }

    public string Arguments { get; init; } = "";

    public string WorkingDirectory { get; init; } = "";
}
