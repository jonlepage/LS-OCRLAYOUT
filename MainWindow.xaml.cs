using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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

    private async Task RunOcrAsync(Bitmap screenshot)
    {
        using var memStream = new MemoryStream();
        screenshot.Save(memStream, System.Drawing.Imaging.ImageFormat.Bmp);
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

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine == null) return;

        var result = await engine.RecognizeAsync(softwareBitmap);

        var scaleX = ActualWidth / softwareBitmap.PixelWidth;
        var scaleY = ActualHeight / softwareBitmap.PixelHeight;

        _ocrWords.Clear();
        foreach (var line in result.Lines)
        {
            foreach (var word in line.Words)
            {
                _ocrWords.Add(new OcrWordInfo
                {
                    Text = word.Text,
                    Bounds = new Rect(
                        word.BoundingRect.X * scaleX,
                        word.BoundingRect.Y * scaleY,
                        word.BoundingRect.Width * scaleX,
                        word.BoundingRect.Height * scaleY)
                });
            }
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        HighlightCanvas.Children.Clear();

        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            CountLabel.Text = "";
            BottomCountLabel.Text = "";
            return;
        }

        int matchCount = 0;
        foreach (var word in _ocrWords)
        {
            if (word.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = word.Bounds.Width + 8,
                    Height = word.Bounds.Height + 6,
                    Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 0)),
                    Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 255, 220, 0)),
                    StrokeThickness = 2,
                };
                Canvas.SetLeft(rect, word.Bounds.X - 4);
                Canvas.SetTop(rect, word.Bounds.Y - 3);
                HighlightCanvas.Children.Add(rect);
                matchCount++;
            }
        }

        var text = matchCount > 0 ? $"{matchCount} match{(matchCount > 1 ? "es" : "")}" : "no match";
        CountLabel.Text = text;
        BottomCountLabel.Text = text;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
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
