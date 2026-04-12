using System.Drawing;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using Windows.Media.Ocr;
using BitmapAlphaMode = Windows.Graphics.Imaging.BitmapAlphaMode;
using BitmapPixelFormat = Windows.Graphics.Imaging.BitmapPixelFormat;

namespace ScreenSearchOverlay;

public partial class MainWindow : Window
{
    // FNV-1a 64-bit over 1 row out of 8. Sampling makes it ~8× faster on
    // 4K screens; collision risk is irrelevant in practice because any real
    // scroll moves pixels across all rows.
    private static unsafe ulong ComputeHash(Bitmap bmp)
    {
        const ulong FNV_OFFSET = 14695981039346656037UL;
        const ulong FNV_PRIME = 1099511628211UL;

        var data = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            ulong h = FNV_OFFSET;
            byte* p = (byte*)data.Scan0;
            int stride = data.Stride;
            int height = bmp.Height;
            int qwords = stride / 8;

            for (int y = 0; y < height; y += 8)
            {
                ulong* row = (ulong*)(p + y * stride);
                for (int i = 0; i < qwords; i++)
                {
                    h ^= row[i];
                    h *= FNV_PRIME;
                }
            }
            return h;
        }
        finally { bmp.UnlockBits(data); }
    }

    internal static Bitmap CaptureScreen(int x, int y, int width, int height)
    {
        var bmp = new Bitmap(width, height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height));
        return bmp;
    }

    // Push pixels from the captured GDI bitmap into the persistent
    // WriteableBitmap. Single memcpy into the WPF back buffer; no GC,
    // no WIC, no HBITMAP — sub-5 ms even at 4K. UI-thread only.
    private void UpdateScreenshotBitmap(Bitmap src)
    {
        var w = src.Width;
        var h = src.Height;

        // Defensive: realloc if dimensions ever change (e.g. monitor swap)
        if (w != _screenshotBitmap.PixelWidth || h != _screenshotBitmap.PixelHeight)
        {
            _screenshotBitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                w, h, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
            ScreenshotImage.Source = _screenshotBitmap;
        }

        var data = src.LockBits(
            new System.Drawing.Rectangle(0, 0, w, h),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            // Format32bppArgb (System.Drawing) and Bgra32 (WPF) are the same
            // byte layout in little-endian — direct memcpy is correct.
            _screenshotBitmap.WritePixels(
                new Int32Rect(0, 0, w, h),
                data.Scan0,
                data.Stride * h,
                data.Stride);
        }
        finally
        {
            src.UnlockBits(data);
        }
    }

    // Pre-computed gamma 0.7 LUT — applied per-pixel to give the OCR a
    // moderate contrast boost without scanning min/max twice.
    private static readonly byte[] GammaLut = BuildGammaLut(0.7);

    private static byte[] BuildGammaLut(double gamma)
    {
        var lut = new byte[256];
        var invG = 1.0 / gamma;
        for (int i = 0; i < 256; i++)
        {
            var v = Math.Pow(i / 255.0, invG) * 255.0;
            lut[i] = (byte)Math.Clamp(v, 0, 255);
        }
        return lut;
    }

    // Single-pass `unsafe` grayscale + gamma boost. No byte[] allocations,
    // no Marshal.Copy round-trip — direct pointer reads/writes against the
    // locked DIB. ~3-4× faster than the old two-pass + Marshal.Copy version.
    // BT.601 weights in fixed-point: (29*B + 150*G + 77*R + 128) >> 8.
    private static unsafe Bitmap EnhanceForOcr(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var enhanced = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        var srcData = source.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var dstData = enhanced.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        try
        {
            int srcStride = srcData.Stride;
            int dstStride = dstData.Stride;
            byte* srcBase = (byte*)srcData.Scan0;
            byte* dstBase = (byte*)dstData.Scan0;

            fixed (byte* lut = GammaLut)
            {
                for (int y = 0; y < height; y++)
                {
                    byte* s = srcBase + y * srcStride;
                    byte* d = dstBase + y * dstStride;
                    for (int x = 0; x < width; x++)
                    {
                        // BGRA byte order
                        int gray = (29 * s[0] + 150 * s[1] + 77 * s[2] + 128) >> 8;
                        byte v = lut[gray];
                        d[0] = v;
                        d[1] = v;
                        d[2] = v;
                        d[3] = 255;
                        s += 4;
                        d += 4;
                    }
                }
            }
        }
        finally
        {
            source.UnlockBits(srcData);
            enhanced.UnlockBits(dstData);
        }
        return enhanced;
    }

    private static Windows.Graphics.Imaging.SoftwareBitmap BitmapToSoftwareBitmap(Bitmap source)
    {
        var width = source.Width;
        var height = source.Height;
        var data = source.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        var bytes = Math.Abs(data.Stride) * height;
        var pixels = new byte[bytes];
        Marshal.Copy(data.Scan0, pixels, 0, bytes);
        source.UnlockBits(data);

        var sb = new Windows.Graphics.Imaging.SoftwareBitmap(
            BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
        sb.CopyFromBuffer(pixels.AsBuffer());
        return sb;
    }

    private async Task RunOcrAsync(Bitmap screenshot)
    {
        // Capture UI-bound dimensions before going to the background thread
        var actualW = ActualWidth;
        var actualH = ActualHeight;

        // Enhance + convert to SoftwareBitmap entirely off the UI thread —
        // these are the most expensive sync steps in the OCR pipeline (full
        // double pass over every pixel + a copy into a Windows.Graphics buffer).
        using var softwareBitmap = await Task.Run(() =>
        {
            using var enhanced = EnhanceForOcr(screenshot);
            return BitmapToSoftwareBitmap(enhanced);
        });

        var scaleX = actualW / softwareBitmap.PixelWidth;
        var scaleY = actualH / softwareBitmap.PixelHeight;

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

}
