using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Windows.Media.Ocr;
using Application = System.Windows.Application;
using Forms = System.Windows.Forms;

namespace ScreenSearchOverlay;

public partial class App : Application
{
    private const int HOTKEY_ID = 9000;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_NOREPEAT = 0x4000;
    private const uint VK_F = 0x46;
    private const int WM_HOTKEY = 0x0312;

    private HwndSource? _hwndSource;
    private MainWindow? _overlayWindow;
    private Forms.NotifyIcon? _trayIcon;

    // Selected OCR languages
    internal List<Windows.Globalization.Language> SelectedOcrLanguages { get; } = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        SetupTrayIcon();

        var parameters = new HwndSourceParameters("HotkeyHost")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };
        _hwndSource = new HwndSource(parameters);
        _hwndSource.AddHook(WndProc);

        if (!RegisterHotKey(_hwndSource.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_F))
        {
            Forms.MessageBox.Show(
                "Impossible d'enregistrer Ctrl+Alt+F.\nUn autre programme utilise peut-être ce raccourci.",
                "ScreenSearchOverlay",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Warning);
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "Screen Search Overlay (Ctrl+Alt+F)",
            Visible = true
        };

        try
        {
            var exePath = Environment.ProcessPath;
            if (exePath != null)
                _trayIcon.Icon = Icon.ExtractAssociatedIcon(exePath);
        }
        catch
        {
            _trayIcon.Icon = SystemIcons.Application;
        }

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Screen Search Overlay").Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Ctrl+Alt+F pour chercher").Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());

        // OCR Languages submenu
        var langMenu = new Forms.ToolStripMenuItem("OCR Languages");
        var availableLanguages = OcrEngine.AvailableRecognizerLanguages;

        foreach (var lang in availableLanguages)
        {
            var item = new Forms.ToolStripMenuItem(lang.DisplayName)
            {
                CheckOnClick = true,
                Checked = true,
                Tag = lang
            };
            item.CheckedChanged += (_, _) => UpdateSelectedLanguages(langMenu);
            langMenu.DropDownItems.Add(item);
            SelectedOcrLanguages.Add(lang);
        }

        menu.Items.Add(langMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Shutdown();
        });

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowOverlay();
    }

    private void UpdateSelectedLanguages(Forms.ToolStripMenuItem langMenu)
    {
        SelectedOcrLanguages.Clear();
        foreach (Forms.ToolStripMenuItem item in langMenu.DropDownItems)
        {
            if (item.Checked && item.Tag is Windows.Globalization.Language lang)
                SelectedOcrLanguages.Add(lang);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
        }

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
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
