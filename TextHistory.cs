using System.IO;
using System.Text.Json;

namespace ScreenSearchOverlay;

// The Prompt Builder's texts, kept per prompt the way a terminal keeps its
// commands: each text that arrives by Ctrl+Alt+G and each one sent, newest
// first, a repeat moved back to the top rather than kept twice. ↑ / ↓ in the
// text box walk them (PromptWindow.History).
//
// prompt-history.json, apart from prompts.json: selected texts can be long,
// and prompts.json stays short enough to read and hand-edit.
internal sealed class TextHistory
{
    internal const int MaxEntries = 100;

    private static readonly SnapshotWriter Writer = new();

    // What the text box held when the window was last closed: Ctrl+Alt+G
    // with nothing selected starts from it, after a restart too.
    public string Text { get; set; } = "";

    // Prompt id → its texts, newest first. "" while the list has no prompt.
    public Dictionary<string, List<string>> Prompts { get; set; } = [];

    internal IReadOnlyList<string> For(string promptId) =>
        Prompts.TryGetValue(promptId, out var entries) ? entries : [];

    internal void Add(string promptId, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!Prompts.TryGetValue(promptId, out var entries)) Prompts[promptId] = entries = [];
        var existing = IndexOf(entries, text);
        if (existing >= 0) entries.RemoveAt(existing);
        entries.Insert(0, text);
        if (entries.Count > MaxEntries) entries.RemoveRange(MaxEntries, entries.Count - MaxEntries);
    }

    // In the history of any prompt.
    internal bool Contains(string text) => Prompts.Values.Any(entries => IndexOf(entries, text) >= 0);

    // The same text give or take the blanks around it (a copied line often
    // ends with a line break) is the same entry.
    internal static int IndexOf(IReadOnlyList<string> entries, string text)
    {
        var trimmed = text.AsSpan().Trim();
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].AsSpan().Trim().SequenceEqual(trimmed)) return i;
        return -1;
    }

    // Not pruned of the prompts missing from prompts.json: when that file is
    // unreadable (a hand edit), starter prompts stand in for the user's, and
    // their texts must still be here once it is fixed. A deleted prompt's
    // texts go with it instead (PromptWindow.ForgetDeleted).
    internal static TextHistory Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();

            var history = JsonSerializer.Deserialize<TextHistory>(File.ReadAllText(path), PromptLibrary.JsonOptions) ?? new();
            // A hand-edited file may hold nulls where texts are expected.
            history.Text ??= "";
            history.Prompts ??= [];
            foreach (var (id, entries) in history.Prompts.ToList())
            {
                if (entries is null) history.Prompts.Remove(id);
                else entries.RemoveAll(string.IsNullOrWhiteSpace);
            }
            return history;
        }
        catch
        {
            // As prompts.json: a file we cannot read is set aside, never
            // overwritten by the next save.
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
            return new();
        }
    }

    internal void Save(string path) =>
        Writer.Save(path, JsonSerializer.Serialize(this, PromptLibrary.JsonOptions));

    internal void SaveInBackground(string path) =>
        Writer.SaveInBackground(path, JsonSerializer.Serialize(this, PromptLibrary.JsonOptions));
}
