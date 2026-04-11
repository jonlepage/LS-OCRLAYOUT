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
