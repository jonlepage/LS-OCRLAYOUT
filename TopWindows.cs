using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ScreenSearchOverlay;

internal sealed record TopWindow(IntPtr Handle, string Title, string Process);

// The other programs' windows, front to back, as the taskbar shows them:
// visible, titled, not a tool palette, not hidden by the system (cloaked
// shells of UWP apps), not ours. For the MCP look_at_screen "window".
internal static unsafe partial class TopWindows
{
    internal static List<TopWindow> List()
    {
        var handles = new List<IntPtr>();
        var state = GCHandle.Alloc(handles);
        try { EnumWindows(&Collect, GCHandle.ToIntPtr(state)); }
        finally { state.Free(); }

        var names = new Dictionary<int, string>();
        foreach (var process in Process.GetProcesses())
            using (process)
                names[process.Id] = process.ProcessName;

        var own = Environment.ProcessId;
        var windows = new List<TopWindow>();
        foreach (var hwnd in handles)
        {
            if (!IsWindowVisible(hwnd)) continue;
            if ((GetWindowLongW(hwnd, GWL_EXSTYLE) & WS_EX_TOOLWINDOW) != 0) continue;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            var title = Title(hwnd);
            if (title.Length == 0) continue;
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == own) continue;
            windows.Add(new TopWindow(hwnd, title, names.GetValueOrDefault((int)pid, "")));
        }
        return windows;
    }

    // By process name first ("blender"), then by a piece of the title: an
    // editor's title may hold "blender" too, and the editor is likely in front.
    internal static TopWindow? Match(List<TopWindow> windows, string query)
    {
        if (query.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            query = query[..^4];
        return windows.FirstOrDefault(w => w.Process.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? windows.FirstOrDefault(w => w.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static string Title(IntPtr hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length == 0) return "";
        var buffer = new char[length + 1];
        fixed (char* text = buffer)
            return new string(text, 0, GetWindowTextW(hwnd, text, buffer.Length));
    }

    [UnmanagedCallersOnly]
    private static int Collect(IntPtr hwnd, IntPtr state)
    {
        ((List<IntPtr>)GCHandle.FromIntPtr(state).Target!).Add(hwnd);
        return 1; // keep going
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int DWMWA_CLOAKED = 14;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr state);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextLengthW(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    private static partial int GetWindowTextW(IntPtr hWnd, char* text, int maxCount);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
