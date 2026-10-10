using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Size = System.Windows.Size;

namespace ScreenSearchOverlay;

// Prompt Builder: Ctrl+Alt+G copies the selection and a screenshot, opens
// PromptWindow on them, and hands the composed message to ChatWindow.
//
// Latency budget, hotkey to window: the Ctrl+C round trip (10–50 ms) — the
// screen capture runs in parallel, everything else about the image happens
// after the window is up, and the window itself is built in advance.
public partial class App
{
    private const int PROMPT_HOTKEY_ID = 9001;
    private const uint VK_G = 0x47;
    private const string PromptsFileName = "prompts.json";
    private const string ChatLogFileName = "chat.log";
    // WebView2 profile (cookies, the refused cookie banner). Next to the exe,
    // like every other file this app writes.
    private const string WebViewFolderName = "ScreenSearchOverlay.WebView2";

    private PromptWindow? _promptWindow;
    private ChatWindow? _chatWindow;
    private bool _promptOpening;

    internal ObservableCollection<SavedPrompt> Prompts { get; private set; } = [];
    internal string LastPromptId { get; set; } = "";
    internal bool PromptAttachScreenshot { get; set; }
    internal Size? PromptWindowSize { get; set; }
    // Where the splitters were left, and the text size of the two text boxes.
    internal double? PromptSidebarWidth { get; set; }
    internal double? PromptBoxHeight { get; set; }
    internal double PromptTextSize { get; set; } = PromptWindow.DefaultTextSize;
    internal Rect? ChatWindowBounds { get; set; }
    // ChatGPT options of the settings panel: a temporary chat (nothing in the
    // account's history), and the send button clicked for the user — for a
    // text alone, and for a message with an image.
    internal bool ChatTemporary { get; set; } = true;
    internal bool ChatAutoSendText { get; set; } = true;
    internal bool ChatAutoSendImage { get; set; } = true;
    // Ctrl+Alt+G takes the image already in the clipboard (Win+Shift+S…)
    // rather than a new screenshot.
    internal bool ClipboardImageFirst { get; set; }

    // The clipboard as we last left it or read it: our own screenshot copy
    // is not an image the user put there, and an image already sent once is
    // not sent again — the next Ctrl+Alt+G takes a fresh screenshot.
    private uint _spentClipboardSequence;

