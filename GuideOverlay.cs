using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace ScreenSearchOverlay;

// The interactive guide's screen (MCP guide_step): two windows over the live
// app, never activated, so the app keeps the keyboard and the mouse.
// - The marks layer: the whole monitor, click-through. The red ring and the
//   agent's words sit over the real button; the user clicks it for real.
// - The steps panel: top-right (top-left when the ring is under it), the
//   path with its current step, "Esc to quit" and a ✕. It takes its own
//   clicks: a click on it must not fall on the app's own buttons below
//   (the close button lives up there).
// Both are left out of screen captures (Windows 10 2004+), so the agent's
// next capture shows the app alone; on older systems they hide for it.
// Like the search overlay, sized in screen pixels.
internal sealed partial class GuideOverlay
{
    private const double PanelMargin = 8;
    // The marks layer dims the screen lightly: the app stays readable
    private const byte StepDim = 0x55;
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
    private static readonly Brush DoneGreen = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));

    private readonly Window _marks;
    private readonly Canvas _canvas = new();
    private readonly Window _panel;
    private readonly StackPanel _stepList = new() { Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _footer;
    private Rect _ring = Rect.Empty;
    private bool _panelOnLeft;

    internal ScreenInfo Screen { get; }
    // false before Windows 10 2004: hide (SetVisible) around a capture
    internal bool ExcludedFromCapture { get; private set; } = true;

    internal GuideOverlay(ScreenInfo screen, Action quit)
    {
        Screen = screen;
        _marks = NewLayer(clickThrough: true);
        _marks.Left = screen.X;
        _marks.Top = screen.Y;
        _marks.Width = screen.Width;
        _marks.Height = screen.Height;
        _marks.Content = _canvas;

        _footer = new TextBlock { Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
        _panel = NewLayer(clickThrough: false);
        _panel.SizeToContent = SizeToContent.WidthAndHeight;
        _panel.Content = NewPanel(quit);
        _panel.SizeChanged += (_, _) => PlacePanel();

        _marks.Show();
        _panel.Show();
    }

    // Steps before current are done; current's ring at mark (screen pixels
    // from the monitor's corner).
    internal void ShowStep(IReadOnlyList<string> steps, int current, GuidePoint mark)
    {
        var ink = new GuideInk(_canvas, Screen.Width, Screen.Height);
        ink.Clear();
        _ring = GuideInk.RingAround(new Point(mark.X, mark.Y), mark.Width, mark.Height);
        ink.Spotlight([new EllipseGeometry(_ring)], StepDim);
        ink.Ring(_ring);
        if (mark.Label is { } label)
            ink.Bubble(label, _ring);

        ListSteps(steps, current, currentDone: false);
        _footer.Text = Loc.T("guide.progress", current, steps.Count) + " · " + Loc.T("guide.quit");
        PlacePanel();
    }

    // Clicked: the ring goes, the app shows through while the agent works
    // out the next step.
    internal void StepTaken(IReadOnlyList<string> steps, int current)
    {
        _canvas.Children.Clear();
        _ring = Rect.Empty;
        ListSteps(steps, current, currentDone: true);
        _footer.Text = Loc.T("guide.next");
    }

    internal void Finish(IReadOnlyList<string> steps, string? message)
    {
        _canvas.Children.Clear();
        _ring = Rect.Empty;
        ListSteps(steps, steps.Count, currentDone: true);
        _footer.Text = message ?? Loc.T("guide.done");
        _footer.Foreground = DoneGreen;
    }

    // Screen point (the mouse hook's) on the ring
    internal bool HitsTarget(int x, int y) => !_ring.IsEmpty && _ring.Contains(new Point(x - Screen.X, y - Screen.Y));

    internal bool IsOnPanel(int x, int y) =>
        new Rect(_panel.Left, _panel.Top, _panel.ActualWidth, _panel.ActualHeight).Contains(new Point(x, y));

    internal void SetVisible(bool visible)
    {
        _marks.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
        _panel.Visibility = visible ? Visibility.Visible : Visibility.Hidden;
    }

    internal void Close()
    {
        _marks.Close();
        _panel.Close();
    }

    private void ListSteps(IReadOnlyList<string> steps, int current, bool currentDone)
    {
        _stepList.Children.Clear();
        for (var i = 0; i < steps.Count; i++)
        {
            var number = i + 1;
            var done = number < current || (number == current && currentDone);
            var now = number == current && !currentDone;
            var badge = new Border
            {
                Width = 16,
                Height = 16,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = done ? DoneGreen : now ? new SolidColorBrush(GuideInk.Red) : Brushes.Transparent,
                BorderBrush = Muted,
                BorderThickness = new Thickness(done || now ? 0 : 1),
                Child = new TextBlock
                {
                    Text = done ? "✓" : number.ToString(CultureInfo.InvariantCulture),
                    Foreground = done ? new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)) : now ? Brushes.White : Muted,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(badge, Dock.Left);
            var line = new DockPanel { Margin = new Thickness(0, i == 0 ? 0 : 4, 0, 0) };
            line.Children.Add(badge);
            line.Children.Add(GuideInk.NewText(steps[i], 12, now ? FontWeights.SemiBold : FontWeights.Normal,
                now ? Brushes.White : Muted));
            _stepList.Children.Add(line);
        }
    }

    private Border NewPanel(Action quit)
    {
        var close = new TextBlock
        {
            Text = "✕",
            Foreground = Muted,
            FontSize = 12,
            Padding = new Thickness(8, 0, 0, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = Loc.T("guide.quit"),
        };
        close.MouseLeftButtonUp += (_, _) => quit();
        DockPanel.SetDock(close, Dock.Right);
        var header = new DockPanel();
        header.Children.Add(close);
        header.Children.Add(GuideInk.NewText(Loc.T("guide.title"), 13, FontWeights.SemiBold, Brushes.White));

        var body = new StackPanel();
        body.Children.Add(header);
        body.Children.Add(_stepList);
        body.Children.Add(_footer);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x1E)),
            BorderBrush = new SolidColorBrush(GuideInk.Red),
            BorderThickness = new Thickness(1.5),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            MinWidth = 200,
            MaxWidth = 300,
            Child = body,
        };
    }

    // Top-right, out of the way; top-left while the ring would be under it
    private void PlacePanel()
    {
        var width = _panel.ActualWidth;
        var height = _panel.ActualHeight;
        // No ring between steps: stay put rather than jump
        if (!_ring.IsEmpty)
            _panelOnLeft = _ring.IntersectsWith(new Rect(Screen.Width - PanelMargin - width, PanelMargin, width, height));
        _panel.Left = Screen.X + (_panelOnLeft ? PanelMargin : Screen.Width - PanelMargin - width);
        _panel.Top = Screen.Y + PanelMargin;
    }

    private Window NewLayer(bool clickThrough)
    {
        var window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            ResizeMode = ResizeMode.NoResize,
        };
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var style = GetWindowLongW(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLongW(hwnd, GWL_EXSTYLE, clickThrough ? style | WS_EX_TRANSPARENT : style);
            if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
                ExcludedFromCapture = false;
        };
        return window;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    [LibraryImport("user32.dll")]
    private static partial int GetWindowLongW(IntPtr hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    private static partial int SetWindowLongW(IntPtr hWnd, int nIndex, int dwNewLong);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);
}
