using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace ScreenSearchOverlay;

// Global low-level mouse hook (WH_MOUSE_LL) running on its own dedicated
// thread with a private message pump.
//
// MSDN guidance (LowLevelMouseProc): low-level hooks have a system-wide
// timeout (LowLevelHooksTimeout, default 1000 ms on Win10 1709+). If the
// thread that installed the hook fails to dispatch the callback within the
// timeout, Windows SILENTLY removes the hook. Running the pump on the WPF
// UI thread is a latent bug — any UI freeze >1 s permanently disables our
// scroll detection. Moving the pump to a dedicated thread isolates us
// completely.
//
// The callback marshals the event back to the UI thread via the
// SynchronizationContext supplied at construction so subscribers continue
// to run on the dispatcher (state machine fields are UI-thread-only).
internal sealed partial class LowLevelMouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const uint WM_QUIT = 0x0012;

    public event Action? WheelDetected;

    private IntPtr _hookId = IntPtr.Zero;
    private HookProc? _proc;                     // rooted for the hook lifetime
    private Thread? _thread;
    private uint _threadId;
    private readonly ManualResetEventSlim _installed = new(false);
    private Exception? _installError;
    private SynchronizationContext? _syncCtx;

    /// <summary>
    /// Install the hook. Captures the current SynchronizationContext so the
    /// WheelDetected event marshals back to the WPF UI thread. Must be
    /// called from the UI thread (or the thread you want callbacks on).
    /// </summary>
    public void Install()
    {
        _syncCtx = SynchronizationContext.Current;

        _thread = new Thread(ThreadProc)
        {
            IsBackground = true,
            Name = "LLMouseHook",
        };
        _thread.Start();

        // Block until SetWindowsHookEx has actually been called so Install()
        // is synchronous from the caller's perspective.
        _installed.Wait();
        if (_installError != null)
            throw _installError;
    }

    private void ThreadProc()
    {
        try
        {
            _threadId = GetCurrentThreadId();
            _proc = Callback;
            // SetWindowsHookEx wants a function pointer; LibraryImport doesn't
            // marshal delegates directly, so we hand it the FP manually. The
            // delegate _proc stays rooted for the hook's lifetime.
            var fp = Marshal.GetFunctionPointerForDelegate(_proc);
            _hookId = SetWindowsHookEx(WH_MOUSE_LL, fp, IntPtr.Zero, 0);
            if (_hookId == IntPtr.Zero)
            {
                _installError = new Win32Exception(Marshal.GetLastPInvokeError());
                _installed.Set();
                return;
            }
        }
        catch (Exception ex)
        {
            _installError = ex;
            _installed.Set();
            return;
        }

        _installed.Set();

        // Dedicated message pump. WH_MOUSE_LL is dispatched on this thread by
        // Windows — we just need GetMessage to keep the thread alive and let
        // the OS deliver hook callbacks.
        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        // Unhook on the same thread that installed it.
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        _proc = null;
    }

    private IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (int)wParam == WM_MOUSEWHEEL)
        {
            try
            {
                var handler = WheelDetected;
                if (handler != null)
                {
                    if (_syncCtx != null)
                        _syncCtx.Post(static state => ((Action)state!).Invoke(), handler);
                    else
                        handler();
                }
            }
            catch { /* never block the input thread */ }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_thread == null) return;
        try
        {
            if (_threadId != 0)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            _thread.Join(1000);
        }
        catch { }
        _thread = null;
        _installed.Dispose();
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    // ── P/Invoke (LibraryImport for everything; SetWindowsHookEx takes the
    //    function pointer as IntPtr to avoid the delegate-marshal restriction). ──

    [LibraryImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static partial IntPtr SetWindowsHookEx(int idHook, IntPtr lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW", SetLastError = true)]
    private static partial int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static partial IntPtr DispatchMessage(ref MSG lpMsg);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll", EntryPoint = "PostThreadMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);
}
