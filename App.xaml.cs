using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ScreenSearchOverlay;

public partial class App : Application
{
    private const int HOTKEY_ID = 9000;
    private const uint MOD_WIN = 0x0008;
    private const uint VK_F = 0x46;
    private const int WM_HOTKEY = 0x0312;

    private HwndSource? _hwndSource;
    private MainWindow? _overlayWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create a hidden window to receive hotkey messages
        var parameters = new HwndSourceParameters("HotkeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0 // invisible
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        if (!RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, MOD_WIN, VK_F))
        {
            MessageBox.Show(
                "Impossible d'enregistrer Win+F.\nUn autre programme utilise peut-être ce raccourci.",
                "ScreenSearchOverlay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Show overlay immediately on first launch
        ShowOverlay();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
        }
        base.OnExit(e);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            handled = true;
            ShowOverlay();
        }
        return IntPtr.Zero;
    }

    private void ShowOverlay()
    {
        if (_overlayWindow is { IsVisible: true })
            return;

        _overlayWindow = new MainWindow();
        _overlayWindow.Closed += (_, _) => _overlayWindow = null;
        _overlayWindow.Show();
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
