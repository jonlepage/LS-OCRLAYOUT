using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using Size = System.Windows.Size;

namespace ScreenSearchOverlay;

// Guide mode (MCP): an AI agent shows the user parts of the frozen capture
// it looked at, the rest of the screen dimmed. show_me: one ring around the
// spot to click, drawn like a pen stroke, then rippling, the agent's words in
// a bubble beside it. explain_screen: a numbered frame of its own color
// around each zone, drawn one after the other, its title on a tab, the
// explanations in a legend beside them. No search bar, no OCR; Esc or a
// click closes, as in translate mode.
public partial class MainWindow : Window
{
    // Ring around a single icon or button, in DIPs
    private const double GuideRingSize = 64;
    // Room between a zone the agent sized and its ring
    private const double GuideRingMargin = 16;
    private const double GuideRingThickness = 4;
    // How far each ripple spreads past the ring
    private const double GuideRippleSpread = 36;
    // Room between a zone and its frame, so the stroke covers none of it
    private const double GuideFramePad = 4;
    private const double GuideFrameThickness = 3;
    private const double GuideFrameRadius = 6;
    private const double GuideLabelGap = 12;
    private const double GuideEdgeMargin = 8;
    // Desktop sizes: compact text, like the apps it sits on
    private const double GuideLabelMaxWidth = 340;
    // A legend with no free room dodges the mouse: this far past the screen's
    // middle before it switches side, so it doesn't flicker there
    private const double GuideDodgeBand = 40;
    private static readonly Color GuideColor = Color.FromRgb(255, 59, 48);
    // The zones' colors in their numbers' order: the Prompt Builder's palette
    private static readonly Color[] GuideZoneColors =
    [
        Color.FromRgb(0xF8, 0x71, 0x71), Color.FromRgb(0xFB, 0x92, 0x3C), Color.FromRgb(0xFA, 0xCC, 0x15),
        Color.FromRgb(0x4A, 0xDE, 0x80), Color.FromRgb(0x2D, 0xD4, 0xBF), Color.FromRgb(0x38, 0xBD, 0xF8),
        Color.FromRgb(0x81, 0x8C, 0xF8), Color.FromRgb(0xC0, 0x84, 0xFC), Color.FromRgb(0xF4, 0x72, 0xB6),
    ];
    // Light palette: dark text on it
    private static readonly SolidColorBrush GuideZoneInk = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly TimeSpan GuideDrawTime = TimeSpan.FromMilliseconds(450);
    // Frames start one after the other, in their numbers' order
    private static readonly TimeSpan GuideDrawStagger = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan GuideRippleTime = TimeSpan.FromSeconds(1.4);

    private readonly GuideScene? _guide;
    // explain_screen's legend, and its side when it dodges the mouse
    private Border? _guideLegend;
    private bool _legendOnLeft;

    private void ShowGuide(GuideScene guide)
    {
        SearchBarBorder.Visibility = Visibility.Collapsed;
        DimRect.Visibility = Visibility.Hidden;
        // Opened by an agent, not by a hotkey: Windows hands the keyboard
        // (Esc) to a background program only when pushed. Not the Window
        // overload: it drops Topmost.
        WindowActivation.BringToFront(new WindowInteropHelper(this).Handle);
        Activate();

        // Screen pixels → canvas DIPs, as for the OCR boxes (RunOcrAsync)
        var scaleX = ActualWidth / _screenshot.Width;
        var scaleY = ActualHeight / _screenshot.Height;
        switch (guide)
        {
            case GuidePoint point:
                ShowGuidePoint(point, scaleX, scaleY);
                break;
            case GuideZones zones:
                ShowGuideZones(zones.Zones, scaleX, scaleY);
                break;
        }
    }

