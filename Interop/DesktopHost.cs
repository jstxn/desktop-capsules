using System.Runtime.InteropServices;

namespace DesktopCapsules.Interop;

public static class DesktopHost
{
    public static void PrepareAsDesktopToolWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var exStyle = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(exStyle));
    }

    public static bool TryAttachToDesktop(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero)
        {
            return false;
        }

        SendMessageTimeout(
            progman,
            SpawnWorkerW,
            IntPtr.Zero,
            IntPtr.Zero,
            SendMessageTimeoutFlags.Normal,
            1000,
            out _);

        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        style &= ~WsPopup;
        style |= WsChild;
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr(style));

        SetLastError(0);
        SetParent(hwnd, progman);
        if (Marshal.GetLastWin32Error() != 0)
        {
            return false;
        }

        SetWindowPos(
            hwnd,
            HwndTop,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow | SwpFrameChanged);

        return true;
    }

    private const int GwlStyle = -16;
    private const int GwlExStyle = -20;
    private const long WsChild = 0x40000000L;
    private const long WsPopup = 0x80000000L;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExAppWindow = 0x00040000L;
    private const uint SpawnWorkerW = 0x052C;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpFrameChanged = 0x0020;
    private static readonly IntPtr HwndTop = IntPtr.Zero;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void SetLastError(uint dwErrCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        uint msg,
        IntPtr wParam,
        IntPtr lParam,
        SendMessageTimeoutFlags fuFlags,
        uint uTimeout,
        out IntPtr lpdwResult);

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : GetWindowLongPtr32(hwnd, index);
    }

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr newLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, newLong) : SetWindowLongPtr32(hwnd, index, newLong);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [Flags]
    private enum SendMessageTimeoutFlags : uint
    {
        Normal = 0x0000
    }
}
