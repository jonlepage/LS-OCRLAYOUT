using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Media.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using BitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;
using WinRtBitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;

namespace ScreenSearchOverlay;

public partial class MainWindow : Window
{
    private readonly List<OcrWordInfo> _ocrWords = [];
    private int _historyIndex = -1;
    private bool _isDragging;
    private System.Windows.Point _dragOffset;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Hide window briefly to take a clean screenshot
        Opacity = 0;
        await Task.Delay(150);

        // Get the screen where the mouse cursor is
        GetCursorPos(out var cursorPos);
        var hMonitor = MonitorFromPoint(cursorPos, MONITOR_DEFAULTTONEAREST);
        var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMonitor, ref monitorInfo);

        var screenBounds = monitorInfo.rcMonitor;
        int screenX = screenBounds.Left;
        int screenY = screenBounds.Top;
        int screenW = screenBounds.Right - screenBounds.Left;
        int screenH = screenBounds.Bottom - screenBounds.Top;

        // Position this window exactly on that monitor
        Left = screenX;
        Top = screenY;
        Width = screenW;
        Height = screenH;

        var screenshot = CaptureScreen(screenX, screenY, screenW, screenH);
        ScreenshotImage.Source = ConvertToWpfBitmap(screenshot);

        Opacity = 1;

        // Run OCR
        await RunOcrAsync(screenshot);

        // Apply saved search bar size and position
        ApplySearchBarSize();
        ApplySearchBarPosition();

        // Force focus on search box
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private static Bitmap CaptureScreen(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        return bmp;
    }

    private static BitmapSource ConvertToWpfBitmap(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            return System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    private static Bitmap EnhanceForOcr(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var enhanced = new Bitmap(width, height);

        // Lock bits for fast pixel access
        var srcData = source.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var dstData = enhanced.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        int bytes = Math.Abs(srcData.Stride) * height;
        var srcPixels = new byte[bytes];
        var dstPixels = new byte[bytes];
        Marshal.Copy(srcData.Scan0, srcPixels, 0, bytes);

        // Pass 1: find min/max luminance for histogram stretch
        byte min = 255, max = 0;
        for (int i = 0; i < bytes; i += 4)
        {
            byte gray = (byte)(srcPixels[i] * 0.114 + srcPixels[i + 1] * 0.587 + srcPixels[i + 2] * 0.299);
            if (gray < min) min = gray;
            if (gray > max) max = gray;
        }

        // Pass 2: convert to grayscale + stretch contrast
        float range = max - min;
        if (range < 1) range = 1;

        for (int i = 0; i < bytes; i += 4)
        {
            byte gray = (byte)(srcPixels[i] * 0.114 + srcPixels[i + 1] * 0.587 + srcPixels[i + 2] * 0.299);
            byte stretched = (byte)((gray - min) / range * 255);
            dstPixels[i] = stretched;     // B
            dstPixels[i + 1] = stretched; // G
            dstPixels[i + 2] = stretched; // R
            dstPixels[i + 3] = 255;       // A
        }

        Marshal.Copy(dstPixels, 0, dstData.Scan0, bytes);
        source.UnlockBits(srcData);
        enhanced.UnlockBits(dstData);

        return enhanced;
    }

    private async Task RunOcrAsync(Bitmap screenshot)
    {
        using var enhanced = EnhanceForOcr(screenshot);
        using var memStream = new MemoryStream();
        enhanced.Save(memStream, System.Drawing.Imaging.ImageFormat.Bmp);
        memStream.Seek(0, SeekOrigin.Begin);

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(memStream.ToArray());
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);

        var decoder = await WinRtBitmapDecoder.CreateAsync(stream);
        var softwareBitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var scaleX = ActualWidth / softwareBitmap.PixelWidth;
        var scaleY = ActualHeight / softwareBitmap.PixelHeight;

        _ocrWords.Clear();

        var app = (App)System.Windows.Application.Current;
        var languages = app.SelectedOcrLanguages;

        // Run OCR for each selected language in parallel
        var tasks = new List<Task<OcrResult>>();
        foreach (var lang in languages)
        {
            var engine = OcrEngine.TryCreateFromLanguage(lang);
            if (engine != null)
                tasks.Add(engine.RecognizeAsync(softwareBitmap).AsTask());
        }

        if (tasks.Count == 0) return;

        var results = await Task.WhenAll(tasks);

        // Merge results from all languages, deduplicate by position
        foreach (var result in results)
        {
            foreach (var line in result.Lines)
            {
                foreach (var word in line.Words)
                {
                    var bounds = new Rect(
                        word.BoundingRect.X * scaleX,
                        word.BoundingRect.Y * scaleY,
                        word.BoundingRect.Width * scaleX,
                        word.BoundingRect.Height * scaleY);

                    // Skip if we already have a word at roughly the same position
                    bool duplicate = false;
                    foreach (var existing in _ocrWords)
                    {
                        if (Math.Abs(existing.Bounds.X - bounds.X) < 5 &&
                            Math.Abs(existing.Bounds.Y - bounds.Y) < 5)
                        {
                            duplicate = true;
                            break;
                        }
                    }

                    if (!duplicate)
                    {
                        _ocrWords.Add(new OcrWordInfo
                        {
                            Text = word.Text,
                            Bounds = bounds
                        });
                    }
                }
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        HighlightCanvas.Children.Clear();

        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            BottomCountLabel.Text = "";
            return;
        }

        // Compile regex once if regex mode is active
        Regex? compiledRegex = null;
        if (RegexToggle?.IsChecked == true)
        {
            try
            {
                compiledRegex = new Regex(query, RegexOptions.IgnoreCase);
            }
            catch (RegexParseException)
            {
                BottomCountLabel.Text = "invalid regex";
                BottomCountLabel.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(170, 255, 80, 80));
                return;
            }
        }

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var (fill, stroke, thickness, padX, padY) = GetHighlightStyle();

        int matchCount = 0;
        foreach (var word in _ocrWords)
        {
            bool isMatch = compiledRegex != null
                ? compiledRegex.IsMatch(word.Text)
                : word.Text.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (isMatch)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = word.Bounds.Width + padX,
                    Height = word.Bounds.Height + padY,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = thickness,
                    Opacity = 0,
                    Tag = word.Text,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                rect.MouseLeftButtonDown += Rect_Click;
                Canvas.SetLeft(rect, word.Bounds.X - padX / 2.0);
                Canvas.SetTop(rect, word.Bounds.Y - padY / 2.0);
                HighlightCanvas.Children.Add(rect);
                rect.BeginAnimation(OpacityProperty, fadeIn);
                matchCount++;
            }
        }

        BottomCountLabel.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(170, 255, 255, 0));
        var text = matchCount > 0 ? $"{matchCount} match{(matchCount > 1 ? "es" : "")}" : "no match";
        BottomCountLabel.Text = text;
    }

    private void Rect_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Shapes.Rectangle rect || rect.Tag is not string text)
            return;

        System.Windows.Clipboard.SetText(text);

        var originalFill = rect.Fill;
        rect.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 255, 100));

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        timer.Tick += (_, _) =>
        {
            rect.Fill = originalFill;
            timer.Stop();
        };
        timer.Start();
        e.Handled = true;
    }

    private void RegexToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (RegexToggle.IsChecked == true)
        {
            RegexToggle.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(80, 255, 255, 0));
            RegexToggle.BorderBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(200, 255, 220, 0));
        }
        else
        {
            RegexToggle.Background = System.Windows.Media.Brushes.Transparent;
            RegexToggle.BorderBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(102, 255, 255, 255));
        }
        SearchBox_TextChanged(SearchBox, null!);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Style = null;

        // Copy all screen text
        var copyAll = new System.Windows.Controls.MenuItem { Header = "Copy all screen text" };
        copyAll.IsEnabled = _ocrWords.Count > 0;
        copyAll.Click += (_, _) =>
        {
            var allText = string.Join(" ", _ocrWords.Select(w => w.Text));
            System.Windows.Clipboard.SetText(allText);
            BottomCountLabel.Text = "all text copied";
        };
        menu.Items.Add(copyAll);

        // Copy highlighted text
        var copyHighlighted = new System.Windows.Controls.MenuItem { Header = "Copy highlighted text" };
        var query = SearchBox.Text.Trim();
        copyHighlighted.IsEnabled = !string.IsNullOrEmpty(query) && HighlightCanvas.Children.Count > 0;
        copyHighlighted.Click += (_, _) =>
        {
            var matchedWords = GetMatchedWords();
            System.Windows.Clipboard.SetText(string.Join(" ", matchedWords));
            BottomCountLabel.Text = "highlighted text copied";
        };
        menu.Items.Add(copyHighlighted);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Clear history
        var clearHistory = new System.Windows.Controls.MenuItem { Header = "Clear history" };
        clearHistory.IsEnabled = app.SearchHistory.Count > 0;
        clearHistory.Click += (_, _) =>
        {
            app.SearchHistory.Clear();
            app.AddToSearchHistory(""); // triggers save with empty (clears file)
            app.SearchHistory.Clear();
            BottomCountLabel.Text = "history cleared";
        };
        menu.Items.Add(clearHistory);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Zen mode
        var zenMode = new System.Windows.Controls.MenuItem
        {
            Header = "Zen mode",
            IsCheckable = true,
            IsChecked = app.ZenMode
        };
        zenMode.Click += (_, _) =>
        {
            app.ZenMode = zenMode.IsChecked;
            app.SaveSettings();
            SearchBox_TextChanged(SearchBox, null!);
        };
        menu.Items.Add(zenMode);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Highlight size
        var highlightSizeMenu = new System.Windows.Controls.MenuItem { Header = "Highlight size" };
        foreach (var size in new[] { "large", "medium", "small" })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = size,
                IsCheckable = true,
                IsChecked = app.BoxSize == size
            };
            var capturedSize = size;
            item.Click += (_, _) =>
            {
                app.BoxSize = capturedSize;
                app.SaveSettings();
                SearchBox_TextChanged(SearchBox, null!);
            };
            highlightSizeMenu.Items.Add(item);
        }
        menu.Items.Add(highlightSizeMenu);

        // Search bar size
        var searchBarMenu = new System.Windows.Controls.MenuItem { Header = "Search bar size" };
        foreach (var size in new[] { "large", "medium", "small", "tiny" })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = size,
                IsCheckable = true,
                IsChecked = app.SearchBarSize == size
            };
            var capturedSize = size;
            item.Click += (_, _) =>
            {
                app.SearchBarSize = capturedSize;
                app.SaveSettings();
                ApplySearchBarSize();
            };
            searchBarMenu.Items.Add(item);
        }
        menu.Items.Add(searchBarMenu);

        menu.PlacementTarget = MenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private List<string> GetMatchedWords()
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return [];

        Regex? compiledRegex = null;
        if (RegexToggle?.IsChecked == true)
        {
            try { compiledRegex = new Regex(query, RegexOptions.IgnoreCase); }
            catch { return []; }
        }

        return _ocrWords
            .Where(w => compiledRegex != null
                ? compiledRegex.IsMatch(w.Text)
                : w.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.Text)
            .ToList();
    }

    private (SolidColorBrush fill, SolidColorBrush stroke, double thickness, int padX, int padY) GetHighlightStyle()
    {
        var app = (App)System.Windows.Application.Current;

        // Highlight padding
        var (padX, padY) = app.BoxSize switch
        {
            "large" => (12, 10),
            "medium" => (8, 6),
            "small" => (4, 3),
            _ => (8, 6)
        };

        if (app.ZenMode)
        {
            return (
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 255, 255, 255)),
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 180, 180, 180)),
                1,
                padX, padY
            );
        }

        return (
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 0)),
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 255, 220, 0)),
            2,
            padX, padY
        );
    }

    private void ApplySearchBarPosition()
    {
        var app = (App)System.Windows.Application.Current;
        var x = app.SearchBarX;
        var y = app.SearchBarY;

        // Default: center top
        if (x < 0 || y < 0)
        {
            SearchBarBorder.UpdateLayout();
            x = (ActualWidth - SearchBarBorder.ActualWidth) / 2;
            y = 30;
        }

        // Clamp to screen bounds using actual bar size
        SearchBarBorder.UpdateLayout();
        var scale = (SearchBarBorder.LayoutTransform as ScaleTransform)?.ScaleX ?? 1.0;
        var barW = SearchBarBorder.ActualWidth * scale;
        var barH = SearchBarBorder.ActualHeight * scale;
        x = Math.Max(0, Math.Min(x, ActualWidth - barW));
        y = Math.Max(0, Math.Min(y, ActualHeight - barH));

        SearchBarBorder.Margin = new Thickness(x, y, 0, 0);
    }

    private void SearchBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Don't drag if clicking on interactive elements
        if (e.OriginalSource is System.Windows.Controls.TextBox
            or System.Windows.Controls.Primitives.ButtonBase)
            return;

        _isDragging = true;
        _dragOffset = e.GetPosition(SearchBarBorder);
        SearchBarBorder.CaptureMouse();
        e.Handled = true;
    }

    private void SearchBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        SearchBarBorder.ReleaseMouseCapture();

        // Save position
        var app = (App)System.Windows.Application.Current;
        app.SearchBarX = SearchBarBorder.Margin.Left;
        app.SearchBarY = SearchBarBorder.Margin.Top;
        app.SaveSettings();
        e.Handled = true;
    }

    private void SearchBar_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging) return;

        var pos = e.GetPosition(RootGrid);
        var newX = pos.X - _dragOffset.X;
        var newY = pos.Y - _dragOffset.Y;

        // Clamp to keep the bar fully visible
        var barW = SearchBarBorder.ActualWidth * (SearchBarBorder.LayoutTransform as ScaleTransform)?.ScaleX ?? SearchBarBorder.ActualWidth;
        var barH = SearchBarBorder.ActualHeight * (SearchBarBorder.LayoutTransform as ScaleTransform)?.ScaleY ?? SearchBarBorder.ActualHeight;
        newX = Math.Max(0, Math.Min(newX, ActualWidth - barW));
        newY = Math.Max(0, Math.Min(newY, ActualHeight - barH));

        SearchBarBorder.Margin = new Thickness(newX, newY, 0, 0);
    }

    private void ApplySearchBarSize()
    {
        var app = (App)System.Windows.Application.Current;
        var scale = app.SearchBarSize switch
        {
            "large" => 1.4,
            "medium" => 1.0,
            "small" => 0.75,
            "tiny" => 0.55,
            _ => 1.0
        };
        SearchBarBorder.LayoutTransform = new ScaleTransform(scale, scale);
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var history = app.SearchHistory;

        if (e.Key == Key.Up && history.Count > 0)
        {
            _historyIndex = Math.Min(_historyIndex + 1, history.Count - 1);
            SearchBox.Text = history[_historyIndex];
            SearchBox.CaretIndex = SearchBox.Text.Length;
            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            _historyIndex--;
            if (_historyIndex < 0)
            {
                _historyIndex = -1;
                SearchBox.Text = "";
            }
            else
            {
                SearchBox.Text = history[_historyIndex];
                SearchBox.CaretIndex = SearchBox.Text.Length;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var query = SearchBox.Text.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                app.AddToSearchHistory(query);
                _historyIndex = -1;
            }
            e.Handled = true;
        }
        else if (e.Key != Key.Escape)
        {
            _historyIndex = -1;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            var app = (App)System.Windows.Application.Current;
            var query = SearchBox.Text.Trim();
            if (!string.IsNullOrEmpty(query))
                app.AddToSearchHistory(query);
            Close();
        }
    }

    #region Native interop

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    #endregion
}

internal class OcrWordInfo
{
    public required string Text { get; init; }
    public Rect Bounds { get; init; }
}
