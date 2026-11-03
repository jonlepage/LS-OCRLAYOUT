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
                BottomCountLabel.Text = Loc.T("ov.invalidRegex");
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
        foreach (var (matchText, bounds) in FindMatches(query, compiledRegex))
        {
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = bounds.Width + padX,
                Height = bounds.Height + padY,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                Opacity = 0,
                Tag = matchText,
                Cursor = System.Windows.Input.Cursors.Hand,
            };
            rect.MouseLeftButtonDown += Rect_Click;
            Canvas.SetLeft(rect, bounds.X - padX / 2.0);
            Canvas.SetTop(rect, bounds.Y - padY / 2.0);
            HighlightCanvas.Children.Add(rect);
            rect.BeginAnimation(OpacityProperty, fadeIn);
            matchCount++;
        }

        BottomCountLabel.Foreground = new SolidColorBrush(
            System.Windows.Media.Color.FromArgb(170, 255, 255, 0));
        var text = matchCount > 0 ? Loc.Plural("ov.match", matchCount) : Loc.T("ov.noMatch");
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
        var copyAll = new System.Windows.Controls.MenuItem { Header = Loc.T("ov.copyAll") };
        copyAll.IsEnabled = _ocrWords.Count > 0;
        copyAll.Click += (_, _) =>
        {
            var allText = string.Join(" ", _ocrWords.Select(w => w.Text));
            System.Windows.Clipboard.SetText(allText);
            BottomCountLabel.Text = Loc.T("ov.allCopied");
        };
        menu.Items.Add(copyAll);

        // Copy highlighted text
        var copyHighlighted = new System.Windows.Controls.MenuItem { Header = Loc.T("ov.copyHighlighted") };
        var query = SearchBox.Text.Trim();
        copyHighlighted.IsEnabled = !string.IsNullOrEmpty(query) && HighlightCanvas.Children.Count > 0;
        copyHighlighted.Click += (_, _) =>
        {
            var matchedWords = GetMatchedWords();
            System.Windows.Clipboard.SetText(string.Join(" ", matchedWords));
            BottomCountLabel.Text = Loc.T("ov.highlightedCopied");
        };
        menu.Items.Add(copyHighlighted);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Clear history
        var clearHistory = new System.Windows.Controls.MenuItem { Header = Loc.T("ov.clearHistory") };
        clearHistory.IsEnabled = app.SearchHistory.Count > 0;
        clearHistory.Click += (_, _) =>
        {
            app.SearchHistory.Clear();
            app.AddToSearchHistory(""); // triggers save with empty (clears file)
            app.SearchHistory.Clear();
            BottomCountLabel.Text = Loc.T("ov.historyCleared");
        };
        menu.Items.Add(clearHistory);

        menu.Items.Add(new System.Windows.Controls.Separator());

        // Zen mode
        var zenMode = new System.Windows.Controls.MenuItem
        {
            Header = Loc.T("ov.zen"),
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
        var highlightSizeMenu = new System.Windows.Controls.MenuItem { Header = Loc.T("ov.highlightSize") };
        foreach (var size in new[] { "large", "medium", "small" })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = Loc.T("ov.size." + size),
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
        var searchBarMenu = new System.Windows.Controls.MenuItem { Header = Loc.T("ov.searchBarSize") };
        foreach (var size in new[] { "large", "medium", "small", "tiny" })
        {
            var item = new System.Windows.Controls.MenuItem
            {
                Header = Loc.T("ov.size." + size),
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

        return FindMatches(query, compiledRegex).Select(m => m.Text).ToList();
    }

    // One highlight per match. A word read alone lights up whole, and its
    // click copies all of it (a full e-mail address from "gmail"); inside a
    // run of Japanese or Chinese, only the characters found.
    private List<(string Text, Rect Bounds)> FindMatches(string query, Regex? regex)
    {
        var matches = new List<(string Text, Rect Bounds)>();
        foreach (var word in _ocrWords)
        {
            if (word.Pieces is not { } pieces)
            {
                bool isMatch = regex != null
                    ? regex.IsMatch(word.Text)
                    : word.Text.Contains(query, StringComparison.OrdinalIgnoreCase);
                if (isMatch) matches.Add((word.Text, word.Bounds));
                continue;
            }

            foreach (var (start, length) in Occurrences(word.Text, query, regex))
            {
                int first = -1, last = -1;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (pieces[i].Start >= start + length || pieces[i].Start + pieces[i].Length <= start) continue;
                    if (first < 0) first = i;
                    last = i;
                }
                if (first < 0) continue;

                var bounds = pieces[first].Bounds;
                for (int i = first + 1; i <= last; i++)
                    bounds = Rect.Union(bounds, pieces[i].Bounds);
                // Two finds inside the same piece: one highlight
                if (matches.Count > 0 && matches[^1].Bounds == bounds) continue;

                var from = pieces[first].Start;
                matches.Add((word.Text[from..(pieces[last].Start + pieces[last].Length)], bounds));
            }
        }
        return matches;
    }

    private static IEnumerable<(int Start, int Length)> Occurrences(string text, string query, Regex? regex)
    {
        if (regex != null)
        {
            foreach (Match match in regex.Matches(text))
                if (match.Length > 0) yield return (match.Index, match.Length);
            yield break;
        }

        for (int at = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = text.IndexOf(query, at + query.Length, StringComparison.OrdinalIgnoreCase))
            yield return (at, query.Length);
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