    private void SetupPromptBuilder(IntPtr hotkeyHost)
    {
        ChatLog.Start(GetFilePath(ChatLogFileName));
        Prompts = PromptLibrary.Load(GetFilePath(PromptsFileName), Loc.Current);
        // Untouched starter prompts speak the UI language, now and after a change.
        if (StarterPrompts.Localize(Prompts, Loc.Current)) SavePromptsInBackground();
        Loc.Changed += () =>
        {
            if (StarterPrompts.Localize(Prompts, Loc.Current)) SavePromptsInBackground();
        };

        if (!RegisterHotKey(hotkeyHost, PROMPT_HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, VK_G))
        {
            Forms.MessageBox.Show(
                Loc.T("app.hotkeyFailed", "Ctrl+Alt+G"),
                "ScreenSearchOverlay",
                Forms.MessageBoxButtons.OK,
                Forms.MessageBoxIcon.Warning);
        }

        // Built once the app is idle after startup, so the first Ctrl+Alt+G
        // doesn't pay for parsing the window's XAML and creating its HWND.
        // The window is then reused: closing it only hides it.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
        {
            _promptWindow ??= new PromptWindow();
            new WindowInteropHelper(_promptWindow).EnsureHandle();
        });
    }

    private void TeardownPromptBuilder(IntPtr hotkeyHost)
    {
        UnregisterHotKey(hotkeyHost, PROMPT_HOTKEY_ID);
        // Edits saved in the background may not have reached the disk yet.
        SavePrompts();
        _chatWindow?.CloseForGood();
    }

    internal void SavePrompts() => PromptLibrary.Save(GetFilePath(PromptsFileName), Prompts);

    internal void SavePromptsInBackground() => PromptLibrary.SaveInBackground(GetFilePath(PromptsFileName), Prompts);

    // copySelection false: from the tray menu, nothing to copy.
    private async void ShowPromptBuilder(bool copySelection = true)
    {
        // A second press while we are still copying would fire a second
        // Ctrl+C into the middle of the first.
        if (_promptOpening) return;

        // Already in front: Ctrl+C would copy from our own window and
        // overwrite the text being edited. Just bring it back.
        if (_promptWindow is { IsActive: true })
        {
            _promptWindow.Present();
            return;
        }

        _promptOpening = true;
        try
        {
            // The clipboard image is read before the Ctrl+C, which may replace
            // it. Otherwise the screen is captured on a worker thread while
            // Ctrl+C runs.
            var sequence = SelectionGrabber.ClipboardSequence;
            var capture = (ClipboardImageFirst && sequence != _spentClipboardSequence
                    ? ScreenCapture.FromClipboardImage()
                    : null)
                ?? ScreenCapture.Start();
            if (capture.FromClipboard) _spentClipboardSequence = sequence;
            var text = copySelection ? await SelectionGrabber.CopySelectionAsync((ushort)VK_G) : "";

            // ChatGPT loads in the background while a prompt is picked —
            // after the copy, in case the text came from the ChatGPT window.
            ChatWindowInstance().Preheat();

            // Never cover the screen before it is captured (~100 ms at 4K,
            // normally over before the Ctrl+C round trip is).
            await capture.Taken;

            var window = _promptWindow ??= new PromptWindow();
            window.Load(text, capture);
            window.Present();

            // After the text: Win+V then lists the screenshot right above it.
            if (!capture.FromClipboard) CopyToClipboard(capture);
        }
        catch (Exception ex)
        {
            Forms.MessageBox.Show(Loc.T("app.promptError", ex.Message), "ScreenSearchOverlay",
                Forms.MessageBoxButtons.OK, Forms.MessageBoxIcon.Warning);
        }
        finally
        {
            _promptOpening = false;
        }
    }

    // The clipboard's current image was used (a region snip): not again.
    internal void SpendClipboard() => _spentClipboardSequence = SelectionGrabber.ClipboardSequence;

    private async void CopyToClipboard(ScreenCapture capture)
    {
        await capture.CopyToClipboardAsync();
        _spentClipboardSequence = SelectionGrabber.ClipboardSequence;
    }

    // image: the screenshot PNG, possibly still encoding — ChatWindow awaits
    // it only once the page is ready for it.
    internal async void SendToChat(string message, Task<byte[]?>? image) =>
        await ChatWindowInstance().SendAsync(message, image, image is null ? ChatAutoSendText : ChatAutoSendImage);

    // The Prompt Builder's ChatGPT button: the window, nothing sent.
    internal async void OpenChat() => await ChatWindowInstance().OpenAsync();

    // The Prompt Builder is showing: ChatGPT's window gets out of the way.
    internal void HideChat() => _chatWindow?.StepAside();

    private ChatWindow ChatWindowInstance() =>
        _chatWindow ??= new ChatWindow(
            GetFilePath(WebViewFolderName),
            ChatWindowBounds,
            bounds =>
            {
                ChatWindowBounds = bounds;
                SaveSettings();
            },
            backToPromptBuilder: () => (_promptWindow ??= new PromptWindow()).Present(),
            temporaryChat: () => ChatTemporary);

    // ── settings.json entries (called from SaveSettings / LoadSettings) ──

    private void SavePromptSettings(Dictionary<string, string> data)
    {
        data["promptAttachScreenshot"] = PromptAttachScreenshot.ToString();
        data["lastPromptId"] = LastPromptId;
        if (PromptWindowSize is { } size)
            data["promptWindowSize"] = FormatNumbers(size.Width, size.Height);
        if (ChatWindowBounds is { } bounds)
            data["chatWindowBounds"] = FormatNumbers(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (PromptSidebarWidth is { } sidebar)
            data["promptSidebarWidth"] = FormatNumbers(sidebar);
        if (PromptBoxHeight is { } promptHeight)
            data["promptBoxHeight"] = FormatNumbers(promptHeight);
        data["promptTextSize"] = FormatNumbers(PromptTextSize);
        data["chatTemporary"] = ChatTemporary.ToString();
        data["chatAutoSendText"] = ChatAutoSendText.ToString();
        data["chatAutoSendImage"] = ChatAutoSendImage.ToString();
        data["clipboardImageFirst"] = ClipboardImageFirst.ToString();
    }

    private void LoadPromptSettings(Dictionary<string, string> data)
    {
        if (data.TryGetValue("promptAttachScreenshot", out var attach))
            PromptAttachScreenshot = bool.TryParse(attach, out var v) && v;
        if (data.TryGetValue("lastPromptId", out var id))
            LastPromptId = id;
        if (data.TryGetValue("promptWindowSize", out var s) && ParseNumbers(s, 2) is { } n)
            PromptWindowSize = new Size(n[0], n[1]);
        if (data.TryGetValue("chatWindowBounds", out var c) && ParseNumbers(c, 4) is { } r)
            ChatWindowBounds = new Rect(r[0], r[1], r[2], r[3]);
        if (data.TryGetValue("promptSidebarWidth", out var w) && ParseNumbers(w, 1) is { } sidebar)
            PromptSidebarWidth = sidebar[0];
        if (data.TryGetValue("promptBoxHeight", out var h) && ParseNumbers(h, 1) is { } promptHeight)
            PromptBoxHeight = promptHeight[0];
        if (data.TryGetValue("promptTextSize", out var t) && ParseNumbers(t, 1) is { } textSize)
            PromptTextSize = textSize[0];
        if (data.TryGetValue("chatTemporary", out var temporary) && bool.TryParse(temporary, out var isTemporary))
            ChatTemporary = isTemporary;
        // 1.5.2 had a single option: it carries over to both.
        if (data.TryGetValue("chatAutoSend", out var autoSend) && bool.TryParse(autoSend, out var isAutoSend))
            ChatAutoSendText = ChatAutoSendImage = isAutoSend;
        if (data.TryGetValue("chatAutoSendText", out var autoText) && bool.TryParse(autoText, out var isAutoText))
            ChatAutoSendText = isAutoText;
        if (data.TryGetValue("chatAutoSendImage", out var autoImage) && bool.TryParse(autoImage, out var isAutoImage))
            ChatAutoSendImage = isAutoImage;
        if (data.TryGetValue("clipboardImageFirst", out var clipboardFirst) && bool.TryParse(clipboardFirst, out var isClipboardFirst))
            ClipboardImageFirst = isClipboardFirst;
    }

    private static string FormatNumbers(params double[] values) =>
        string.Join(",", values.Select(v => v.ToString(CultureInfo.InvariantCulture)));

    // Sizes must be positive (Size and Rect throw otherwise): every number of
    // a size or a single length, the last two of bounds (x, y, width, height).
    private static double[]? ParseNumbers(string text, int count)
    {
        var parts = text.Split(',');
        if (parts.Length != count) return null;
        var values = new double[count];
        for (int i = 0; i < count; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                return null;
        var sizes = count == 4 ? values[2..] : values;
        return sizes.All(v => v > 0) ? values : null;
    }
}
