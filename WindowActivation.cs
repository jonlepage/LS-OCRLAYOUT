using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenSearchOverlay;

// Activate() alone is refused when another program has the foreground —
// after the snipping overlay, or a click in ChatGPT's page: the taskbar
// button flashes and the window stays behind. Windows lifts that lock
// while Alt is held, the usual way out: when a first try is refused,
// hold Alt for the call. The Alt key-up then lands on the window, which
// has no menu for it to open.
internal static partial class WindowActivation
{
    internal static void BringToFront(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        window.Topmost = true;
        window.Topmost = false;
        if (!SetForegroundWindow(hwnd) || GetForegroundWindow() != hwnd)
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        window.Activate();
    }

    // Another program's window (MCP look_at_screen), or our overlay that must
    // stay topmost; restored if minimized. Borrowing the input of the
    // foreground thread first: an Alt key-up landing on another program could
    // open its menu bar. true when the screen changes, so the caller lets it
    // repaint before capturing.
    internal static bool BringToFront(IntPtr hwnd)
    {
        var restored = IsIconic(hwnd) && ShowWindow(hwnd, SW_RESTORE);
        var foreground = GetForegroundWindow();
        if (foreground == hwnd) return restored;

        if (!SetForegroundWindow(hwnd) || GetForegroundWindow() != hwnd)
        {
            var theirs = GetWindowThreadProcessId(foreground, IntPtr.Zero);
            var ours = GetCurrentThreadId();
            if (theirs != 0 && theirs != ours && AttachThreadInput(ours, theirs, true))
            {
                SetForegroundWindow(hwnd);
                AttachThreadInput(ours, theirs, false);
            }
        }
        if (GetForegroundWindow() != hwnd)
        {
            keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
            SetForegroundWindow(hwnd);
            keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        return true;
    }

    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const int SW_RESTORE = 9;

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
}
