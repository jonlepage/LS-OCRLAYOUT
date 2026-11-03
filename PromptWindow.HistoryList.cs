using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace ScreenSearchOverlay;

// A piece of a row's text; Hit: it matches the search, shown highlighted.
public sealed record TextPiece(string Text, bool Hit);

// A row of the history list: an entry as it would come back, or the draft
// (Index -1). Preview: the text on one paragraph, from a little before the
// first match while searching; Tip: the text with its lines, for the hover.
public sealed record HistoryRow(int Index, string Number, string Meta, bool IsCurrent,
    IReadOnlyList<TextPiece> Preview, IReadOnlyList<TextPiece> Tip)
{
    public bool IsDraft => Index < 0;
}

// The history icon's list: all the selected prompt's texts at once, newest
// first, to search and pick from. A pick is a jump in the ↑ / ↓ walk
// (PromptWindow.History): the text left comes back as it was left.
public partial class PromptWindow
{
    // How much of a text a row shows (its two lines), and its hover.
    private const int PreviewLength = 260;
    private const int TipLength = 1500;

    private const CompareOptions SearchOptions = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    // The texts as they would come back, read as the list opens: the box
    // cannot change while it is open. Flat: on one paragraph, as shown.
    private List<(int Index, string Text, string Flat)> _historyTexts = [];

    private bool HistoryListOpen => HistoryCard.Visibility == Visibility.Visible;

