using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;

namespace ScreenSearchOverlay;

// Translate mode: every OCR line goes out in ONE indexed batch, and each
// translation comes back at the same index — which maps it straight to the
// line's screen rect. The translated text is drawn on an opaque box painted
// with the background color around the original text. Left-click anywhere
// outside the search bar closes the overlay and hands the app back.
public partial class MainWindow : Window
{
    // Static: the translation cache outlives the overlay window, so
    // reopening the same software screen is instant.
    private static readonly ITranslator Translator = new GoogleTranslator();

    private const double TranslationPadX = 3;
    private const double TranslationPadY = 2;
    private const double MinTranslationFontSize = 9;
    // How much wider than the source text a translation may grow before its
    // font shrinks (French is much longer than Japanese).
    private const double MaxTranslationGrowth = 1.3;
    // Bounds of ink height / font size: 0.5 ≈ lowercase only ("commencer"),
    // ~0.9 for kanji; outside means the OCR text is garbage.
    private const double MinInkRatio = 0.5;
    private const double MaxInkRatio = 1.2;
    // Distance (screen px) of the sampled ring outside each OCR box
    private const int BackgroundRingOffset = 3;

    private bool _translateMode;
    // Bumped whenever the displayed translation becomes stale (toggle off,
    // new scroll frame, new request): in-flight results check it and bail.
    private int _translateGeneration;
    private bool _leftClickArmed;

    private void TranslateToggle_Changed(object sender, RoutedEventArgs e)
    {
        _translateMode = TranslateToggle.IsChecked == true;

        if (_translateMode)
        {
            TranslateToggle.Background = new SolidColorBrush(Color.FromArgb(80, 80, 160, 255));
            TranslateToggle.BorderBrush = new SolidColorBrush(Color.FromArgb(200, 110, 180, 255));
        }
        else
        {
            TranslateToggle.Background = System.Windows.Media.Brushes.Transparent;
            TranslateToggle.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
        }

        DimRect.Visibility = _translateMode ? Visibility.Hidden : Visibility.Visible;
        HighlightCanvas.Visibility = _translateMode ? Visibility.Hidden : Visibility.Visible;

        if (!_translateMode)
        {
            ClearTranslation();
            // Back to search: the match count must not inherit a faded label
            BottomCountLabel.BeginAnimation(OpacityProperty, null);
        }

        RefreshOverlayContent();
    }

    // Re-applies whatever the current mode shows, after an OCR pass or toggle
    private void RefreshOverlayContent()
    {
        if (_translateMode)
            _ = RefreshTranslationAsync();
        else
            SearchBox_TextChanged(SearchBox, null!);
    }

    private void ClearTranslation()
    {
        _translateGeneration++;
        TranslationCanvas.Children.Clear();
    }

    private async Task RefreshTranslationAsync()
    {
        ClearTranslation();
        var generation = _translateGeneration;
        SetStatus("translating…", Color.FromArgb(170, 255, 255, 255));

        try
        {
            // Button clicked before the first OCR pass finished
            await _ocrTask;
            if (generation != _translateGeneration) return;

            var lines = _ocrLines.Where(l => TranslationFilter.IsWorthTranslating(l.Text)).ToList();
            if (lines.Count == 0)
            {
                SetStatus("no text found", Color.FromArgb(170, 255, 255, 0), fadeOut: true);
                return;
            }

            // Index i in → index i out: lines[i] ↔ translations[i]
            var target = ((App)System.Windows.Application.Current).TranslateTarget;
            var translations = await Translator.TranslateAsync(
                lines.Select(l => l.Text).ToList(), target, CancellationToken.None);
            if (generation != _translateGeneration) return;

            // Lines already in the target language, or returned unchanged,
            // keep the real screen pixels: no box at all.
            var shown = Enumerable.Range(0, lines.Count)
                .Where(i => TranslationFilter.ChangesAnything(lines[i].Text, translations[i], target))
                .ToList();
            if (shown.Count == 0)
            {
                SetStatus("nothing to translate", Color.FromArgb(170, 255, 255, 0), fadeOut: true);
                return;
            }

            // Sampled on the UI thread, after the await: _screenshot is only
            // swapped here, and a swap bumps the generation (checked above),
            // so these pixels are the ones the lines were read from.
            var backgrounds = SampleBackgroundColors(shown.Select(i => lines[i]).ToList());
            var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
            for (int k = 0; k < shown.Count; k++)
            {
                var i = shown[k];
                var block = CreateTranslationBlock(lines[i], translations[i].Text, backgrounds[k], pixelsPerDip);
                TranslationCanvas.Children.Add(block);
                block.BeginAnimation(OpacityProperty, fadeIn);
            }

            SetStatus($"{shown.Count} line{(shown.Count > 1 ? "s" : "")} translated — click anywhere to close",
                Color.FromArgb(170, 255, 255, 255), fadeOut: true);
        }
        catch (Exception ex)
        {
            Log($"translate EX: {ex}");
            if (generation == _translateGeneration)
                SetStatus($"translation failed: {ex.Message}", Color.FromArgb(170, 255, 80, 80));
        }
    }

