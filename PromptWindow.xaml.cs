using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Forms = System.Windows.Forms;
using Image = System.Windows.Controls.Image;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Size = System.Windows.Size;

namespace ScreenSearchOverlay;

// A language or a theme of the settings panel. Badge: a language's code,
// shown small beside its name.
public sealed record LanguageRow(string Code, string Label, bool IsCurrent, string Badge = "");

// Saved prompts on the left, the text to send on the right. The selected
// prompt is edited in place and saved as you type; this window only composes
// the message — sending it is ChatWindow's job.
//
// Built once and reused: closing only hides it, so Ctrl+Alt+G never pays for
// XAML parsing, HWND creation or template instantiation twice — and the
// ChatGPT window's "Prompt Builder" button finds it as it was left.
public partial class PromptWindow : Window
{
    private const int MaxShortcuts = 9;
    private static readonly string[] Digits = ["1", "2", "3", "4", "5", "6", "7", "8", "9"];

    // Title colors, picked for contrast on the dark list. "" = default text color.
    private static readonly string[] TitleColors =
        ["", "#F87171", "#FB923C", "#FACC15", "#4ADE80", "#2DD4BF", "#38BDF8", "#818CF8", "#C084FC", "#F472B6"];

    internal const double DefaultTextSize = 13.5;
    private const double MinTextSize = 10;
    private const double MaxTextSize = 28;

    // Settings panel, "Generate an account": a throwaway address to sign up with.
    private const string TempMailUrl = "https://temp-mail.id";
    private const string ChangelogUrl = "https://github.com/jonlepage/LS-OCRLAYOUT/blob/main/CHANGELOG.md";
    // "Buy me a tea": a Stripe payment link, the amount chosen by the payer.
    private const string TeaUrl = "https://buy.stripe.com/aFaaEY3elgUa9Jp29x57W04";

    private readonly App _app = (App)System.Windows.Application.Current;
    private readonly ListCollectionView _view;
    // Prompts currently showing a keycap, so a refresh only touches those.
    private readonly List<SavedPrompt> _numbered = new(MaxShortcuts);
    private readonly List<Button> _swatches = [];

    // Edits are saved shortly after you stop typing, not on every keystroke.
    private readonly DispatcherTimer _saveSoon = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _undoExpiry = new() { Interval = TimeSpan.FromSeconds(8) };
    // The selected prompt is remembered as soon as it is picked, not only when
    // the window closes: the app may be killed or the session ended first.
    private readonly DispatcherTimer _saveSelectionSoon = new() { Interval = TimeSpan.FromSeconds(1) };
    private (SavedPrompt Prompt, int Index)? _deleted;
    private ScreenCapture? _capture;
    // The search text, trimmed once per keystroke instead of once per prompt.
    private string _query = "";

    public PromptWindow()
    {
        InitializeComponent();

        // Before _view: Option_Changed ignores these, as it does everything
        // raised during construction.
        TemporaryChatOption.IsChecked = _app.ChatTemporary;
        AutoSendTextOption.IsChecked = _app.ChatAutoSendText;
        AutoSendImageOption.IsChecked = _app.ChatAutoSendImage;
        ClipboardImageOption.IsChecked = _app.ClipboardImageFirst;
        AutoUpdateOption.IsChecked = _app.CheckUpdatesAutomatically;

        // Read before the list is filled: filling it selects the first prompt,
        // and every selection is remembered as the last one.
        var lastPromptId = _app.LastPromptId;

        // A view of our own: the filter must not leak into the app-wide collection.
        _view = new ListCollectionView(_app.Prompts) { Filter = Matches };
        PromptList.ItemsSource = _view;

        _app.Prompts.CollectionChanged += Prompts_CollectionChanged;
        foreach (var prompt in _app.Prompts)
            prompt.PropertyChanged += Prompt_PropertyChanged;

        _saveSoon.Tick += (_, _) => { _saveSoon.Stop(); _app.SavePromptsInBackground(); };
        _undoExpiry.Tick += (_, _) => HideUndo();
        _saveSelectionSoon.Tick += (_, _) => { _saveSelectionSoon.Stop(); _app.SaveSettings(); };
        SourceInitialized += (_, _) => ConfigureNativeWindow();
        Loc.Changed += OnLanguageChanged;
        Theme.Changed += OnThemeChanged;
        _app.UpdateChanged += RefreshUpdateRow;
        _app.McpChanged += RefreshMcp;

        RestoreLayout();
        BuildSwatches();
        BuildLanguageRows();
        BuildThemeRows();
        AttachCapture.IsChecked = _app.PromptAttachScreenshot;

        Select(_app.Prompts.FirstOrDefault(p => p.Id == lastPromptId) ?? _app.Prompts.FirstOrDefault());
        RefreshListState();
        UpdateEditorState();
        RefreshUpdateRow();
    }