    private void ShowGuidePoint(GuidePoint point, double scaleX, double scaleY)
    {
        // A zone gets the ellipse through its corners: √2 times its sides
        var width = point.Width is { } w ? Math.Max(GuideRingSize, w * scaleX * Math.Sqrt(2) + GuideRingMargin) : GuideRingSize;
        var height = point.Height is { } h ? Math.Max(GuideRingSize, h * scaleY * Math.Sqrt(2) + GuideRingMargin) : GuideRingSize;
        var ring = new Rect(point.X * scaleX - width / 2, point.Y * scaleY - height / 2, width, height);
        var color = new SolidColorBrush(GuideColor);

        AddSpotlight([new EllipseGeometry(ring)]);
        AddRipple(ring, color);

        var pen = new Ellipse { Width = ring.Width, Height = ring.Height };
        var a = (ring.Width - GuideRingThickness) / 2;
        var b = (ring.Height - GuideRingThickness) / 2;
        // Ramanujan's perimeter of an ellipse
        var perimeter = Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
        AddPenStroke(pen, ring, GuideColor, GuideRingThickness, perimeter, TimeSpan.Zero);

        if (point.Label is { } label)
        {
            var bubble = NewBubble(color, NewGuideText(label, 14, FontWeights.SemiBold, System.Windows.Media.Brushes.White));
            var size = Measured(bubble);
            // Below the ring, or above it when there is no room
            var top = ring.Bottom + GuideLabelGap;
            if (top + size.Height > ActualHeight - GuideEdgeMargin)
                top = ring.Top - GuideLabelGap - size.Height;
            PlaceOnScreen(bubble, size, (ring.Left + ring.Right - size.Width) / 2, top, TimeSpan.Zero);
        }
    }

    private void ShowGuideZones(IReadOnlyList<GuideZone> zones, double scaleX, double scaleY)
    {
        var frames = zones.Select(zone => new Rect(
            zone.Left * scaleX - GuideFramePad, zone.Top * scaleY - GuideFramePad,
            zone.Width * scaleX + 2 * GuideFramePad, zone.Height * scaleY + 2 * GuideFramePad)).ToList();

        AddSpotlight(frames.Select(frame => (Geometry)new RectangleGeometry(frame, GuideFrameRadius, GuideFrameRadius)));

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var color = GuideZoneColors[i % GuideZoneColors.Length];
            var begin = GuideDrawStagger * i;
            var pen = new Rectangle { Width = frame.Width, Height = frame.Height, RadiusX = GuideFrameRadius, RadiusY = GuideFrameRadius };
            // Rounded corners: four quarter circles instead of four corners
            var perimeter = 2 * (frame.Width + frame.Height) - (8 - 2 * Math.PI) * GuideFrameRadius;
            AddPenStroke(pen, frame, color, GuideFrameThickness, perimeter, begin);
            AddFrameTab(i + 1, zones[i].Title, frame, color, begin);
        }

