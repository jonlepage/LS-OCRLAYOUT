using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenSearchOverlay;

// MainWindow is split across multiple partial files for readability:
//   MainWindow.xaml.cs           — ctor, Loaded, Closed, fields, Log, types
//   MainWindow.Scroll.cs         — scroll state machine + hook handlers
//   MainWindow.Capture.cs        — screen capture, OCR pipeline
//   MainWindow.Search.cs         — search box, regex, menu, highlights
//   MainWindow.SearchBar.cs      — search bar drag, position, size, key nav
//   MainWindow.NativeInterop.cs  — Win32 P/Invoke + helpers
public partial class MainWindow : Window
{
    private readonly List<OcrWordInfo> _ocrWords = [];
    private int _historyIndex = -1;
    private bool _isDragging;
    private System.Windows.Point _dragOffset;
    private Bitmap _screenshot;
    private readonly ScreenInfo _screenInfo;
    // Single WriteableBitmap reused across refreshes — avoids the GC.Collect()
    // pathology of CreateBitmapSourceFromHBitmap (dotnet/wpf #5246).
    private WriteableBitmap _screenshotBitmap = null!;

    // Scroll state — read by Scroll.cs partial
    private readonly System.Threading.Timer _scrollDebounce;
    private readonly LowLevelMouseHook _mouseHook = new();
    private bool _scrollSessionActive;
    // All wheel state lives on the UI thread, so plain int suffices.
    // Incremented on every wheel (PreviewMouseWheel or hook); OnScrollIdle
    // captures it and aborts if it changes during the refresh.
    private int _wheelGeneration;

    // OCR cache: skipping the (expensive) OCR pass when the captured pixels
    // hash to the same value as last time. Sampled FNV-1a, see ComputeHash.
    private ulong _lastScreenshotHash;

    // Diagnostic log is gated by [Conditional("LS_DEBUG_LOG")] on the Log
    // method. Without that symbol defined, the C# compiler removes the call
    // site entirely — including the interpolated string allocation. Enable
    // by adding <DefineConstants>LS_DEBUG_LOG</DefineConstants> to the csproj.

    public MainWindow(Bitmap screenshot, ScreenInfo screenInfo)
    {
        _screenshot = screenshot;
        _screenInfo = screenInfo;

        InitializeComponent();

        // Position window and set image before showing
        Left = screenInfo.X;
        Top = screenInfo.Y;
        Width = screenInfo.Width;
        Height = screenInfo.Height;

        // Allocate the WriteableBitmap once at the screen dimensions; every
        // future refresh writes pixels into the same backing buffer.
        _screenshotBitmap = new WriteableBitmap(
            screenInfo.Width, screenInfo.Height, 96, 96, PixelFormats.Bgra32, null);
        UpdateScreenshotBitmap(screenshot);
        ScreenshotImage.Source = _screenshotBitmap;

        // Apply search bar size and position before render
        ApplySearchBarSize();

        // Thread-pool debounce timer (immune to dispatcher starvation)
        _scrollDebounce = new System.Threading.Timer(
            _ =>
            {
                try { Dispatcher.BeginInvoke(new Action(OnScrollIdle)); }
                catch (Exception ex) { Log($"timer-tick EX: {ex}"); }
            },
            null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);

        // Global hook MUST be installed on a thread that has a message loop —
        // we install it after Loaded so the WPF dispatcher is fully running.
        Loaded += (_, _) =>
        {
            try
            {
                _mouseHook.WheelDetected += OnGlobalWheelDetected;
                _mouseHook.Install();
                Log("mouse hook installed");
            }
            catch (Exception ex) { Log($"hook install EX: {ex}"); }
        };

        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Apply position after layout so ActualWidth is available
        ApplySearchBarPosition();

        // Focus search box
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        // Run OCR in background
        await RunOcrAsync(_screenshot);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        try { _mouseHook.Dispose(); } catch { }
        try { _scrollDebounce.Dispose(); } catch { }
        // Release the GDI bitmap — otherwise the handle leaks until the GC
        // collects the Window (which can be much later).
        try { _screenshot?.Dispose(); } catch { }
        // Make sure we don't leave the window click-through if it somehow lingers
        try { SetClickThrough(false); } catch { }
    }

    // ─── Diagnostic logging ──────────────────────────────────────────────

    private static readonly object _logLock = new();
    private static readonly string _logPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "ls-ocrlayout-scroll.log");

    [System.Diagnostics.Conditional("LS_DEBUG_LOG")]
    private static void Log(string message)
    {
        try
        {
            lock (_logLock)
            {
                System.IO.File.AppendAllText(_logPath,
                    $"{DateTime.Now:HH:mm:ss.fff} [T{Environment.CurrentManagedThreadId:D2}] {message}\n");
            }
        }
        catch { }
    }
}

public record ScreenInfo(int X, int Y, int Width, int Height);

internal class OcrWordInfo
{
    public required string Text { get; init; }
    public Rect Bounds { get; init; }
}
