using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
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

    private const int MaxHistoryEntries = 50;
    private const string HistoryFileName = "search-history.json";
    private const string SettingsFileName = "settings.json";

    // Selected OCR languages
    internal List<Windows.Globalization.Language> SelectedOcrLanguages { get; } = [];
    // Language tags the user unchecked in the tray menu. The excluded set is
    // what gets saved, so a language installed later starts enabled.
    private readonly HashSet<string> _disabledOcrLanguages = new(StringComparer.OrdinalIgnoreCase);

    // Search history
    internal List<string> SearchHistory { get; private set; } = [];

    // Persistent settings
    internal bool ZenMode { get; set; }
    internal string BoxSize { get; set; } = "medium";
    internal string SearchBarSize { get; set; } = "medium";
    internal double SearchBarX { get; set; } = -1;
    internal double SearchBarY { get; set; } = -1;
    internal string TranslateTarget { get; set; } = "fr";
    // UI language of the Prompt Builder and the tray menu (Loc).
    internal string Language { get; set; } = Loc.DefaultCode();

    // Tray items whose text follows the UI language.
    private Forms.ToolStripItem? _trayFindHint;
    private Forms.ToolStripItem? _trayPromptHint;
    private Forms.ToolStripMenuItem? _trayOcrMenu;
    private Forms.ToolStripItem? _trayQuit;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LoadHistory();
        LoadSettings();
        Loc.Apply(Language);
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
                Loc.T("app.hotkeyFailed", "Ctrl+Alt+F"),
                "ScreenSearchOverlay",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Warning);
        }

        SetupPromptBuilder(_hwndSource.Handle);
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
        _trayFindHint = menu.Items.Add("");
        _trayFindHint.Enabled = false;
        _trayPromptHint = menu.Items.Add("");
        _trayPromptHint.Enabled = false;
        menu.Items.Add(new Forms.ToolStripSeparator());

        // OCR Languages submenu
        var langMenu = new Forms.ToolStripMenuItem();
        _trayOcrMenu = langMenu;
        var availableLanguages = OcrEngine.AvailableRecognizerLanguages;

        foreach (var lang in availableLanguages)
        {
            var enabled = !_disabledOcrLanguages.Contains(lang.LanguageTag);
            // Name + tag: the tag identifies the language whatever the
            // language of the name.
            var item = new Forms.ToolStripMenuItem($"{lang.DisplayName} ({lang.LanguageTag})")
            {
                CheckOnClick = true,
                Checked = enabled,
                Tag = lang
            };
            item.CheckedChanged += (_, _) => UpdateSelectedLanguages(langMenu);
            langMenu.DropDownItems.Add(item);
            if (enabled)
                SelectedOcrLanguages.Add(lang);
        }

        menu.Items.Add(langMenu);
        menu.Items.Add(new Forms.ToolStripSeparator());
        _trayQuit = menu.Items.Add("", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            Shutdown();
        });

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => ShowOverlay();

        UpdateTrayTexts();
        Loc.Changed += UpdateTrayTexts;
    }

    private void UpdateTrayTexts()
    {
        if (_trayFindHint is null) return;
        _trayFindHint.Text = Loc.T("tray.findHint");
        _trayPromptHint!.Text = Loc.T("tray.promptHint");
        _trayOcrMenu!.Text = Loc.T("tray.ocr");
        _trayQuit!.Text = Loc.T("tray.quit");
    }

    internal void SetLanguage(string code)
    {
        Language = code;
        Loc.Apply(code);
        SaveSettings();
    }

    private void UpdateSelectedLanguages(Forms.ToolStripMenuItem langMenu)
    {
        SelectedOcrLanguages.Clear();
        _disabledOcrLanguages.Clear();
        foreach (Forms.ToolStripMenuItem item in langMenu.DropDownItems)
        {
            if (item.Tag is not Windows.Globalization.Language lang) continue;
            if (item.Checked)
                SelectedOcrLanguages.Add(lang);
            else
                _disabledOcrLanguages.Add(lang.LanguageTag);
        }
        SaveSettings();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hwndSource != null)
        {
            UnregisterHotKey(_hwndSource.Handle, HOTKEY_ID);
            TeardownPromptBuilder(_hwndSource.Handle);
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
        else if (msg == WM_HOTKEY && wParam.ToInt32() == PROMPT_HOTKEY_ID)
        {
            handled = true;
            ShowPromptBuilder();
        }
        return IntPtr.Zero;
    }

    private void ShowOverlay()
    {
        if (_overlayWindow is { IsVisible: true })
            return;

        // Capture screenshot BEFORE creating the window — no delay needed
        var screenInfo = ScreenSearchOverlay.MainWindow.GetCurrentScreenInfo();
        var screenshot = ScreenSearchOverlay.MainWindow.CaptureScreen(
            screenInfo.X, screenInfo.Y, screenInfo.Width, screenInfo.Height);

        _overlayWindow = new ScreenSearchOverlay.MainWindow(screenshot, screenInfo);
        _overlayWindow.Closed += (_, _) => _overlayWindow = null;
        _overlayWindow.Show();
    }

    internal void AddToSearchHistory(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return;
        SearchHistory.Remove(query);
        SearchHistory.Insert(0, query);
        if (SearchHistory.Count > MaxHistoryEntries)
            SearchHistory.RemoveAt(SearchHistory.Count - 1);
        SaveHistory();
    }

    private void LoadHistory()
    {
        try
        {
            var path = GetFilePath(HistoryFileName);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                SearchHistory = JsonSerializer.Deserialize<List<string>>(json) ?? [];
            }
        }
        catch { }
    }

    private void SaveHistory()
    {
        try
        {
            var json = JsonSerializer.Serialize(SearchHistory);
            File.WriteAllText(GetFilePath(HistoryFileName), json);
        }
        catch { }
    }

    internal void SaveSettings()
    {
        try
        {
            var data = new Dictionary<string, string>
            {
                ["zenMode"] = ZenMode.ToString(),
                ["boxSize"] = BoxSize,
                ["searchBarSize"] = SearchBarSize,
                ["searchBarX"] = SearchBarX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["searchBarY"] = SearchBarY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["translateTarget"] = TranslateTarget,
                ["disabledOcrLanguages"] = string.Join(",", _disabledOcrLanguages),
                ["language"] = Language
            };
            SavePromptSettings(data);
            var json = JsonSerializer.Serialize(data);
            File.WriteAllText(GetFilePath(SettingsFileName), json);
        }
        catch { }
    }

    private void LoadSettings()
    {
        try
        {
            var path = GetFilePath(SettingsFileName);
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (data != null)
                {
                    if (data.TryGetValue("zenMode", out var zen))
                        ZenMode = bool.TryParse(zen, out var v) && v;
                    if (data.TryGetValue("boxSize", out var size))
                        BoxSize = size;
                    if (data.TryGetValue("searchBarSize", out var sbSize))
                        SearchBarSize = sbSize;
                    if (data.TryGetValue("searchBarX", out var sx) &&
                        double.TryParse(sx, System.Globalization.CultureInfo.InvariantCulture, out var parsedX))
                        SearchBarX = parsedX;
                    if (data.TryGetValue("searchBarY", out var sy) &&
                        double.TryParse(sy, System.Globalization.CultureInfo.InvariantCulture, out var parsedY))
                        SearchBarY = parsedY;
                    if (data.TryGetValue("translateTarget", out var target) && !string.IsNullOrWhiteSpace(target))
                        TranslateTarget = target;
                    if (data.TryGetValue("language", out var language) && !string.IsNullOrWhiteSpace(language))
                        Language = language;
                    if (data.TryGetValue("disabledOcrLanguages", out var disabled))
                        _disabledOcrLanguages.UnionWith(disabled.Split(',', StringSplitOptions.RemoveEmptyEntries));
                    LoadPromptSettings(data);
                }
            }
        }
        catch { }
    }

    private static string GetFilePath(string fileName)
    {
        var dir = Path.GetDirectoryName(Environment.ProcessPath)
                  ?? AppDomain.CurrentDomain.BaseDirectory;
        return Path.Combine(dir, fileName);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(IntPtr hWnd, int id);
}