    private FrameworkElement CreateTranslationBlock(OcrLineInfo line, string text, Color background, double pixelsPerDip)
    {
        var bounds = line.Bounds;
        var typeface = new Typeface(new System.Windows.Media.FontFamily("Segoe UI"),
            FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

        // Font size that reproduces the source glyphs: the OCR box hugs the
        // ink, and the ink height depends on the letters — "commencer" has
        // no capital nor ascender, so its box is a third shorter than
        // "Guide" at the same font size. Measuring the ink height of the OCR
        // text itself gives the ratio between box height and font size.
        var reference = new FormattedText(line.Text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            typeface, 100, System.Windows.Media.Brushes.Black, pixelsPerDip);
        var inkRatio = Math.Clamp(reference.Extent / 100, MinInkRatio, MaxInkRatio);
        var fontSize = Math.Max(MinTranslationFontSize, bounds.Height / inkRatio);

        // Then shrink only when the translation would grow too far past the
        // original text.
        var textWidth = MeasureTextWidth(text, typeface, fontSize, pixelsPerDip);
        var maxWidth = bounds.Width * MaxTranslationGrowth;
        if (textWidth > maxWidth)
        {
            fontSize = Math.Max(MinTranslationFontSize, fontSize * maxWidth / textWidth);
            textWidth = MeasureTextWidth(text, typeface, fontSize, pixelsPerDip);
        }

        // Perceived luminance (BT.601) picks a readable text color
        var luminance = 0.299 * background.R + 0.587 * background.G + 0.114 * background.B;
        var foreground = luminance > 140 ? Colors.Black : Colors.White;

        var block = new Border
        {
            Background = new SolidColorBrush(background),
            Width = Math.Max(bounds.Width, textWidth) + TranslationPadX * 2,
            MinHeight = bounds.Height + TranslationPadY * 2,
            Padding = new Thickness(TranslationPadX, 0, TranslationPadX, 0),
            // Hover shows what the OCR read, to judge a doubtful translation
            ToolTip = line.Text,
            Opacity = 0,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = typeface.FontFamily,
                FontSize = fontSize,
                Foreground = new SolidColorBrush(foreground),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,
            },
        };
        Canvas.SetLeft(block, bounds.X - TranslationPadX);
        Canvas.SetTop(block, bounds.Y - TranslationPadY);
        return block;
    }

    private static double MeasureTextWidth(string text, Typeface typeface, double fontSize, double pixelsPerDip)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, System.Windows.FlowDirection.LeftToRight,
            typeface, fontSize, System.Windows.Media.Brushes.Black, pixelsPerDip);
        return formatted.WidthIncludingTrailingWhitespace;
    }

    // Background under each line = most frequent color on a thin ring just
    // outside its OCR box. Glyphs rarely touch that ring, so the dominant
    // color (quantized to 4 bits per channel, then averaged inside the
    // winning bucket) is the surface the text is printed on.
    private unsafe Color[] SampleBackgroundColors(List<OcrLineInfo> lines)
    {
        var colors = new Color[lines.Count];
        var bmp = _screenshot;
        int width = bmp.Width;
        int height = bmp.Height;
        var scaleX = width / ActualWidth;
        var scaleY = height / ActualHeight;

        var data = bmp.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            byte* scan0 = (byte*)data.Scan0;
            int stride = data.Stride;
            var buckets = new Dictionary<int, (int Count, long R, long G, long B)>();

            for (int i = 0; i < lines.Count; i++)
            {
                var r = lines[i].Bounds;
                int x0 = Math.Clamp((int)(r.Left * scaleX) - BackgroundRingOffset, 0, width - 1);
                int x1 = Math.Clamp((int)Math.Ceiling(r.Right * scaleX) + BackgroundRingOffset, 0, width - 1);
                int y0 = Math.Clamp((int)(r.Top * scaleY) - BackgroundRingOffset, 0, height - 1);
                int y1 = Math.Clamp((int)Math.Ceiling(r.Bottom * scaleY) + BackgroundRingOffset, 0, height - 1);

                buckets.Clear();
                for (int x = x0; x <= x1; x++)
                {
                    AddColorSample(buckets, scan0 + y0 * stride + x * 4);
                    AddColorSample(buckets, scan0 + y1 * stride + x * 4);
                }
                for (int y = y0; y <= y1; y++)
                {
                    AddColorSample(buckets, scan0 + y * stride + x0 * 4);
                    AddColorSample(buckets, scan0 + y * stride + x1 * 4);
                }

                var best = buckets.Values.MaxBy(b => b.Count);
                colors[i] = Color.FromRgb(
                    (byte)(best.R / best.Count),
                    (byte)(best.G / best.Count),
                    (byte)(best.B / best.Count));
            }
        }
        finally
        {
            bmp.UnlockBits(data);
        }
        return colors;
    }

    // p points to one BGRA pixel
    private static unsafe void AddColorSample(Dictionary<int, (int Count, long R, long G, long B)> buckets, byte* p)
    {
        int key = ((p[2] >> 4) << 8) | ((p[1] >> 4) << 4) | (p[0] >> 4);
        buckets.TryGetValue(key, out var b);
        buckets[key] = (b.Count + 1, b.R + p[2], b.G + p[1], b.B + p[0]);
    }

    private void SetStatus(string text, Color color, bool fadeOut = false)
    {
        BottomCountLabel.Foreground = new SolidColorBrush(color);
        BottomCountLabel.Text = text;

        // Drop any running fade so a new message shows at full opacity
        BottomCountLabel.BeginAnimation(OpacityProperty, null);
        if (fadeOut)
        {
            // The label sits over the app's own text: show the result
            // briefly, then get out of the way.
            BottomCountLabel.BeginAnimation(OpacityProperty,
                new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400)) { BeginTime = TimeSpan.FromSeconds(2.5) });
        }
    }

    // In translate mode, a left click anywhere outside the search bar closes
    // the overlay. Same Down/Up split as the right click (see
    // Window_PreviewMouseRightButtonDown): consume Down, close on Up, so the
    // app underneath never receives an orphan button release.
    private void Window_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_translateMode) return;
        if (e.OriginalSource is DependencyObject src && IsInsideSearchBar(src)) return;
        _leftClickArmed = true;
        e.Handled = true;
    }

    private void Window_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_leftClickArmed) return;
        _leftClickArmed = false;
        e.Handled = true;
        CloseAndSaveQuery();
    }
}