    // Under the icon, its right edge on the text box's. Both stay where they
    // are as the window is resized: the margins are measured from its right.
    private void OpenHistoryList()
    {
        if (HistoryEntries.Count == 0) return;
        _historyTexts = HistoryTexts();
        var top = HistoryListButton.TranslatePoint(new Point(0, HistoryListButton.ActualHeight), RootGrid).Y + 6;
        var right = RootGrid.ActualWidth - BodyBox.TranslatePoint(new Point(BodyBox.ActualWidth, 0), RootGrid).X;
        HistoryCard.Margin = new Thickness(0, top, right, 12);
        HistoryCard.Visibility = Visibility.Visible;
        HistoryFilter.Text = "";
        FilterHistoryList();
        // Typing searches at once; ↑ / ↓ / Enter / Esc are the window's
        // (Window_PreviewKeyDown).
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(HistoryFilter));
    }

    // However it closes, the keyboard goes back to the text.
    private void CloseHistoryList()
    {
        if (!HistoryListOpen) return;
        HistoryCard.Visibility = Visibility.Collapsed;
        BodyBox.Focus();
    }

    private List<(int, string, string)> HistoryTexts()
    {
        var entries = HistoryEntries;
        var texts = new List<(int, string, string)>(entries.Count + 1);
        if (_draft is not null && (_historyIndex < 0 ? BodyBox.Text : _draft) is { } draft && !string.IsNullOrWhiteSpace(draft))
            texts.Add((-1, draft, Flatten(draft)));
        for (int i = 0; i < entries.Count; i++)
        {
            var text = i == _historyIndex ? BodyBox.Text : _historyEdits.GetValueOrDefault(i, entries[i]);
            texts.Add((i, text, Flatten(text)));
        }
        return texts;
    }

    // The rows matching the search, accents and case ignored as in the
    // prompt list. The cursor on the current text, or on the newest match.
    private void FilterHistoryList()
    {
        var query = HistoryFilter.Text.Trim();
        var rows = new List<HistoryRow>(_historyTexts.Count);
        foreach (var (index, text, flat) in _historyTexts)
        {
            if (Excerpt(flat, query, PreviewLength, lead: 40) is not { } preview) continue;
            var tip = text.Trim();
            var characters = Loc.Characters(text.Length);
            rows.Add(new HistoryRow(
                index,
                index < 0 ? "" : (index + 1).ToString(Loc.Culture),
                index < 0 ? Loc.T("pb.history.draft") + " · " + characters : characters,
                index == _historyIndex,
                preview,
                // A search across a line break matches the flat text only.
                Excerpt(tip, query, TipLength, lead: 300) ?? Excerpt(tip, "", TipLength, lead: 0)!));
        }

        HistoryList.ItemsSource = rows;
        HistoryEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        HistoryList.SelectedItem = query.Length == 0
            ? rows.FirstOrDefault(r => r.IsCurrent) ?? rows.FirstOrDefault()
            : rows.FirstOrDefault();
        if (HistoryList.SelectedItem is { } selected) HistoryList.ScrollIntoView(selected);
    }

    // A window on the text: from its start, or from a little before the
    // first match, with every match in it as a hit. null: no match.
    private static List<TextPiece>? Excerpt(string text, string query, int length, int lead)
    {
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var start = 0;
        if (query.Length > 0)
        {
            var first = compare.IndexOf(text, query, SearchOptions);
            if (first < 0) return null;
            if (first > lead)
            {
                // On a word's start, not in the middle of one.
                start = first - lead;
                while (start > 0 && !char.IsWhiteSpace(text[start - 1]) && first - start < lead + 20) start--;
            }
        }

        var end = Math.Min(text.Length, start + length);
        var pieces = new List<TextPiece>();
        if (start > 0) pieces.Add(new("…", false));
        var from = start;
        while (query.Length > 0 && from < end)
        {
            var at = compare.IndexOf(text.AsSpan(from, end - from), query, SearchOptions, out var matched);
            if (at < 0 || matched == 0) break;
            if (at > 0) pieces.Add(new(text.Substring(from, at), false));
            pieces.Add(new(text.Substring(from + at, matched), true));
            from += at + matched;
        }
        if (from < end) pieces.Add(new(text[from..end], false));
        if (end < text.Length) pieces.Add(new("…", false));
        return pieces;
    }

    // Line breaks and runs of blanks as single spaces.
    private static string Flatten(string text)
    {
        var flat = new StringBuilder(text.Length);
        var blank = false;
        foreach (var c in text.AsSpan().Trim())
        {
            if (char.IsWhiteSpace(c))
            {
                blank = true;
                continue;
            }
            if (blank) flat.Append(' ');
            blank = false;
            flat.Append(c);
        }
        return flat.ToString();
    }

    private void MoveHistoryCursor(int delta)
    {
        if (HistoryList.Items.Count == 0) return;
        HistoryList.SelectedIndex = Math.Clamp(HistoryList.SelectedIndex + delta, 0, HistoryList.Items.Count - 1);
        HistoryList.ScrollIntoView(HistoryList.SelectedItem);
    }

    // The list closes; the text shows from its start.
    private void PickHistory(HistoryRow? row)
    {
        CloseHistoryList();
        if (row is not null && row.Index != _historyIndex) ShowHistoryEntry(row.Index, fromTop: true);
    }

    private void HistoryList_Click(object sender, RoutedEventArgs e)
    {
        if (HistoryListOpen) CloseHistoryList();
        else OpenHistoryList();
    }

    // A click elsewhere in the window closes the list, and goes on to what it
    // was on. Not one on the icon: its own click closes it.
    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (HistoryListOpen && !HistoryCard.IsMouseOver && !HistoryListButton.IsMouseOver) CloseHistoryList();
    }

    private void HistoryFilter_TextChanged(object sender, TextChangedEventArgs e) => FilterHistoryList();

    // A click on a row picks it; not one on the scrollbar.
    private void HistoryList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(HistoryList, e.OriginalSource as DependencyObject) is ListBoxItem item)
            PickHistory(item.DataContext as HistoryRow);
    }
}

// A TextBlock's text as pieces, the hits highlighted. The history list's
// rows are recycled: each new value rebuilds the runs.
public static class Highlight
{
    public static readonly DependencyProperty PiecesProperty = DependencyProperty.RegisterAttached(
        "Pieces", typeof(IReadOnlyList<TextPiece>), typeof(Highlight), new PropertyMetadata(null, OnPiecesChanged));

    public static IReadOnlyList<TextPiece>? GetPieces(DependencyObject element) =>
        (IReadOnlyList<TextPiece>?)element.GetValue(PiecesProperty);

    public static void SetPieces(DependencyObject element, IReadOnlyList<TextPiece>? value) =>
        element.SetValue(PiecesProperty, value);

    private static void OnPiecesChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not TextBlock block) return;
        block.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<TextPiece> pieces) return;
        foreach (var piece in pieces)
        {
            var run = new Run(piece.Text);
            if (piece.Hit)
            {
                run.SetResourceReference(TextElement.BackgroundProperty, "SearchHitBg");
                run.SetResourceReference(TextElement.ForegroundProperty, "SearchHitText");
            }
            block.Inlines.Add(run);
        }
    }
}
