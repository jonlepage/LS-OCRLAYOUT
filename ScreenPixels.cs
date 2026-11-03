using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenSearchOverlay;

// The overlays work in the screen's own pixels — captures, GetWindowRect,
// the mouse hook — while WPF places a window in DIPs scaled by its monitor,
// and monitors can each have their own scale (a 4K TV at 175 % beside a
// screen at 100 %). These place a window in pixels, on any monitor.
internal static partial class ScreenPixels
{
    // Over that rectangle of the screen, before it first shows. Landing on a
    // monitor of another scale, WPF resizes the window for it
    // (WM_DPICHANGED, sent to hidden windows too): the second pass sets the
    // size again, on that monitor's scale now.
    internal static void Cover(Window window, ScreenInfo area) =>
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            for (var i = 0; i < 2; i++)
                SetWindowPos(hwnd, IntPtr.Zero, area.X, area.Y, area.Width, area.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        };

    // Its top-left corner at that screen point, its size kept
    internal static void MoveTo(Window window, double x, double y)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, IntPtr.Zero, (int)Math.Round(x), (int)Math.Round(y), 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    // Where it is on the screen, in pixels
    internal static Rect Bounds(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && GetWindowRect(hwnd, out var r)
            ? new Rect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top)
            : Rect.Empty;
    }

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