    private SavedPrompt? Selected => PromptList.SelectedItem as SavedPrompt;

    // ── Called by App on every Ctrl+Alt+G ──

    internal void Load(string text, ScreenCapture capture)
    {
        // Window already open and nothing selected this time: keep what was
        // being written rather than wiping it.
        if (text.Length > 0 || !IsVisible)
            BodyBox.Text = text;
        SearchBox.Text = "";

        // An image put in the clipboard was put there to be sent: attached.
        // After one, a screenshot goes back to the user's own choice.
        if (capture.FromClipboard)
            AttachCapture.IsChecked = true;
        else if (_capture is { FromClipboard: true })
            AttachCapture.IsChecked = _app.PromptAttachScreenshot;
        ShowCapture(capture, capture.FromClipboard ? "pb.capture.clipboard" : "pb.capture");
    }

    // The footer's thumbnail and checkbox for this capture.
    private async void ShowCapture(ScreenCapture capture, string label)
    {
        _capture = capture;
        AttachCapture.SetResourceReference(ContentProperty, label);
        CapturePanel.Visibility = Visibility.Visible;
        CaptureThumb.Background = null;
        CaptureThumb.ToolTip = ThumbTip(null);
        UpdateComposerState();

        // Downscaled off the UI thread; lands a few tens of ms after the
        // window is up.
        var previews = await capture.Previews;
        if (_capture != capture) return; // a newer Ctrl+Alt+G took over
        if (previews is null)
        {
            CapturePanel.Visibility = Visibility.Collapsed;
            _capture = null;
            UpdateComposerState();
            return;
        }

        var thumbnail = new ImageBrush(previews.Thumbnail) { Stretch = Stretch.UniformToFill };
        thumbnail.Freeze();
        CaptureThumb.Background = thumbnail;
        CaptureThumb.ToolTip = ThumbTip(previews.Preview);
    }

