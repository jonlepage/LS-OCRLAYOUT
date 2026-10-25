using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ScreenSearchOverlay;

// Guide mode (MCP): an AI agent shows the user parts of the frozen capture
// it looked at, the rest of the screen dimmed (drawing: GuideInk). show_me:
// one ring around the spot to click, the agent's words in a bubble beside
// it. explain_screen: a numbered frame of its own color around each zone,
// drawn one after the other, its title on a tab, the explanations in a
// legend beside them. No search bar, no OCR; Esc or a click closes, as in
// translate mode. The live, step-by-step guide is GuideOverlay.
public partial class MainWindow : Window
{
    // Room between a zone and its frame, so the stroke covers none of it
    private const double GuideFramePad = 4;
    private const double GuideFrameThickness = 3;
    private const double GuideFrameRadius = 6;
    // A legend with no free room dodges the mouse: this far past the screen's
    // middle before it switches side, so it doesn't flicker there
    private const double GuideDodgeBand = 40;
    // The zones' colors in their numbers' order: the Prompt Builder's palette
    private static readonly Color[] GuideZoneColors =
    [
        Color.FromRgb(0xF8, 0x71, 0x71), Color.FromRgb(0xFB, 0x92, 0x3C), Color.FromRgb(0xFA, 0xCC, 0x15),
        Color.FromRgb(0x4A, 0xDE, 0x80), Color.FromRgb(0x2D, 0xD4, 0xBF), Color.FromRgb(0x38, 0xBD, 0xF8),
        Color.FromRgb(0x81, 0x8C, 0xF8), Color.FromRgb(0xC0, 0x84, 0xFC), Color.FromRgb(0xF4, 0x72, 0xB6),
    ];
    // Light palette: dark text on it
    private static readonly SolidColorBrush GuideZoneInk = new(Color.FromRgb(0x1E, 0x1E, 0x1E));
    // Frames start one after the other, in their numbers' order
    private static readonly TimeSpan GuideDrawStagger = TimeSpan.FromMilliseconds(250);

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
        var ink = new GuideInk(HighlightCanvas, ActualWidth, ActualHeight);
        switch (guide)
        {
            case GuidePoint point:
                var ring = GuideInk.RingAround(new Point(point.X * scaleX, point.Y * scaleY), point.Width * scaleX, point.Height * scaleY);
                ink.Spotlight([new EllipseGeometry(ring)], 0x99);
                ink.Ring(ring);
                if (point.Label is { } label)
                    ink.Bubble(label, ring);
                break;
            case GuideZones zones:
                ShowGuideZones(ink, zones.Zones, scaleX, scaleY);
                break;
        }
    }

    private void ShowGuideZones(GuideInk ink, IReadOnlyList<GuideZone> zones, double scaleX, double scaleY)
    {
        var frames = zones.Select(zone => new Rect(
            zone.Left * scaleX - GuideFramePad, zone.Top * scaleY - GuideFramePad,
            zone.Width * scaleX + 2 * GuideFramePad, zone.Height * scaleY + 2 * GuideFramePad)).ToList();

        ink.Spotlight(frames.Select(frame => (Geometry)new RectangleGeometry(frame, GuideFrameRadius, GuideFrameRadius)), 0x99);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var color = GuideZoneColors[i % GuideZoneColors.Length];
            var begin = GuideDrawStagger * i;
            var pen = new Rectangle { Width = frame.Width, Height = frame.Height, RadiusX = GuideFrameRadius, RadiusY = GuideFrameRadius };
            // Rounded corners: four quarter circles instead of four corners
            var perimeter = 2 * (frame.Width + frame.Height) - (8 - 2 * Math.PI) * GuideFrameRadius;
            ink.PenStroke(pen, frame, color, GuideFrameThickness, perimeter, begin);
            AddFrameTab(ink, i + 1, zones[i].Title, frame, color, begin);
        }

        AddZoneLegend(ink, zones, frames);
    }

    // Number and title on a tab sitting on the frame's top-left edge, or
    // just inside the frame when the screen's top leaves no room above.
    private static void AddFrameTab(GuideInk ink, int number, string title, Rect frame, Color color, TimeSpan begin)
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
        var size = GuideInk.Measured(tab);
        var above = frame.Top - size.Height >= 0;
        tab.CornerRadius = above ? new CornerRadius(4, 4, 0, 0) : new CornerRadius(0, 0, 4, 0);
        Canvas.SetLeft(tab, Math.Min(frame.Left, ink.Width - size.Width));
        Canvas.SetTop(tab, above ? frame.Top - size.Height + GuideFrameThickness / 2 : frame.Top);
        ink.Add(tab);
        tab.BeginAnimation(OpacityProperty, GuideInk.FadeIn(begin + GuideInk.DrawTime / 2));
    }

    // One numbered line per zone that has an explanation, beside the group
    // of frames: right of it, else left, else below, else above — the first
    // side with room, so the legend covers none of them. No room anywhere
    // (a maximized app): at the bottom, on the half of the screen the mouse
    // is not on, switching side as the mouse crosses the middle.
    private void AddZoneLegend(GuideInk ink, IReadOnlyList<GuideZone> zones, List<Rect> frames)
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
            words.Children.Add(GuideInk.NewText(zones[i].Title, 13, FontWeights.SemiBold, System.Windows.Media.Brushes.White));
            words.Children.Add(GuideInk.NewText(explanation, 12, FontWeights.Normal,
                new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8))));
            var line = new DockPanel { Margin = new Thickness(0, lines.Children.Count == 0 ? 0 : 6, 0, 0) };
            line.Children.Add(badge);
            line.Children.Add(words);
            lines.Children.Add(line);
        }
        if (lines.Children.Count == 0) return;

        var legend = GuideInk.NewBubble(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), lines);
        _guideLegend = legend;
        var size = GuideInk.Measured(legend);
        var group = frames.Aggregate(Rect.Union);
        var gap = GuideInk.Gap;
        var margin = GuideInk.EdgeMargin;
        var maxX = ActualWidth - margin - size.Width;
        var maxY = ActualHeight - margin - size.Height;
        var sideTop = Math.Clamp(group.Top, margin, Math.Max(margin, maxY));
        var stackLeft = Math.Clamp(group.Left, margin, Math.Max(margin, maxX));
        var begin = GuideDrawStagger * frames.Count;

        Point place;
        if (group.Right + gap <= maxX)
            place = new Point(group.Right + gap, sideTop);
        else if (group.Left - gap - size.Width >= margin)
            place = new Point(group.Left - gap - size.Width, sideTop);
        else if (group.Bottom + gap <= maxY)
            place = new Point(stackLeft, group.Bottom + gap);
        else if (group.Top - gap - size.Height >= margin)
            place = new Point(stackLeft, group.Top - gap - size.Height);
        else
        {
            _legendOnLeft = System.Windows.Input.Mouse.GetPosition(this).X > ActualWidth / 2;
            ink.Place(legend, size, LegendLeft(size.Width), maxY, begin);
            MouseMove += DodgeLegend;
            return;
        }
        ink.Place(legend, size, place.X, place.Y, begin);
    }

    private double LegendLeft(double width) =>
        _legendOnLeft ? GuideInk.EdgeMargin : ActualWidth - GuideInk.EdgeMargin - width;

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
}

// What an agent shows in guide mode, in screen pixels from the monitor's
// top-left corner.
public abstract record GuideScene;

// show_me (and each interactive guide step): a ring around one spot. No
// Width/Height: a single icon or button.
public sealed record GuidePoint(double X, double Y, double? Width, double? Height, string? Label) : GuideScene;

// explain_screen: numbered frames around parts of the screen.
public sealed record GuideZones(IReadOnlyList<GuideZone> Zones) : GuideScene;

public sealed record GuideZone(double Left, double Top, double Width, double Height, string Title, string? Text);
