using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace ScreenSearchOverlay;

// SourceLanguage: the language the provider detected ("ja", "en"…), or null
// when it didn't say.
internal readonly record struct Translation(string Text, string? SourceLanguage);

// Provider-agnostic contract: texts[i] → result[i]. Same count, same order —
// the index is the only link between an OCR block and its translation, so
// the overlay never has to re-align anything.
internal interface ITranslator
{
    Task<Translation[]> TranslateAsync(IReadOnlyList<string> texts, string targetLanguage, CancellationToken ct);
}

// Free, keyless Google endpoint used by the Chrome extension (client=gtx).
// Unofficial: it can be throttled or change without notice.
//
// Why translate_a/t with repeated `q` params instead of translate_a/single
// with newline-joined text: /single auto-detects ONE language for the whole
// payload, so on a mixed screen (English menus + Japanese content) the
// minority language comes back untranslated. /t detects the language per
// item and returns [[translation, detectedLang], ...] in input order — no
// markers, no newline splitting, exact index mapping.
internal sealed class GoogleTranslator : ITranslator
{
    private const string Endpoint = "https://translate.googleapis.com/translate_a/t?client=gtx&sl=auto&tl=";

    // Small batches sent in parallel answer in ~150 ms; one huge batch is
    // not faster and risks hitting the body size limit.
    private const int MaxItemsPerBatch = 50;
    private const int MaxCharsPerBatch = 4000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // Lives as long as the app: reopening the same software screen is
    // instant and doesn't hit the network again.
    private readonly ConcurrentDictionary<(string Lang, string Text), Translation> _cache = new();

    public async Task<Translation[]> TranslateAsync(IReadOnlyList<string> texts, string targetLanguage, CancellationToken ct)
    {
        var results = new Translation[texts.Count];

        // Indexes that still need a network round-trip
        var misses = new List<int>();
        for (int i = 0; i < texts.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text))
                results[i] = new Translation(text, null);
            else if (_cache.TryGetValue((targetLanguage, text), out var cached))
                results[i] = cached;
            else
                misses.Add(i);
        }

        if (misses.Count > 0)
        {
            var distinct = misses.Select(i => texts[i]).Distinct().ToList();
            await Task.WhenAll(Batch(distinct).Select(b => TranslateBatchAsync(b, targetLanguage, ct)));

            foreach (var i in misses)
            {
                results[i] = _cache.TryGetValue((targetLanguage, texts[i]), out var translated)
                    ? translated
                    : new Translation(texts[i], null);
            }
        }

        return results;
    }

    private static IEnumerable<List<string>> Batch(List<string> texts)
    {
        var batch = new List<string>();
        int chars = 0;
        foreach (var text in texts)
        {
            if (batch.Count > 0 && (batch.Count >= MaxItemsPerBatch || chars + text.Length > MaxCharsPerBatch))
            {
                yield return batch;
                batch = [];
                chars = 0;
            }
            batch.Add(text);
            chars += text.Length;
        }
        if (batch.Count > 0)
            yield return batch;
    }

    private async Task TranslateBatchAsync(List<string> batch, string targetLanguage, CancellationToken ct)
    {
        // POST keeps long Japanese payloads (9 bytes per char once
        // percent-encoded) out of the URL length limit.
        var body = new StringBuilder();
        foreach (var text in batch)
        {
            if (body.Length > 0) body.Append('&');
            body.Append("q=").Append(Uri.EscapeDataString(text));
        }

        using var content = new StringContent(body.ToString(), Encoding.UTF8, "application/x-www-form-urlencoded");
        using var response = await Http.PostAsync(Endpoint + Uri.EscapeDataString(targetLanguage), content, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        var items = json.RootElement;
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() != batch.Count)
            throw new InvalidDataException(
                $"Google Translate returned {(items.ValueKind == JsonValueKind.Array ? items.GetArrayLength() : 0)} items for {batch.Count} sent");

        int i = 0;
        foreach (var item in items.EnumerateArray())
        {
            // Normally [translation, detectedLang]; tolerate a bare string
            string? translated = null, detected = null;
            if (item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
            {
                translated = item[0].GetString();
                if (item.GetArrayLength() > 1 && item[1].ValueKind == JsonValueKind.String)
                    detected = item[1].GetString();
            }
            else if (item.ValueKind == JsonValueKind.String)
            {
                translated = item.GetString();
            }

            _cache[(targetLanguage, batch[i])] = new Translation(
                string.IsNullOrEmpty(translated) ? batch[i] : translated, detected);
            i++;
        }
    }
}

// Decides which OCR lines deserve a translation box. The screen's own pixels
// are always sharper than a re-rendering and immune to OCR mistakes, so a box
// is drawn only when it shows the user something new.
internal static class TranslationFilter
{
    // OCR noise never worth a round-trip: icons and avatars read as "0" or
    // a lone glyph ("物", "1一"), counters, dates and timestamps — anything
    // with fewer than two letters.
    public static bool IsWorthTranslating(string text)
    {
        int letters = 0;
        foreach (var c in text)
        {
            if (char.IsLetter(c) && ++letters >= 2)
                return true;
        }
        return false;
    }

    // False when the line is already in the target language — redrawing it
    // would only replay OCR mistakes ("avant de pouvoir envoyer" read as
    // "avant de envoyer") — or when the translator returned the same letters
    // and digits (names, product codes; spacing, case and punctuation aside).
    public static bool ChangesAnything(string source, Translation translation, string targetLanguage)
    {
        if (translation.SourceLanguage is { } detected && SameLanguage(detected, targetLanguage))
            return false;
        return !string.Equals(LettersAndDigits(source), LettersAndDigits(translation.Text), StringComparison.OrdinalIgnoreCase);
    }

    // "fr" matches "fr-CA": compare the primary language subtag only
    private static bool SameLanguage(string a, string b) =>
        string.Equals(a.Split('-')[0], b.Split('-')[0], StringComparison.OrdinalIgnoreCase);

    private static string LettersAndDigits(string text) =>
        string.Concat(text.Where(char.IsLetterOrDigit));
}