    // The preview, and what a click does.
    private static StackPanel ThumbTip(ImageSource? preview)
    {
        var tip = new StackPanel();
        if (preview is not null)
            tip.Children.Add(new Image { Source = preview, Width = 560, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) });
        var hint = new TextBlock();
        hint.SetResourceReference(TextBlock.TextProperty, "pb.capture.snipTip");
        tip.Children.Add(hint);
        return tip;
    }

    // ── Region capture: a click on the thumbnail ──

    // How long DWM takes to fade this window out: the overlay freezes the
    // screen as it opens, and this window must not be in it.
    private const int HideBeforeSnipMs = 200;
    private bool _snipping;

    // Windows' snipping overlay (RegionSnip) for a region of the screen. This
    // window steps aside and comes back with the region as its capture —
    // attached: taking it shows the intent. Canceled: back as it was.
    private async void CaptureThumb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_snipping) return;
        _snipping = true;
        try
        {
            Hide();
            await Task.Delay(HideBeforeSnipMs);
            var taken = await RegionSnip.RunAsync(abandoned: () => IsVisible);
            // Brought back meanwhile (Ctrl+Alt+G): that capture wins.
            if (IsVisible) return;
            if (taken && ScreenCapture.FromClipboardImage() is { } region)
            {
                _app.SpendClipboard();
                AttachCapture.IsChecked = true;
                ShowCapture(region, "pb.capture");
            }
        }
        finally
        {
            _snipping = false;
            if (!IsVisible) Present();
        }
    }

    // Also the ChatGPT window's "Prompt Builder" button: the window comes
    // back as it was left — same prompt, same text, same screenshot.
    internal void Present()
    {
        // One window at a time: this one or ChatGPT's.
        _app.HideChat();
        if (!IsVisible)
        {
            CenterOnCursorScreen();
            Show();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        WindowActivation.BringToFront(this);

        // With text: start in the search box — type to filter, arrows to
        // pick, Enter to send. Without: straight into the text box.
        IInputElement target = string.IsNullOrWhiteSpace(BodyBox.Text) ? BodyBox : SearchBox;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(target));
    }

    // ── Sending ──

    private void Send()
    {
        var message = PromptLibrary.Compose(Selected?.Prompt, BodyBox.Text);
        var image = AttachCapture.IsChecked == true ? _capture?.Png : null;
        if (message.Length == 0 && image is null) return;

        Close();
        _app.SendToChat(message, image);
    }

    // Called on every keystroke: the send button and the total of what
    // ChatGPT will receive, counted without building the message (prompt + a
    // possibly long text). Same trimming and separator as Compose.
    private void UpdateComposerState()
    {
        if (_view is null) return; // during InitializeComponent
        var promptLength = (Selected?.Prompt ?? "").AsSpan().Trim().Length;
        var textLength = BodyBox.Text.AsSpan().Trim().Length;
        var total = promptLength == 0 ? textLength
            : textLength == 0 ? promptLength
            : promptLength + 2 + textLength;
        var withImage = AttachCapture.IsChecked == true && _capture is not null;

        SendButton.IsEnabled = total > 0 || withImage;
        TotalCount.Text = total == 0 && !withImage
            ? ""
            : Loc.T("pb.total", Loc.Characters(total)) + (withImage ? Loc.T("pb.total.capture") : "");
    }

    // ── List: filter, selection, shortcuts ──

    private bool Matches(object item)
    {
        if (_query.Length == 0) return true;
        var prompt = (SavedPrompt)item;
        return Contains(prompt.Name, _query) || Contains(prompt.Prompt, _query);
    }

    // "resume" finds "Résumer": accents and case are ignored.
    private static bool Contains(string source, string value) =>
        CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            source, value, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

    private void Select(SavedPrompt? prompt)
    {
        PromptList.SelectedItem = prompt;
        if (prompt is not null) PromptList.ScrollIntoView(prompt);
    }

    private SavedPrompt VisibleAt(int index) => (SavedPrompt)_view.GetItemAt(index);

    private void Step(int delta)
    {
        if (_view.Count == 0) return;
        var index = Selected is null ? -1 : _view.IndexOf(Selected);
        Select(VisibleAt(Math.Clamp(index + delta, 0, _view.Count - 1)));
    }

    private void SelectNth(int index)
    {
        if (index < _view.Count) Select(VisibleAt(index));
    }

    // After a filter or collection change: keycaps follow what is on screen
    // (Ctrl+1 is always the first visible prompt), and the empty state.
    private void RefreshListState()
    {
        var count = Math.Min(_view.Count, MaxShortcuts);
        // Only prompts that lose their keycap are cleared; the rest are
        // reassigned, and an unchanged value raises no notification.
        foreach (var prompt in _numbered)
        {
            var index = _view.IndexOf(prompt);
            if (index < 0 || index >= count) prompt.Shortcut = "";
        }
        _numbered.Clear();
        for (int i = 0; i < count; i++)
        {
            var prompt = VisibleAt(i);
            prompt.Shortcut = Digits[i];
            _numbered.Add(prompt);
        }

        EmptyList.Visibility = _view.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_view.Count == 0)
        {
            EmptyListText.Text = Loc.T(_query.Length == 0 ? "pb.empty.none" : "pb.empty.noMatch");
            CreateFromSearchButton.Content = _query.Length == 0
                ? Loc.T("pb.empty.create")
                : Loc.T("pb.empty.createNamed", Truncate(_query, 26));
        }
    }

    private void UpdateEditorState()
    {
        var hasSelection = Selected is not null;
        NameBox.IsEnabled = hasSelection;
        PromptBox.IsEnabled = hasSelection;
        PromptActions.IsEnabled = hasSelection;
        RefreshSwatches();
        RefreshRestore();
        UpdateComposerState();
    }

    private void RefreshRestore() =>
        RestoreButton.Visibility = Selected is { } prompt && StarterPrompts.CanRestore(prompt, Loc.Current)
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } prompt) StarterPrompts.Restore(prompt, Loc.Current);
    }

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..(length - 1)] + "…";

    // ── Prompt management ──

    private void NewPrompt(string name = "")
    {
        SearchBox.Text = "";
        var prompt = new SavedPrompt { Name = name };
        _app.Prompts.Insert(0, prompt);
        Select(prompt);
        PromptBox.Focus();
    }

    private void Duplicate()
    {
        if (Selected is not { } source) return;
        var copy = new SavedPrompt
        {
            Name = string.IsNullOrWhiteSpace(source.Name) ? "" : source.Name.Trim() + Loc.T("prompt.copySuffix"),
            Prompt = source.Prompt,
            TitleColor = source.TitleColor,
        };
        _app.Prompts.Insert(_app.Prompts.IndexOf(source) + 1, copy);
        Select(copy);
        NameBox.Focus();
        NameBox.SelectAll();
    }

    // Moves among the VISIBLE prompts, so it also works while a search
    // filters the list.
    private void Move(int delta)
    {
        if (Selected is not { } current) return;
        var target = _view.IndexOf(current) + delta;
        if (target < 0 || target >= _view.Count) return;

        var listHadFocus = PromptList.IsKeyboardFocusWithin;
        _app.Prompts.Move(_app.Prompts.IndexOf(current), _app.Prompts.IndexOf(VisibleAt(target)));
        Select(current);
        if (listHadFocus) FocusSelectedItem();
    }

    private void Delete()
    {
        if (Selected is not { } current) return;
        var listHadFocus = PromptList.IsKeyboardFocusWithin;
        var visibleIndex = _view.IndexOf(current);
        var index = _app.Prompts.IndexOf(current);

        _app.Prompts.RemoveAt(index);
        _deleted = (current, index);
        ShowUndo(Loc.T("pb.deleted", Truncate(current.DisplayName, 36)));

        if (_view.Count > 0)
        {
            Select(VisibleAt(Math.Min(visibleIndex, _view.Count - 1)));
            if (listHadFocus) FocusSelectedItem();
        }
    }

    private void Undo()
    {
        if (_deleted is not { } deleted) return;
        _app.Prompts.Insert(Math.Min(deleted.Index, _app.Prompts.Count), deleted.Prompt);
        Select(deleted.Prompt);
        HideUndo();
    }

    private void ShowUndo(string message)
    {
        UndoText.Text = message;
        HintText.Visibility = Visibility.Collapsed;
        UndoPanel.Visibility = Visibility.Visible;
        _undoExpiry.Stop();
        _undoExpiry.Start();
    }

    private void HideUndo()
    {
        _undoExpiry.Stop();
        _deleted = null;
        UndoPanel.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Visible;
    }

    private void FocusSelectedItem()
    {
        if (Selected is null) return;
        // Virtualized list: the row must be realized before it can take focus.
        PromptList.ScrollIntoView(Selected);
        PromptList.UpdateLayout();
        (PromptList.ItemContainerGenerator.ContainerFromItem(Selected) as ListBoxItem)?.Focus();
    }

    private void SaveSoon()
    {
        _saveSoon.Stop();
        _saveSoon.Start();
    }

    // ── Title color ──

    // Built once; the palette shows while the title is being edited.
    private void BuildSwatches()
    {
        foreach (var hex in TitleColors)
        {
            var swatch = new Button
            {
                Style = (Style)FindResource("SwatchButton"),
                Tag = hex,
            };
            if (hex.Length == 0)
            {
                swatch.SetResourceReference(BackgroundProperty, "TextPrimary");
                swatch.SetResourceReference(ToolTipProperty, "pb.color.default");
            }
            else swatch.Background = Theme.TitleBrush(hex);
            swatch.Click += Swatch_Click;
            _swatches.Add(swatch);
            TitlePalette.Children.Add(swatch);
        }
    }

    // The current color is ringed in the text color.
    private void RefreshSwatches()
    {
        var current = Selected?.TitleColor ?? "";
        var ring = Theme.Brush("TextPrimary");
        foreach (var swatch in _swatches)
            swatch.BorderBrush = (string)swatch.Tag == current ? ring : System.Windows.Media.Brushes.Transparent;
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is { } prompt) prompt.TitleColor = (string)((Button)sender).Tag;
    }

    // ── Layout: splitters and text size, remembered ──

    private void RestoreLayout()
    {
        if (_app.PromptWindowSize is { } size)
        {
            Width = Math.Max(size.Width, MinWidth);
            Height = Math.Max(size.Height, MinHeight);
        }
        if (_app.PromptSidebarWidth is { } sidebar)
            SidebarColumn.Width = new GridLength(Math.Clamp(sidebar, SidebarColumn.MinWidth, SidebarColumn.MaxWidth));
        if (_app.PromptBoxHeight is { } promptHeight)
            PromptRow.Height = new GridLength(Math.Max(promptHeight, PromptRow.MinHeight));
        ApplyTextSize(_app.PromptTextSize);
    }

    private void ApplyTextSize(double size)
    {
        size = Math.Clamp(size, MinTextSize, MaxTextSize);
        PromptBox.FontSize = size;
        BodyBox.FontSize = size;
        TextSizeValue.Text = size.ToString("0.#", Loc.Culture);
        _app.PromptTextSize = size;
    }

    private void ChangeTextSize(double delta)
    {
        ApplyTextSize(PromptBox.FontSize + delta);
        _app.SaveSettings();
    }

    private void Splitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _app.PromptSidebarWidth = SidebarColumn.ActualWidth;
        _app.PromptBoxHeight = PromptRow.ActualHeight;
        _app.SaveSettings();
    }

    // Ctrl+wheel in either text box: text size, for both.
    private void TextArea_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        ChangeTextSize(e.Delta > 0 ? 1 : -1);
        e.Handled = true;
    }

    // ── Settings panel ──

    // The settings rows show the current choice; their submenus list them all.
    private void BuildLanguageRows()
    {
        LanguageRows.ItemsSource = Loc.Languages.Select(l => new LanguageRow(l.Code, l.Name, l.Code == Loc.Current, l.Code)).ToList();
        LanguageValue.Text = Loc.Languages.First(l => l.Code == Loc.Current).Name;
    }

    private void OnLanguageChanged()
    {
        BuildLanguageRows();
        BuildThemeRows();
        foreach (var prompt in _app.Prompts) prompt.RefreshDisplayName();
        RefreshListState();
        UpdateComposerState();
        CharCount.Text = BodyBox.Text.Length == 0 ? "" : Loc.Characters(BodyBox.Text.Length);
        TextSizeValue.Text = PromptBox.FontSize.ToString("0.#", Loc.Culture);
        RefreshUpdateRow();
        RefreshMcpDialog();
        // The new texts change the hint's and the total's widths: refit once laid out.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitHint);
    }

    private void BuildThemeRows()
    {
        ThemeRows.ItemsSource = Theme.Names.Select(n => new LanguageRow(n, Loc.T("theme." + n), n == Theme.Current)).ToList();
        ThemeValue.Text = Loc.T("theme." + Theme.Current);
    }

    // Brushes set from code, which no DynamicResource follows: the palette
    // (deeper shades on the light theme), its ring, and the titles.
    private void OnThemeChanged()
    {
        BuildThemeRows();
        foreach (var swatch in _swatches)
            if ((string)swatch.Tag is { Length: > 0 } hex) swatch.Background = Theme.TitleBrush(hex);
        RefreshSwatches();
        foreach (var prompt in _app.Prompts) prompt.RefreshTitleColor();
    }

    // The submenu closes, the settings stay open: the change shows live.
    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        ThemePopup.IsOpen = false;
        _app.SetTheme((string)((Button)sender).Tag);
    }

    private void LanguageMenu_Click(object sender, RoutedEventArgs e) => LanguagePopup.IsOpen = true;
    private void ThemeMenu_Click(object sender, RoutedEventArgs e) => ThemePopup.IsOpen = true;

    // A submenu is a popup of its own: it would outlive the panel.
    private void SettingsPopup_Closed(object? sender, EventArgs e)
    {
        LanguagePopup.IsOpen = false;
        ThemePopup.IsOpen = false;
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsPopup.IsOpen = !SettingsPopup.IsOpen;

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        LanguagePopup.IsOpen = false;
        _app.SetLanguage((string)((Button)sender).Tag);
    }

    // In the system browser, like every link leaving the app.
    private void NewAccount_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        try { Process.Start(new ProcessStartInfo(TempMailUrl) { UseShellExecute = true }); } catch { }
    }

    private void TeaLink_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(TeaUrl) { UseShellExecute = true }); } catch { }
    }

    private void Changelog_Click(object sender, RoutedEventArgs e)
    {
        SettingsPopup.IsOpen = false;
        try { Process.Start(new ProcessStartInfo(ChangelogUrl) { UseShellExecute = true }); } catch { }
    }

    // The settings checkboxes. Read at use: the next send, the next check.
    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (_view is null) return; // during construction
        _app.ChatTemporary = TemporaryChatOption.IsChecked == true;
        _app.ChatAutoSendText = AutoSendTextOption.IsChecked == true;
        _app.ChatAutoSendImage = AutoSendImageOption.IsChecked == true;
        _app.ClipboardImageFirst = ClipboardImageOption.IsChecked == true;
        _app.CheckUpdatesAutomatically = AutoUpdateOption.IsChecked == true;
        _app.SaveSettings();
    }

    // ── MCP server (App.Mcp) ──

    private bool _syncingMcp;

    // On: the server starts and the setup dialog shows the lines to give the
    // agent. Off at every start of the app, never saved.
    private void McpOption_Changed(object sender, RoutedEventArgs e)
    {
        if (_view is null || _syncingMcp) return;
        var enabled = McpOption.IsChecked == true;
        _app.SetMcpEnabled(enabled);
        if (enabled) ShowMcpDialog();
    }

    private void McpHelp_Click(object sender, RoutedEventArgs e) => ShowMcpDialog();

    // The checkbox follows the server: a start that failed (port taken)
    // unticks it, and the dialog says why.
    private void RefreshMcp()
    {
        _syncingMcp = true;
        McpOption.IsChecked = _app.McpEnabled;
        _syncingMcp = false;
        RefreshMcpDialog();
    }

    private void ShowMcpDialog()
    {
        SettingsPopup.IsOpen = false;
        RefreshMcpDialog();
        McpDialog.Visibility = Visibility.Visible;
    }

    private void RefreshMcpDialog()
    {
        var url = _app.McpUrl;
        McpCommand.Text = $"claude mcp add --scope user --transport http {McpServer.ServerName} {url}";
        McpJson.Text = $$"""
            {
              "mcpServers": {
                "{{McpServer.ServerName}}": {
                  "type": "http",
                  "url": "{{url}}"
                }
              }
            }
            """;
        (McpStatus.Text, var color) = _app.McpError is { } error
            ? (Loc.T("mcp.failed", McpServer.DefaultPort, error), "Danger")
            : _app.McpEnabled
                ? (Loc.T("mcp.running", McpServer.DefaultPort), "StatusSuccess")
                : (Loc.T("mcp.stopped"), "TextMuted");
        McpStatus.SetResourceReference(TextBlock.ForegroundProperty, color);
    }

    private void McpDialogClose_Click(object sender, RoutedEventArgs e) => McpDialog.Visibility = Visibility.Collapsed;

    private void McpDialogBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) =>
        McpDialog.Visibility = Visibility.Collapsed;

    // A click in the card is not a click on the backdrop behind it.
    private void McpDialogCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    // The copy icon turns into a check for a moment.
    private async void McpCopy_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        try { System.Windows.Clipboard.SetText((string)button.Tag); } catch { return; }
        button.Content = "\uE73E";
        await Task.Delay(1200);
        button.Content = "\uE8C8";
    }

    // The popup stays open: it shows the check's result, then the download.
    private void Update_Click(object sender, RoutedEventArgs e) => _app.RunUpdateAction();

    private void RefreshUpdateRow()
    {
        var available = _app.UpdateStage == UpdateStage.Available;
        UpdateStatus.Text = _app.UpdateStatusText;
        UpdateButtonText.Text = _app.UpdateActionText;
        UpdateIcon.Text = available ? "\uE896" : "\uE895"; // Download, Sync
        UpdateButton.IsEnabled = !_app.UpdateBusy;

        // The title bar's pill: the offer, then the download's progress —
        // shown, not clickable, so it keeps its full color.
        var downloading = _app.UpdateStage == UpdateStage.Downloading;
        UpdateBanner.Visibility = available || downloading ? Visibility.Visible : Visibility.Collapsed;
        TeaLink.Visibility = UpdateBanner.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        UpdateBanner.IsHitTestVisible = available;
        if (available && _app.AvailableUpdate is { } update)
        {
            UpdateBannerText.Text = Loc.T("pb.update.banner", update.Version);
            UpdateBanner.ToolTip = Loc.T("pb.update.banner.tip", (update.Size / 1048576.0).ToString("0", Loc.Culture));
        }
        else if (downloading)
        {
            UpdateBannerText.Text = _app.UpdateStatusText;
            UpdateBanner.ToolTip = null;
        }
    }

    // The keyboard hint is a nicety: when the footer is too narrow for all of
    // it (narrow window, longer language), it steps aside rather than being
    // cut. Opacity, not Visibility, which the undo bar already drives.
    private void FitHint()
    {
        HintText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var available = HintHost.ActualWidth - HintText.Margin.Left - HintText.Margin.Right;
        HintText.Opacity = HintText.DesiredSize.Width <= available ? 1 : 0;
    }

    private void HintHost_SizeChanged(object sender, SizeChangedEventArgs e) => FitHint();

    private void TextSmaller_Click(object sender, RoutedEventArgs e) => ChangeTextSize(-1);
    private void TextLarger_Click(object sender, RoutedEventArgs e) => ChangeTextSize(+1);

    // ── Collection / item events ──

    private void Prompts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (SavedPrompt prompt in e.OldItems) prompt.PropertyChanged -= Prompt_PropertyChanged;
        if (e.NewItems is not null)
            foreach (SavedPrompt prompt in e.NewItems) prompt.PropertyChanged += Prompt_PropertyChanged;

        RefreshListState();
        SaveSoon();
    }

    private void Prompt_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SavedPrompt.Prompt):
                if (sender == Selected)
                {
                    UpdateComposerState();
                    RefreshRestore();
                }
                SaveSoon();
                break;
            case nameof(SavedPrompt.TitleColor):
                if (sender == Selected) RefreshSwatches();
                SaveSoon();
                break;
            case nameof(SavedPrompt.Name):
                if (sender == Selected) RefreshRestore();
                SaveSoon();
                break;
        }
    }

    // ── UI events ──

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;
        var ctrl = modifiers == ModifierKeys.Control;
        var alt = modifiers == ModifierKeys.Alt;
        var none = modifiers == ModifierKeys.None;
        var inSearch = SearchBox.IsKeyboardFocusWithin;
        var inList = PromptList.IsKeyboardFocusWithin;

        // The MCP dialog is modal: Esc closes it, no other shortcut reaches
        // the window behind it (Ctrl+Enter would send).
        if (McpDialog.Visibility == Visibility.Visible)
        {
            if (key == Key.Escape && none)
            {
                McpDialog.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
            return;
        }

        switch (key)
        {
            case Key.Escape when none && SettingsPopup.IsOpen:
                SettingsPopup.IsOpen = false;
                break;
            case Key.Escape when none:
                if (SearchBox.Text.Length > 0) SearchBox.Text = "";
                else Close();
                break;
            case Key.Enter when ctrl:
            case Key.Enter when none && (inSearch || inList):
                Send();
                break;
            case Key.N when ctrl:
                NewPrompt();
                break;
            case Key.D when ctrl:
                Duplicate();
                break;
            case Key.F when ctrl:
                SearchBox.Focus();
                SearchBox.SelectAll();
                break;
            case >= Key.D1 and <= Key.D9 when ctrl:
                SelectNth(key - Key.D1);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when ctrl:
                SelectNth(key - Key.NumPad1);
                break;
            case Key.Up when alt:
                Move(-1);
                break;
            case Key.Down when alt:
                Move(+1);
                break;
            case Key.Up when none && inSearch:
                Step(-1);
                break;
            case Key.Down when none && inSearch:
                Step(+1);
                break;
            case Key.Delete when none && inList:
                Delete();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    // Like the send, without the message: this window steps aside for ChatGPT's.
    private void OpenChat_Click(object sender, RoutedEventArgs e)
    {
        Close();
        _app.OpenChat();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_view is null) return;
        _query = SearchBox.Text.Trim();
        _view.Refresh();
        if (Selected is null || !_view.Contains(Selected))
            Select(_view.Count > 0 ? VisibleAt(0) : null);
        RefreshListState();
    }

    private void PromptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_view is null) return;
        UpdateEditorState();
        if (Selected is { } prompt && prompt.Id != _app.LastPromptId)
        {
            _app.LastPromptId = prompt.Id;
            _saveSelectionSoon.Stop();
            _saveSelectionSoon.Start();
        }
    }

    private void PromptList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Only a double-click ON a prompt sends — not one on the empty area or the scrollbar.
        if (ItemsControl.ContainerFromElement(PromptList, e.OriginalSource as DependencyObject) is ListBoxItem)
            Send();
    }

    private void BodyBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var count = BodyBox.Text.Length;
        CharCount.Text = count == 0 ? "" : Loc.Characters(count);
        UpdateComposerState();
    }

    private void AttachCapture_Changed(object sender, RoutedEventArgs e) => UpdateComposerState();

    private void Send_Click(object sender, RoutedEventArgs e) => Send();
    private void NewPrompt_Click(object sender, RoutedEventArgs e) => NewPrompt();
    private void CreateFromSearch_Click(object sender, RoutedEventArgs e) => NewPrompt(_query);
    private void Duplicate_Click(object sender, RoutedEventArgs e) => Duplicate();
    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(+1);
    private void Delete_Click(object sender, RoutedEventArgs e) => Delete();
    private void Undo_Click(object sender, RoutedEventArgs e) => Undo();

    // Closing only hides; the next Ctrl+Alt+G — or the ChatGPT window's back
    // button — shows this same window again. The screenshot is kept for that
    // (a few Mo). At app exit WPF ignores the Cancel and really closes it.
    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        SettingsPopup.IsOpen = false;
        McpDialog.Visibility = Visibility.Collapsed;

        // "Nouveau prompt" then nothing typed: not worth keeping.
        foreach (var blank in _app.Prompts.Where(p => string.IsNullOrWhiteSpace(p.Name) && string.IsNullOrWhiteSpace(p.Prompt)).ToList())
            _app.Prompts.Remove(blank);
        _saveSoon.Stop();
        _saveSelectionSoon.Stop();
        HideUndo();

        _app.LastPromptId = Selected?.Id ?? _app.LastPromptId;
        // Not a clipboard image's forced tick: the choice is the user's.
        if (_capture is not { FromClipboard: true })
            _app.PromptAttachScreenshot = AttachCapture.IsChecked == true;
        _app.PromptWindowSize = new Size(Width, Height);
        _app.SavePromptsInBackground();
        _app.SaveSettings();

        Hide();
    }

    // ── Native window ──

    private void ConfigureNativeWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // No maximize: the layout is made for a dialog-sized window, and a
        // maximized chrome-less window overhangs the screen edges.
        SetWindowLong(hwnd, GWL_STYLE, GetWindowLong(hwnd, GWL_STYLE) & ~WS_MAXIMIZEBOX);
        // Square corners on Windows 11 too (it rounds them by default;
        // ignored on Windows 10).
        int preference = DWMWCP_DONOTROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
    }

    // Shown on the monitor the user is working on, not where it was last
    // hidden. Physical pixels end to end, so mixed-DPI setups stay right.
    private void CenterOnCursorScreen()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        if (!GetWindowRect(hwnd, out var rect)) return;
        var area = Forms.Screen.FromPoint(Forms.Cursor.Position).WorkingArea;
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        SetWindowPos(hwnd, IntPtr.Zero,
            area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2,
            0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    private const int GWL_STYLE = -16;
    private const int WS_MAXIMIZEBOX = 0x00010000;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_DONOTROUND = 1;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}

// "#RRGGBB" → brush, for a prompt's title color, as the current theme shows
// it (Theme caches them). "" falls back to the theme's text color, or to its
// accent with ConverterParameter=accent (the list's selection bar). A theme
// change re-runs it through SavedPrompt.RefreshTitleColor.
public sealed class TitleBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fallback = Theme.Brush(parameter as string == "accent" ? "AccentSoft" : "TextPrimary");
        // A bad color comes from a hand-edited prompts.json.
        return value is string { Length: > 0 } hex ? Theme.TitleBrush(hex) ?? fallback : fallback;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
