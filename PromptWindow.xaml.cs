using System.Collections.Specialized;
using System.ComponentModel;
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

// A language of the settings panel.
public sealed record LanguageRow(string Code, string Label, bool IsCurrent);

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

    private readonly App _app = (App)System.Windows.Application.Current;
    private readonly ListCollectionView _view;
    // Prompts currently showing a keycap, so a refresh only touches those.
    private readonly List<SavedPrompt> _numbered = new(MaxShortcuts);
    private readonly List<Button> _swatches = [];

    // Edits are saved shortly after you stop typing, not on every keystroke.
    private readonly DispatcherTimer _saveSoon = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private readonly DispatcherTimer _undoExpiry = new() { Interval = TimeSpan.FromSeconds(8) };
    private (SavedPrompt Prompt, int Index)? _deleted;
    private ScreenCapture? _capture;
    // The search text, trimmed once per keystroke instead of once per prompt.
    private string _query = "";

    public PromptWindow()
    {
        InitializeComponent();

        // A view of our own: the filter must not leak into the app-wide collection.
        _view = new ListCollectionView(_app.Prompts) { Filter = Matches };
        PromptList.ItemsSource = _view;

        _app.Prompts.CollectionChanged += Prompts_CollectionChanged;
        foreach (var prompt in _app.Prompts)
            prompt.PropertyChanged += Prompt_PropertyChanged;

        _saveSoon.Tick += (_, _) => { _saveSoon.Stop(); _app.SavePromptsInBackground(); };
        _undoExpiry.Tick += (_, _) => HideUndo();
        SourceInitialized += (_, _) => ConfigureNativeWindow();
        Loc.Changed += OnLanguageChanged;

        RestoreLayout();
        BuildSwatches();
        BuildLanguageRows();
        AttachCapture.IsChecked = _app.PromptAttachScreenshot;

        Select(_app.Prompts.FirstOrDefault(p => p.Id == _app.LastPromptId) ?? _app.Prompts.FirstOrDefault());
        RefreshListState();
        UpdateEditorState();
    }

    private SavedPrompt? Selected => PromptList.SelectedItem as SavedPrompt;

    // ── Called by App on every Ctrl+Alt+G ──

    internal async void Load(string text, ScreenCapture capture)
    {
        // Window already open and nothing selected this time: keep what was
        // being written rather than wiping it.
        if (text.Length > 0 || !IsVisible)
            BodyBox.Text = text;
        SearchBox.Text = "";

        _capture = capture;
        CapturePanel.Visibility = Visibility.Visible;
        CaptureThumb.Background = null;
        CaptureThumb.ToolTip = null;
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
        CaptureThumb.ToolTip = new Image { Source = previews.Preview, Width = 560, Stretch = Stretch.Uniform };
    }

    // Also the ChatGPT window's "Prompt Builder" button: the window comes
    // back as it was left — same prompt, same text, same screenshot.
    internal void Present()
    {
        if (!IsVisible)
        {
            CenterOnCursorScreen();
            Show();
        }
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();

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
        UpdateComposerState();
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
                Background = hex.Length == 0
                    ? (Brush)FindResource("TextPrimary")
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)),
            };
            if (hex.Length == 0) swatch.SetResourceReference(ToolTipProperty, "pb.color.default");
            swatch.Click += Swatch_Click;
            _swatches.Add(swatch);
            TitlePalette.Children.Add(swatch);
        }
    }

    // The current color is ringed in white.
    private void RefreshSwatches()
    {
        var current = Selected?.TitleColor ?? "";
        foreach (var swatch in _swatches)
            swatch.BorderBrush = (string)swatch.Tag == current ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.Transparent;
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

    private void BuildLanguageRows() =>
        LanguageRows.ItemsSource = Loc.Languages.Select(l => new LanguageRow(l.Code, l.Label, l.Code == Loc.Current)).ToList();

    private void OnLanguageChanged()
    {
        BuildLanguageRows();
        foreach (var prompt in _app.Prompts) prompt.RefreshDisplayName();
        RefreshListState();
        UpdateComposerState();
        CharCount.Text = BodyBox.Text.Length == 0 ? "" : Loc.Characters(BodyBox.Text.Length);
        TextSizeValue.Text = PromptBox.FontSize.ToString("0.#", Loc.Culture);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) =>
        SettingsPopup.IsOpen = !SettingsPopup.IsOpen;

    private void Language_Click(object sender, RoutedEventArgs e)
    {
        _app.SetLanguage((string)((Button)sender).Tag);
        SettingsPopup.IsOpen = false;
    }

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
                if (sender == Selected) UpdateComposerState();
                SaveSoon();
                break;
            case nameof(SavedPrompt.TitleColor):
                if (sender == Selected) RefreshSwatches();
                SaveSoon();
                break;
            case nameof(SavedPrompt.Name):
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

    private void CaptureThumb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) =>
        AttachCapture.IsChecked = AttachCapture.IsChecked != true;

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

        // "Nouveau prompt" then nothing typed: not worth keeping.
        foreach (var blank in _app.Prompts.Where(p => string.IsNullOrWhiteSpace(p.Name) && string.IsNullOrWhiteSpace(p.Prompt)).ToList())
            _app.Prompts.Remove(blank);
        _saveSoon.Stop();
        HideUndo();

        _app.LastPromptId = Selected?.Id ?? _app.LastPromptId;
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

// "#RRGGBB" → brush, for a prompt's title color. "" falls back to the
// default text color, or to the accent with ConverterParameter=accent (the
// list's selection bar). Brushes are frozen and cached: the list asks for the
// same few colors over and over.
public sealed class TitleBrushConverter : IValueConverter
{
    private static readonly Dictionary<string, SolidColorBrush> Cache = [];
    private static readonly SolidColorBrush Text = Frozen(Color.FromRgb(0xE8, 0xE8, 0xE8));
    private static readonly SolidColorBrush Accent = Frozen(Color.FromRgb(0x60, 0xA5, 0xFA));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var fallback = parameter as string == "accent" ? Accent : Text;
        if (value is not string hex || hex.Length == 0) return fallback;
        if (Cache.TryGetValue(hex, out var brush)) return brush;
        try
        {
            brush = Frozen((Color)ColorConverter.ConvertFromString(hex));
        }
        catch (FormatException)
        {
            return fallback; // hand-edited prompts.json with a bad color
        }
        Cache[hex] = brush;
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
