using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace ScreenSearchOverlay;

internal sealed record CapturePreviews(BitmapSource Thumbnail, BitmapSource Preview);

// The screen under the cursor, and everything derived from it, computed off
// the UI thread. Measured on a 4K screen: capture 100 ms, PNG 120 ms,
// clipboard copy 190 ms — all of which used to run on the UI thread around
// the prompt window's appearance.
//
// The steps form ONE sequential chain: a GDI+ Bitmap must never be read by
// two threads at once ("object is currently in use elsewhere").
internal sealed class ScreenCapture
{
    // Hover preview (560 DIP, sharp up to ~225 %) and footer thumbnail
    // (72 DIP, sharp up to 4×). Downscaling 4K → 1280 → 320 costs ~80 ms,
    // once, in the background, instead of the GPU resampling a 4K texture
    // into a 72 px box on every frame.
    private const int PreviewWidth = 1280;
    private const int ThumbnailWidth = 320;

    private readonly Task<Bitmap?> _full;

    private ScreenCapture(ScreenInfo screen)
    {
        _full = Task.Run(() => Attempt(() => MainWindow.CaptureScreen(screen.X, screen.Y, screen.Width, screen.Height)));
        Previews = _full.ContinueWith(t => t.Result is { } full ? Attempt(() => MakePreviews(full)) : null, TaskScheduler.Default);
        Png = Previews.ContinueWith(_ => _full.Result is { } full ? Attempt(() => EncodePng(full)) : null, TaskScheduler.Default);
    }

    // Starts on a worker thread right away, in parallel with the Ctrl+C.
    internal static ScreenCapture Start() => new(MainWindow.GetCurrentScreenInfo());

    // Completes once the screen is captured: nothing of ours may cover it before.
    internal Task Taken => _full;

    internal Task<CapturePreviews?> Previews { get; }

    // Full resolution. Downscaling for the upload was measured and dropped:
    // on screen content the PNG barely shrinks (0.89 Mo at 4K, 0.77 Mo at
    // 2048 px) and the bicubic pass alone costs 150 ms.
    internal Task<byte[]?> Png { get; }

    // Full resolution into the clipboard, for Win+V. Call it once the
    // selection is copied, so Win+V lists the screenshot above the text.
    //
    // On a short-lived STA thread of its own: the copy takes ~190 ms at 4K
    // and would freeze the window that long on the UI thread. SetImage
    // flushes the data (copy: true), so it outlives the thread.
    internal Task CopyToClipboardAsync() =>
        Png.ContinueWith(_ =>
        {
            if (_full.Result is not { } full) return Task.CompletedTask;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try { Forms.Clipboard.SetImage(full); }
                catch { /* clipboard held by another app: Win+V just won't have it */ }
                finally
                {
                    full.Dispose();
                    done.SetResult();
                }
            })
            {
                IsBackground = true,
                Name = "CaptureClipboard",
            };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return done.Task;
        }, TaskScheduler.Default).Unwrap();

    private static CapturePreviews MakePreviews(Bitmap full)
    {
        using var preview = Scale(full, PreviewWidth);
        using var thumbnail = Scale(preview, ThumbnailWidth);
        return new CapturePreviews(ToBitmapSource(thumbnail), ToBitmapSource(preview));
    }

    private static Bitmap Scale(Bitmap source, int width)
    {
        width = Math.Min(width, source.Width);
        var height = Math.Max(1, (int)Math.Round(source.Height * (double)width / source.Width));
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppRgb);
        using var g = Graphics.FromImage(scaled);
        using var attributes = new ImageAttributes();
        // TileFlipXY: no dark fringe from sampling outside the source edges.
        attributes.SetWrapMode(WrapMode.TileFlipXY);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return scaled;
    }

    // Frozen, so the UI thread can use what a worker thread created.
    private static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        try
        {
            var source = BitmapSource.Create(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgr32, null,
                data.Scan0, data.Stride * bitmap.Height, data.Stride);
            source.Freeze();
            return source;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    // A failed capture (secure desktop, locked session) must not take the
    // prompt window down with it: no screenshot, the text still goes.
    private static T? Attempt<T>(Func<T> work) where T : class
    {
        try { return work(); }
        catch { return null; }
    }
}