        AddZoneLegend(zones, frames);
    }

    // Everything dims but the holes (Nonzero: overlapping holes stay holes)
    private void AddSpotlight(IEnumerable<Geometry> holes)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var hole in holes)
            group.Children.Add(hole);
        var spotlight = new Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)),
            Data = new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)), group),
        };
        HighlightCanvas.Children.Add(spotlight);
        spotlight.BeginAnimation(OpacityProperty, FadeIn(TimeSpan.Zero));
    }

    // Ripples spreading out of the ring, once it is drawn
    private void AddRipple(Rect ring, Brush color)
    {
        var ripple = new Ellipse
        {
            Width = ring.Width,
            Height = ring.Height,
            Stroke = color,
            StrokeThickness = 3,
            Opacity = 0,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var spread = new ScaleTransform();
        ripple.RenderTransform = spread;
        Canvas.SetLeft(ripple, ring.Left);
        Canvas.SetTop(ripple, ring.Top);
        HighlightCanvas.Children.Add(ripple);
        spread.BeginAnimation(ScaleTransform.ScaleXProperty, Ripple(1 + 2 * GuideRippleSpread / ring.Width));
        spread.BeginAnimation(ScaleTransform.ScaleYProperty, Ripple(1 + 2 * GuideRippleSpread / ring.Height));
        ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.8, 0, GuideRippleTime)
        {
            BeginTime = GuideDrawTime,
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    // The pen stroke: a single dash at least as long as the outline (in
    // stroke thicknesses), slid into view. It starts hidden: a frame waiting
    // for its turn must not show early.
    private void AddPenStroke(Shape pen, Rect bounds, Color color, double thickness, double perimeter, TimeSpan begin)
    {
        var dash = perimeter / thickness + 1;
        pen.Stroke = new SolidColorBrush(color);
        pen.StrokeThickness = thickness;
        pen.StrokeDashArray = new DoubleCollection { dash, dash };
        pen.StrokeDashCap = PenLineCap.Round;
        pen.StrokeDashOffset = dash;
        pen.Effect = new DropShadowEffect { Color = color, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };
        Canvas.SetLeft(pen, bounds.Left);
        Canvas.SetTop(pen, bounds.Top);
        HighlightCanvas.Children.Add(pen);
        pen.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(dash, 0, GuideDrawTime)
        {
            BeginTime = begin,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    // Number and title on a tab sitting on the frame's top-left edge, or
    // just inside the frame when the screen's top leaves no room above.
    private void AddFrameTab(int number, string title, Rect frame, Color color, TimeSpan begin)
    {
        var text = new TextBlock { Foreground = GuideZoneInk, FontSize = 12, TextWrapping = TextWrapping.NoWrap };
        text.Inlines.Add(new System.Windows.Documents.Run(number.ToString(CultureInfo.InvariantCulture) + "  ") { FontWeight = FontWeights.Black });
        text.Inlines.Add(new System.Windows.Documents.Run(title) { FontWeight = FontWeights.SemiBold });
        var tab = new Border
        {
            Background = new SolidColorBrush(color),
            Padding = new Thickness(6, 1, 6, 2),
            MaxWidth = Math.Max(frame.Width, 120),
            Opacity = 0,
            Child = text,
        };
        var size = Measured(tab);
        var above = frame.Top - size.Height >= 0;
        tab.CornerRadius = above ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0, 0, 4, 0);
        Canvas.SetLeft(tab, Math.Min(frame.Left, ActualWidth - size.Width));
        Canvas.SetTop(tab, above ? frame.Top - size.Height + GuideFrameThickness / 2 : frame.Top);
        HighlightCanvas.Children.Add(tab);
        tab.BeginAnimation(OpacityProperty, FadeIn(begin + GuideDrawTime / 2));
    }

    // One numbered line per zone that has an explanation, beside the group
    // of frames: right of it, else left, else below, else above — the first
    // side with room, so the legend covers none of them. No room anywhere
    // (a maximized app): at the bottom, on the half of the screen the mouse
    // is not on, switching side as the mouse crosses the middle.
    private void AddZoneLegend(IReadOnlyList<GuideZone> zones, List<Rect> frames)
    {
        var lines = new StackPanel();
        for (var i = 0; i < zones.Count; i++)
        {
            if (zones[i].Text is not { } explanation) continue;
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(GuideZoneColors[i % GuideZoneColors.Length]),
                Margin = new Thickness(0, 1, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock
                {
                    Text = (i + 1).ToString(CultureInfo.InvariantCulture),
                    Foreground = GuideZoneInk,
                    FontSize = 11,
                    FontWeight = FontWeights.Black,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            DockPanel.SetDock(badge, Dock.Left);
            var words = new StackPanel();
            words.Children.Add(NewGuideText(zones[i].Title, 13, FontWeights.SemiBold, System.Windows.Media.Brushes.White));
            words.Children.Add(NewGuideText(explanation, 12, FontWeights.Normal,
                new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8))));
            var line = new DockPanel { Margin = new Thickness(0, lines.Children.Count == 0 ? 0 : 6, 0, 0) };
            line.Children.Add(badge);
            line.Children.Add(words);
            lines.Children.Add(line);
        }
        if (lines.Children.Count == 0) return;

        var legend = NewBubble(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), lines);
        _guideLegend = legend;
        var size = Measured(legend);
        var group = frames.Aggregate(Rect.Union);
        var maxX = ActualWidth - GuideEdgeMargin - size.Width;
        var maxY = ActualHeight - GuideEdgeMargin - size.Height;
        var sideTop = Math.Clamp(group.Top, GuideEdgeMargin, Math.Max(GuideEdgeMargin, maxY));
        var stackLeft = Math.Clamp(group.Left, GuideEdgeMargin, Math.Max(GuideEdgeMargin, maxX));
        var begin = GuideDrawStagger * frames.Count;

        Point place;
        if (group.Right + GuideLabelGap <= maxX)
            place = new Point(group.Right + GuideLabelGap, sideTop);
        else if (group.Left - GuideLabelGap - size.Width >= GuideEdgeMargin)
            place = new Point(group.Left - GuideLabelGap - size.Width, sideTop);
        else if (group.Bottom + GuideLabelGap <= maxY)
            place = new Point(stackLeft, group.Bottom + GuideLabelGap);
        else if (group.Top - GuideLabelGap - size.Height >= GuideEdgeMargin)
            place = new Point(stackLeft, group.Top - GuideLabelGap - size.Height);
        else
        {
            _legendOnLeft = System.Windows.Input.Mouse.GetPosition(this).X > ActualWidth / 2;
            PlaceOnScreen(legend, size, LegendLeft(size.Width), maxY, begin);
            MouseMove += DodgeLegend;
            return;
        }
        PlaceOnScreen(legend, size, place.X, place.Y, begin);
    }

    private double LegendLeft(double width) => _legendOnLeft ? GuideEdgeMargin : ActualWidth - GuideEdgeMargin - width;

    private void DodgeLegend(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var x = e.GetPosition(this).X;
        var middle = ActualWidth / 2;
        bool? left = x > middle + GuideDodgeBand ? true : x < middle - GuideDodgeBand ? false : null;
        if (_guideLegend is not { } legend || left is not { } toLeft || toLeft == _legendOnLeft) return;

        _legendOnLeft = toLeft;
        legend.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(LegendLeft(legend.ActualWidth), TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    // A click on the legend keeps the overlay open: it is being read.
    private bool IsInsideGuideLegend(DependencyObject obj) => _guideLegend is { } legend && IsInside(obj, legend);

    private static DoubleAnimation Ripple(double scale) => new(1, scale, GuideRippleTime)
    {
        BeginTime = GuideDrawTime,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
    };

    private static DoubleAnimation FadeIn(TimeSpan begin) =>
        new(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = begin };

    private static Border NewBubble(Brush border, UIElement content) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x1E)),
        BorderBrush = border,
        BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 7, 10, 8),
        MaxWidth = GuideLabelMaxWidth,
        Opacity = 0,
        Child = content,
    };

    private static TextBlock NewGuideText(string text, double size, FontWeight weight, Brush color) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
    };

    private static Size Measured(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    }

    private void PlaceOnScreen(FrameworkElement element, Size size, double left, double top, TimeSpan begin)
    {
        Canvas.SetLeft(element, Math.Max(GuideEdgeMargin, Math.Min(left, ActualWidth - size.Width - GuideEdgeMargin)));
        Canvas.SetTop(element, Math.Max(GuideEdgeMargin, Math.Min(top, ActualHeight - size.Height - GuideEdgeMargin)));
        HighlightCanvas.Children.Add(element);
        element.BeginAnimation(OpacityProperty, FadeIn(begin));
    }
}

// What an agent shows in guide mode, in screen pixels from the monitor's
// top-left corner.
public abstract record GuideScene;

// show_me: a ring around one spot. No Width/Height: a single icon or button.
public sealed record GuidePoint(double X, double Y, double? Width, double? Height, string? Label) : GuideScene;

// explain_screen: numbered frames around parts of the screen.
public sealed record GuideZones(IReadOnlyList<GuideZone> Zones) : GuideScene;

public sealed record GuideZone(double Left, double Top, double Width, double Height, string Title, string? Text);
