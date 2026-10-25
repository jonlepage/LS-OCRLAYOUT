using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace ScreenSearchOverlay;

// What the MCP guide draws on a canvas of a known size, in DIPs: the red
// ring around a spot (a pen stroke, then ripples), the dimmed rest of the
// screen, the bubbles. Shared by the frozen overlay (MainWindow.Guide.cs)
// and the live interactive guide (GuideOverlay).
internal sealed class GuideInk(Canvas canvas, double width, double height)
{
    // Ring around a single icon or button
    private const double RingSize = 64;
    // Room between a zone the agent sized and its ring
    private const double RingMargin = 16;
    private const double RingThickness = 4;
    // How far each ripple spreads past the ring
    private const double RippleSpread = 36;
    internal const double Gap = 12;
    internal const double EdgeMargin = 8;
    // Desktop sizes: compact text, like the apps it sits on
    private const double BubbleMaxWidth = 340;
    internal static readonly Color Red = Color.FromRgb(255, 59, 48);
    internal static readonly TimeSpan DrawTime = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan RippleTime = TimeSpan.FromSeconds(1.4);

    internal double Width => width;
    internal double Height => height;

    // A zone gets the ellipse through its corners: √2 times its sides
    internal static Rect RingAround(Point center, double? zoneWidth, double? zoneHeight)
    {
        var w = zoneWidth is { } zw ? Math.Max(RingSize, zw * Math.Sqrt(2) + RingMargin) : RingSize;
        var h = zoneHeight is { } zh ? Math.Max(RingSize, zh * Math.Sqrt(2) + RingMargin) : RingSize;
        return new Rect(center.X - w / 2, center.Y - h / 2, w, h);
    }

    internal void Add(UIElement element) => canvas.Children.Add(element);

    internal void Clear() => canvas.Children.Clear();

    // Everything dims but the holes (Nonzero: overlapping holes stay holes)
    internal void Spotlight(IEnumerable<Geometry> holes, byte dim)
    {
        var group = new GeometryGroup { FillRule = FillRule.Nonzero };
        foreach (var hole in holes)
            group.Children.Add(hole);
        var spotlight = new Path
        {
            Fill = new SolidColorBrush(Color.FromArgb(dim, 0, 0, 0)),
            Data = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, width, height)), group),
        };
        Add(spotlight);
        spotlight.BeginAnimation(UIElement.OpacityProperty, FadeIn(TimeSpan.Zero));
    }

    // The red ring: drawn like a pen stroke, then rippling
    internal void Ring(Rect ring)
    {
        var color = new SolidColorBrush(Red);
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
        Add(ripple);
        spread.BeginAnimation(ScaleTransform.ScaleXProperty, Ripple(1 + 2 * RippleSpread / ring.Width));
        spread.BeginAnimation(ScaleTransform.ScaleYProperty, Ripple(1 + 2 * RippleSpread / ring.Height));
        ripple.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.8, 0, RippleTime)
        {
            BeginTime = DrawTime,
            RepeatBehavior = RepeatBehavior.Forever,
        });

        var a = (ring.Width - RingThickness) / 2;
        var b = (ring.Height - RingThickness) / 2;
        // Ramanujan's perimeter of an ellipse
        var perimeter = Math.PI * (3 * (a + b) - Math.Sqrt((3 * a + b) * (a + 3 * b)));
        PenStroke(new Ellipse { Width = ring.Width, Height = ring.Height }, ring, Red, RingThickness, perimeter, TimeSpan.Zero);
    }

    // The pen stroke: a single dash at least as long as the outline (in
    // stroke thicknesses), slid into view. It starts hidden: a shape waiting
    // for its turn must not show early.
    internal void PenStroke(Shape pen, Rect bounds, Color color, double thickness, double perimeter, TimeSpan begin)
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
        Add(pen);
        pen.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(dash, 0, DrawTime)
        {
            BeginTime = begin,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
        });
    }

    // The agent's words below the ring, or above it when there is no room
    internal void Bubble(string text, Rect ring)
    {
        var bubble = NewBubble(new SolidColorBrush(Red), NewText(text, 14, FontWeights.SemiBold, Brushes.White));
        var size = Measured(bubble);
        var top = ring.Bottom + Gap;
        if (top + size.Height > height - EdgeMargin)
            top = ring.Top - Gap - size.Height;
        Place(bubble, size, (ring.Left + ring.Right - size.Width) / 2, top, TimeSpan.Zero);
    }

    // Kept on screen, faded in
    internal void Place(FrameworkElement element, Size size, double left, double top, TimeSpan begin)
    {
        Canvas.SetLeft(element, Math.Max(EdgeMargin, Math.Min(left, width - size.Width - EdgeMargin)));
        Canvas.SetTop(element, Math.Max(EdgeMargin, Math.Min(top, height - size.Height - EdgeMargin)));
        Add(element);
        element.BeginAnimation(UIElement.OpacityProperty, FadeIn(begin));
    }

    internal static Border NewBubble(Brush border, UIElement content) => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(0xEE, 0x1E, 0x1E, 0x1E)),
        BorderBrush = border,
        BorderThickness = new Thickness(1.5),
        CornerRadius = new CornerRadius(6),
        Padding = new Thickness(10, 7, 10, 8),
        MaxWidth = BubbleMaxWidth,
        Opacity = 0,
        Child = content,
    };

    internal static TextBlock NewText(string text, double size, FontWeight weight, Brush color) => new()
    {
        Text = text,
        Foreground = color,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.Wrap,
    };

    internal static Size Measured(FrameworkElement element)
    {
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    }

    internal static DoubleAnimation FadeIn(TimeSpan begin) =>
        new(0, 1, TimeSpan.FromMilliseconds(200)) { BeginTime = begin };

    private static DoubleAnimation Ripple(double scale) => new(1, scale, RippleTime)
    {
        BeginTime = DrawTime,
        RepeatBehavior = RepeatBehavior.Forever,
        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
    };
}
