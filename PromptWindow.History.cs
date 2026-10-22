using System.Windows;

namespace ScreenSearchOverlay;

// The text box walks the selected prompt's history (TextHistory) like a
// terminal's command line: ↑ on its first line goes back, ↓ on its last
// comes forward, and past the newest text is what was being written before.
// The header's arrows do the same, its counter says where the box is.
public partial class PromptWindow
{
    // Where the box is in the history: 0 is the newest text, -1 the draft.
    private int _historyIndex;
    // The text the box held when the walk began, if it is in no entry: ↓ past
    // the newest text brings it back. null: the box began on an entry.
    private string? _draft;
    // Entries edited during the walk, by index: they come back as they were
    // left, until a new text arrives or one is sent — as in a terminal.
    private readonly Dictionary<int, string> _historyEdits = [];

    private string HistoryKey => Selected?.Id ?? "";
    private IReadOnlyList<string> HistoryEntries => _app.TextHistory.For(HistoryKey);

    // A walk begins from the box as it is: on its entry when it holds one,
    // else on a draft. After a new text, a send, another prompt.
    private void ResetHistory()
    {
        if (_view is null) return; // during construction
        _historyEdits.Clear();
        _historyIndex = TextHistory.IndexOf(HistoryEntries, BodyBox.Text);
        _draft = _historyIndex < 0 ? BodyBox.Text : null;
        RefreshHistory();
    }

    // delta +1: an older text, -1: a newer one. false: nothing that way.
    private bool StepHistory(int delta)
    {
        var entries = HistoryEntries;
        var target = _historyIndex + delta;
        if (target < (_draft is null ? 0 : -1) || target >= entries.Count) return false;

        // What is left comes back as it was left.
        if (_historyIndex < 0) _draft = BodyBox.Text;
        else if (_historyIndex < entries.Count && BodyBox.Text == entries[_historyIndex]) _historyEdits.Remove(_historyIndex);
        else _historyEdits[_historyIndex] = BodyBox.Text;

        _historyIndex = target;
        BodyBox.Text = target < 0 ? _draft! : _historyEdits.GetValueOrDefault(target, entries[target]);
        // The caret on the edge the next press goes on from: the top going
        // back, the bottom coming forward.
        if (delta > 0)
        {
            BodyBox.CaretIndex = 0;
            BodyBox.ScrollToHome();
        }
        else
        {
            BodyBox.CaretIndex = BodyBox.Text.Length;
            BodyBox.ScrollToEnd();
        }
        RefreshHistory();
        return true;
    }

    // "3 / 12": the third newest of twelve texts; "– / 12" on the draft.
    private void RefreshHistory()
    {
        var count = HistoryEntries.Count;
        HistoryPanel.Visibility = count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var position = _historyIndex < 0 ? "–" : (_historyIndex + 1).ToString(Loc.Culture);
        HistoryPosition.Text = position + " / " + count.ToString(Loc.Culture);
        OlderTextButton.IsEnabled = _historyIndex + 1 < count;
        NewerTextButton.IsEnabled = _historyIndex > (_draft is null ? 0 : -1);
    }

    // The caret on the box's first (or last) line as shown, wrapped lines
    // included, and no selection: ↑ / ↓ would have nowhere to go in the text.
    private bool CaretOnEdge(bool first)
    {
        if (!BodyBox.IsKeyboardFocused || BodyBox.SelectionLength > 0) return false;
        if (BodyBox.Text.Length == 0) return true;
        var line = BodyBox.GetLineIndexFromCharacterIndex(BodyBox.CaretIndex);
        if (line < 0) return false; // not laid out yet
        return first ? line == 0 : line == BodyBox.LineCount - 1;
    }

    private void OlderText_Click(object sender, RoutedEventArgs e) => StepHistory(+1);
    private void NewerText_Click(object sender, RoutedEventArgs e) => StepHistory(-1);
}
