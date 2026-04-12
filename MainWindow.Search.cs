using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace ScreenSearchOverlay;

public partial class MainWindow : Window
{
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        HighlightCanvas.Children.Clear();

        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            BottomCountLabel.Text = "";
            return;
        }

        // Compile regex once if regex mode is active
        Regex? compiledRegex = null;
        if (RegexToggle?.IsChecked == true)
        {
            try
            {
                compiledRegex = new Regex(query, RegexOptions.IgnoreCase);
            }
            catch (RegexParseException)
            {
                BottomCountLabel.Text = "invalid regex";
                BottomCountLabel.Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(170, 255, 80, 80));
                return;
            }
        }

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        var (fill, stroke, thickness, padX, padY) = GetHighlightStyle();

        int matchCount = 0;
        foreach (var word in _ocrWords)
        {
            bool isMatch = compiledRegex != null
                ? compiledRegex.IsMatch(word.Text)
                : word.Text.Contains(query, StringComparison.OrdinalIgnoreCase);

            if (isMatch)
            {
                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = word.Bounds.Width + padX,
                    Height = word.Bounds.Height + padY,
                    Fill = fill,
                    Stroke = stroke,
                    StrokeThickness = thickness,
                    Opacity = 0,
                    Tag = word.Text,
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                rect.MouseLeftButtonDown += Rect_Click;
                Canvas.SetLeft(rect, word.Bounds.X - padX / 2.0);
                Canvas.SetTop(rect, word.Bounds.Y - padY / 2.0);
                HighlightCanvas.Children.Add(rect);
                rect.BeginAnimation(OpacityProperty, fadeIn);
                matchCount++;
            }
        }

        BottomCountLabel.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(170, 255, 255, 0));
        var text = matchCount > 0 ? $"{matchCount} match{(matchCount > 1 ? "es" : "")}" : "no match";
        BottomCountLabel.Text = text;
    }

    private void Rect_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Shapes.Rectangle rect || rect.Tag is not string text)
            return;

        System.Windows.Clipboard.SetText(text);

        var originalFill = rect.Fill;
        rect.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 0, 255, 100));

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        timer.Tick += (_, _) =>
        {
            rect.Fill = originalFill;
            timer.Stop();
        };
        timer.Start();
        e.Handled = true;
    }

    private void RegexToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (RegexToggle.IsChecked == true)
        {
            RegexToggle.Background = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(80, 255, 255, 0));
            RegexToggle.BorderBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(200, 255, 220, 0));
        }
        else
        {
            RegexToggle.Background = System.Windows.Media.Brushes.Transparent;
            RegexToggle.BorderBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromArgb(102, 255, 255, 255));
        }
        SearchBox_TextChanged(SearchBox, null!);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)System.Windows.Application.Current;
        var menu = new System.Windows.Controls.ContextMenu();
        menu.Style = null;

        // Copy all screen text
        var copyAll = new System.Windows.Controls.MenuItem { Header = "Copy all screen text" };
        copyAll.IsEnabled = _ocrWords.Count > 0;
        copyAll.Click += (_, _) =>
        {
            var allText = string.Join(" ", _ocrWords.Select(w => w.Text));
            System.Windows.Clipboard.SetText(allText);
            BottomCountLabel.Text = "all text copied";
        };
        menu.Items.Add(copyAll);

        // Copy highlighted text
        var copyHighlighted = new System.Windows.Controls.MenuItem { Header = "Copy highlighted text" };
        var query = SearchBox.Text.Trim();
        copyHighlighted.IsEnabled = !string.IsNullOrEmpty(query) && HighlightCanvas.Children.Count > 0;
        copyHighlighted.Click += (_, _) =>
        {
            var matchedWords = GetMatchedWords();
            System.Windows.Clipboard.SetText(string.Join(" ", matchedWords));
            BottomCountLabel.Text = "highlighted text copied";
        };
        menu.Items.Add(copyHighlighted);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Clear history
        var clearHistory = new System.Windows.Controls.MenuItem { Header = "Clear history" };
        clearHistory.IsEnabled = app.SearchHistory.Count > 0;
        clearHistory.Click += (_, _) =>
        {
            app.SearchHistory.Clear();
            app.AddToSearchHistory(""); // triggers save with empty (clears file)
            app.SearchHistory.Clear();
            BottomCountLabel.Text = "history cleared";
        };
        menu.Items.Add(clearHistory);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Zen mode
        var zenMode = new System.Windows.Controls.MenuItem
        {
            Header = "Zen mode",
            IsCheckable = true,
            IsChecked = app.ZenMode
        };
        zenMode.Click += (_, _) =>
        {
            app.ZenMode = zenMode.IsChecked;
            app.SaveSettings();
            SearchBox_TextChanged(SearchBox, null!);
        };
        menu.Items.Add(zenMode);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Highlight size
        var highlightSizeMenu = new System.Windows.Controls.MenuItem { Header = "Highlight size" };
        foreach (var size in new[] { "large", "medium", "small" })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = size,
                IsCheckable = true,
                IsChecked = app.BoxSize == size
            };
            var capturedSize = size;
            item.Click += (_, _) =>
            {
                app.BoxSize = capturedSize;
                app.SaveSettings();
                SearchBox_TextChanged(SearchBox, null!);
            };
            highlightSizeMenu.Items.Add(item);
        }
        menu.Items.Add(highlightSizeMenu);

        // Search bar size
        var searchBarMenu = new System.Windows.Controls.MenuItem { Header = "Search bar size" };
        foreach (var size in new[] { "large", "medium", "small", "tiny" })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = size,
                IsCheckable = true,
                IsChecked = app.SearchBarSize == size
            };
            var capturedSize = size;
            item.Click += (_, _) =>
            {
                app.SearchBarSize = capturedSize;
                app.SaveSettings();
                ApplySearchBarSize();
            };
            searchBarMenu.Items.Add(item);
        }
        menu.Items.Add(searchBarMenu);

        menu.PlacementTarget = MenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private List<string> GetMatchedWords()
    {
        var query = SearchBox.Text.Trim();
        if (string.IsNullOrEmpty(query)) return [];

        Regex? compiledRegex = null;
        if (RegexToggle?.IsChecked == true)
        {
            try { compiledRegex = new Regex(query, RegexOptions.IgnoreCase); }
            catch { return []; }
        }

        return _ocrWords
            .Where(w => compiledRegex != null
                ? compiledRegex.IsMatch(w.Text)
                : w.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(w => w.Text)
            .ToList();
    }

    private (SolidColorBrush fill, SolidColorBrush stroke, double thickness, int padX, int padY) GetHighlightStyle()
    {
        var app = (App)System.Windows.Application.Current;

        // Highlight padding
        var (padX, padY) = app.BoxSize switch
        {
            "large" => (12, 10),
            "medium" => (8, 6),
            "small" => (4, 3),
            _ => (8, 6)
        };

        if (app.ZenMode)
        {
            return (
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(25, 255, 255, 255)),
                new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, 180, 180, 180)),
                1,
                padX, padY
            );
        }

        return (
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 0)),
            new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 255, 220, 0)),
            2,
            padX, padY
        );
    }
}
