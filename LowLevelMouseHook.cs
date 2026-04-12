using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScreenSearchOverlay;

// Global low-level mouse hook (WH_MOUSE_LL). Detects wheel events system-wide
// — even when our overlay is WS_EX_TRANSPARENT and never receives the wheel
// itself — so we can keep the debounce timer alive during a scroll session.
//
// The callback fires on the thread that installed the hook, so the installer
// must have a message loop. WPF's UI thread satisfies this.
internal sealed class LowLevelMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;

    public event Action? WheelDetected;

    private IntPtr _hookId = IntPtr.Zero;
    private HookProc? _proc;

    public void Install()
    {
        // Keep the delegate rooted — without this it gets GC'd and the native
        // callback bombs the next time Windows tries to invoke it.
        _proc = Callback;
        _hookId = SetWindowsHookEx(WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
        if (_hookId == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL)
        {
            try { WheelDetected?.Invoke(); }
            catch { /* never block the input thread */ }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _proc = null;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
