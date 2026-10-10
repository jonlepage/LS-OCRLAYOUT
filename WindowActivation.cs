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

    private const byte VK_MENU = 0x12;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);
}
