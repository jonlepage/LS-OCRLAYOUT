using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;

namespace ScreenSearchOverlay;

// The MCP screen guide (list_windows, look_at_screen, show_me,
// explain_screen): the agent captures an app's window, sees it, then circles
// a spot or frames zones on that same frozen capture in the overlay — what
// it saw is exactly what the user sees. The step-by-step, live guide
// (guide_step) is App.Interactive.cs.
// Coordinates travel in the pixels of the image the agent received; only the
// last capture is kept.
public partial class App : IScreenGuide
{
    // The API shrinks any image past 1568 px or ~1.15 megapixels; staying
    // under both keeps the agent's coordinates in our image's pixels.
    private const int GuideMaxEdge = 1568;
    private const double GuideMaxPixels = 1_150_000;
    private const int GuideGridStep = 100;
    // A window just brought to the front, or restored, repaints first.
    private const int GuideActivationMs = 350;
    // Our overlay just closed: the compositor drops it from the screen.
    private const int GuideOverlayCloseMs = 100;

    // Screen: the whole monitor at full resolution, for the overlay. Crop:
    // the part the agent saw (the window), in Screen's pixels. Width, Height:
    // the agent's image. ProcessId: the app whose window it is (none: the
    // screen under the mouse).
    private sealed record GuideCapture(Bitmap Screen, ScreenInfo Info, Rectangle Crop, int Width, int Height, uint? ProcessId)
    {
        // Screen pixels per image pixel
        public double ScaleX => Crop.Width / (double)Width;
        public double ScaleY => Crop.Height / (double)Height;

        // The captured window, in desktop coordinates
        public Rectangle Area => new(Info.X + Crop.X, Info.Y + Crop.Y, Crop.Width, Crop.Height);

        public bool Contains(double x, double y) => x >= 0 && y >= 0 && x <= Width && y <= Height;

        // An image point, in screen pixels from the monitor's corner
        public GuidePoint Mark(GuideSpot spot) => new(
            Crop.X + spot.X * ScaleX, Crop.Y + spot.Y * ScaleY, spot.Width * ScaleX, spot.Height * ScaleY,
            string.IsNullOrEmpty(spot.Label) ? null : spot.Label);

        // A desktop point, in the image's pixels; null outside the image
        public System.Drawing.PointF? ImagePoint(System.Drawing.Point desktop) =>
            Area.Contains(desktop)
                ? new((desktop.X - Area.X) / (float)ScaleX, (desktop.Y - Area.Y) / (float)ScaleY)
                : null;
    }

    private const string GuideNoLook = "Call look_at_screen first: the marks are drawn on its image.";

    private GuideCapture? _guideCapture;

    Task<IReadOnlyList<TopWindow>> IScreenGuide.ListWindowsAsync() =>
        Task.Run(() => (IReadOnlyList<TopWindow>)TopWindows.List());

    Task<(ScreenLook? Look, string? Error)> IScreenGuide.LookAsync(string? window, bool grid) =>
        Dispatcher.InvokeAsync(() => LookAsync(window, grid)).Task.Unwrap();

    Task<string?> IScreenGuide.ShowAsync(GuideSpot spot) =>
        Dispatcher.InvokeAsync(() => ShowGuide(spot)).Task;

    Task<string?> IScreenGuide.ExplainAsync(IReadOnlyList<GuideArea> areas) =>
        Dispatcher.InvokeAsync(() => Explain(areas)).Task;

    private async Task<(ScreenLook?, string?)> LookAsync(string? window, bool grid)
    {
        // Never capture our own overlay
        var settle = CloseOverlay() ? GuideOverlayCloseMs : 0;

        TopWindow? target = null;
        if (!string.IsNullOrWhiteSpace(window))
        {
            var windows = TopWindows.List();
            target = TopWindows.Match(windows, window.Trim());
            if (target is null)
                return (null, $"No open window matches \"{window}\". Open windows: "
                    + string.Join("; ", windows.Select(w => $"{w.Title} ({w.Process})")));
            // In front: what covers it would be captured instead.
            if (WindowActivation.BringToFront(target.Handle))
                settle = GuideActivationMs;
        }
        if (settle > 0)
            await Task.Delay(settle);
        return (await CaptureForAgentAsync(target, grid), null);
    }

    // That window, else the monitor under the mouse; it becomes the latest
    // capture. Called after any delay: a restored window has its final place.
    private async Task<ScreenLook> CaptureForAgentAsync(TopWindow? target, bool grid)
    {
        ScreenInfo screen;
        var crop = Rectangle.Empty;
        if (target is null)
            screen = ScreenSearchOverlay.MainWindow.GetCurrentScreenInfo();
        else
        {
            screen = ScreenSearchOverlay.MainWindow.GetWindowScreenInfo(target.Handle);
            // The window on its monitor (a maximized one overflows it by its
            // invisible borders).
            var bounds = ScreenSearchOverlay.MainWindow.GetWindowBounds(target.Handle);
            crop = Rectangle.Intersect(
                new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                new Rectangle(screen.X, screen.Y, screen.Width, screen.Height));
            crop.Offset(-screen.X, -screen.Y);
        }
        if (crop.IsEmpty)
            crop = new Rectangle(0, 0, screen.Width, screen.Height);

        var (capture, png) = await Task.Run(() => Capture(screen, crop, grid, target?.ProcessId));
        _guideCapture?.Screen.Dispose();
        _guideCapture = capture;
        return new ScreenLook(png, capture.Width, capture.Height, target?.Title);
    }

