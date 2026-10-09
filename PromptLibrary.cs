using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenSearchOverlay;

// One saved prompt. Mutable and observable because the prompt window edits
// it in place: the list on the left follows the name as you type it.
//
// Public (not internal) because WPF bindings read its properties.
public sealed class SavedPrompt : INotifyPropertyChanged
{
    private const int PreviewLength = 140;

    private string _name = "";
    private string _prompt = "";
    private string _titleColor = "";
    private string _shortcut = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name
    {
        get => _name;
        set { if (_name == value) return; _name = value; Notify(nameof(Name), nameof(DisplayName), nameof(Preview)); }
    }

    public string Prompt
    {
        get => _prompt;
        set { if (_prompt == value) return; _prompt = value; Notify(nameof(Prompt), nameof(DisplayName), nameof(Preview)); }
    }

    // One of the starter prompts (StarterPrompts), by key; null for the
    // user's own. Kept once edited: "Restore the original" uses it.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Starter { get; set; }

    // "#RRGGBB" picked from the palette, "" for the default text color. The
    // title wears it in the editor and in the list: prompts are told apart
    // by color at a glance.
    public string TitleColor
    {
        get => _titleColor;
        set { if (_titleColor == value) return; _titleColor = value; Notify(nameof(TitleColor)); }
    }

    // The name is optional: an unnamed prompt shows its first line, so writing
    // a prompt and sending it right away never requires inventing a title.
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Name)) return Name.Trim();
            var firstLine = FirstLine(Prompt);
            return firstLine.Length > 0 ? firstLine : Loc.T("prompt.untitled");
        }
    }

    // Second line of a list entry. For an unnamed prompt the first line is
    // already the title, so the preview starts after it.
    [JsonIgnore]
    public string Preview
    {
        get
        {
            var body = string.IsNullOrWhiteSpace(Name) ? AfterFirstLine(Prompt) : Prompt;
            var flat = string.Join(' ', body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return flat.Length > PreviewLength ? flat[..PreviewLength] : flat;
        }
    }

    // "1"…"9" while the prompt is among the first nine visible ones (Ctrl+1…9).
    [JsonIgnore]
    public string Shortcut
    {
        get => _shortcut;
        set { if (_shortcut == value) return; _shortcut = value; Notify(nameof(Shortcut)); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // After a language change: "Sans titre" / "Untitled".
    internal void RefreshDisplayName() => Notify(nameof(DisplayName));

    // After a theme change: the same color, shown in the other theme's shade.
    internal void RefreshTitleColor() => Notify(nameof(TitleColor));

    private void Notify(params string[] names)
    {
        foreach (var name in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static string FirstLine(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0) return trimmed;
        }
        return "";
    }

    private static string AfterFirstLine(string text)
    {
        var trimmed = text.TrimStart();
        var newline = trimmed.IndexOf('\n');
        return newline < 0 ? "" : trimmed[(newline + 1)..];
    }
}

// prompts.json load/save and message composition — no UI dependency.
internal static class PromptLibrary
{
    // Indented, accents written as-is: the file is meant to be readable and
    // hand-editable, not just a cache.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // language: the UI language, for the starter prompts of a first run.
    internal static ObservableCollection<SavedPrompt> Load(string path, string language)
    {
        try
        {
            if (!File.Exists(path))
                return new(StarterPrompts.Create(language));

            var loaded = JsonSerializer.Deserialize<List<SavedPrompt>>(File.ReadAllText(path), JsonOptions) ?? [];
            StarterPrompts.Recognize(loaded);
            return new(loaded);
        }
        catch
        {
            // A file we cannot read is set aside rather than overwritten by
            // the next save: hand edits are never lost silently.
            try { File.Copy(path, path + ".bak", overwrite: true); } catch { }
            return new(StarterPrompts.Create(language));
        }
    }

    // Synchronous: for app exit, where a background write could be cut short.
    internal static void Save(string path, IEnumerable<SavedPrompt> prompts) =>
        Write(path, Snapshot(prompts));

    // The JSON is taken on the calling (UI) thread — microseconds, and the
    // prompts are only ever touched there — and the disk write, which an
    // antivirus scan can stretch to tens of ms, happens on the thread pool.
    internal static void SaveInBackground(string path, IEnumerable<SavedPrompt> prompts)
    {
        var snapshot = Snapshot(prompts);
        Task.Run(() => Write(path, snapshot));
    }

    private static readonly Lock WriteLock = new();
    private static long _snapshotVersion;
    private static long _writtenVersion;

    private static (long Version, string Json) Snapshot(IEnumerable<SavedPrompt> prompts) =>
        (Interlocked.Increment(ref _snapshotVersion), JsonSerializer.Serialize(prompts, JsonOptions));

    private static void Write(string path, (long Version, string Json) snapshot)
    {
        lock (WriteLock)
        {
            // Two background writes may reach the lock out of order: an older
            // snapshot never overwrites a newer one.
            if (snapshot.Version <= _writtenVersion) return;
            try
            {
                // Write-then-rename: a crash mid-write never leaves a truncated file.
                var temp = path + ".tmp";
                File.WriteAllText(temp, snapshot.Json);
                File.Move(temp, path, overwrite: true);
                _writtenVersion = snapshot.Version;
            }
            catch { }
        }
    }

    // The prompt goes first, so a prompt ending with ':' reads naturally:
    // "Corrige et traduis ce texte :" + blank line + the text.
    internal static string Compose(string? prompt, string? text)
    {
        var p = prompt?.Trim() ?? "";
        var t = text?.Trim() ?? "";
        if (p.Length == 0) return t;
        if (t.Length == 0) return p;
        return p + "\n\n" + t;
    }
}