    private string? ShowGuide(GuideSpot spot)
    {
        if (_guideCapture is not { } capture)
            return GuideNoLook;
        if (!capture.Contains(spot.X, spot.Y))
            return GuideOutside(capture);
        return OpenGuide(capture, capture.Mark(spot));
    }

    private static string GuideOutside(GuideCapture capture) =>
        $"x/y fall outside the image ({capture.Width}×{capture.Height} px).";

    private string? Explain(IReadOnlyList<GuideArea> areas)
    {
        if (_guideCapture is not { } capture)
            return GuideNoLook;
        if (areas.FirstOrDefault(a => !capture.Contains(a.Left, a.Top)) is { } outside)
            return $"Zone \"{outside.Title}\" starts outside the image ({capture.Width}×{capture.Height} px).";

        // A zone running past the image's edge stops at it
        var zones = areas.Select(a => new GuideZone(
            capture.Crop.X + a.Left * capture.ScaleX, capture.Crop.Y + a.Top * capture.ScaleY,
            Math.Min(a.Width, capture.Width - a.Left) * capture.ScaleX,
            Math.Min(a.Height, capture.Height - a.Top) * capture.ScaleY,
            a.Title, string.IsNullOrEmpty(a.Text) ? null : a.Text)).ToList();
        return OpenGuide(capture, new GuideZones(zones));
    }

    private string? OpenGuide(GuideCapture capture, GuideScene scene)
    {
        // One guide at a time: a picture replaces a running interactive guide
        StopInteractiveGuide(GuideStepOutcome.Replaced);
        CloseOverlay();
        // A copy: the overlay disposes its screenshot when it closes, and
        // the agent may show other spots on the same capture.
        _overlayWindow = new ScreenSearchOverlay.MainWindow(new Bitmap(capture.Screen), capture.Info, scene);
        _overlayWindow.Closed += (_, _) => _overlayWindow = null;
        _overlayWindow.Show();
        return null;
    }

    // true when one was open
    private bool CloseOverlay()
    {
        if (_overlayWindow is not { IsVisible: true } overlay) return false;
        overlay.Close();
        return true;
    }

    // Worker thread: the capture never touches the UI. The whole monitor is
    // kept for the overlay; the agent gets the crop, as sharp as the limits
    // allow — a window smaller than the screen comes out sharper.
    private static (GuideCapture, byte[]) Capture(ScreenInfo screen, Rectangle crop, bool grid, uint? processId)
    {
        var full = ScreenSearchOverlay.MainWindow.CaptureScreen(screen.X, screen.Y, screen.Width, screen.Height);
        using var window = full.Clone(crop, PixelFormat.Format32bppArgb);
        var fit = Math.Min(1, Math.Min(
            GuideMaxEdge / (double)Math.Max(crop.Width, crop.Height),
            Math.Sqrt(GuideMaxPixels / ((double)crop.Width * crop.Height))));
        using var image = ScreenCapture.Scale(window, (int)(crop.Width * fit));
        if (grid)
            DrawGrid(image);
        return (new GuideCapture(full, screen, crop, image.Width, image.Height, processId), ScreenCapture.EncodePng(image));
    }

    // Faint lines every GuideGridStep pixels, numbered small and see-through
    // along the bottom and right edges, away from title bars and menus:
    // landmarks for the agent's coordinates. Only the agent sees them.
    private static void DrawGrid(Bitmap image)
    {
        using var g = Graphics.FromImage(image);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var line = new Pen(Color.FromArgb(90, 255, 0, 255));
        using var font = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tag = new SolidBrush(Color.FromArgb(110, 0, 0, 0));
        using var ink = new SolidBrush(Color.FromArgb(220, 255, 140, 255));

        void Number(int value, float x, float y, bool alignRight, bool alignBottom)
        {
            var text = value.ToString(CultureInfo.InvariantCulture);
            var size = g.MeasureString(text, font);
            if (alignRight) x -= size.Width;
            if (alignBottom) y -= size.Height;
            g.FillRectangle(tag, x, y, size.Width, size.Height);
            g.DrawString(text, font, ink, x, y);
        }

        for (var x = GuideGridStep; x < image.Width; x += GuideGridStep)
        {
            g.DrawLine(line, x, 0, x, image.Height);
            Number(x, x + 1, image.Height, alignRight: false, alignBottom: true);
        }
        for (var y = GuideGridStep; y < image.Height; y += GuideGridStep)
        {
            g.DrawLine(line, 0, y, image.Width, y);
            Number(y, image.Width, y + 1, alignRight: true, alignBottom: false);
        }
    }
}
