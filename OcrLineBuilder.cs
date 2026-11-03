using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Windows.Media.Ocr;

namespace ScreenSearchOverlay;

internal class OcrLineInfo
{
    public required string Text { get; init; }
    public Rect Bounds { get; init; }
}

// Turns the raw OCR results (one per enabled language) into translation
// units: one entry per visual line, keeping the most plausible reading when
// several engines read the same spot, with CJK text glued back together and
// the lines the OCR cut in two joined again. The search words come from the
// same readings (BuildSearchWords).
//
// No UI dependency, so it can be exercised on a plain screenshot outside
// the app.
internal static class OcrLineBuilder
{
    // The Japanese engine reads a Latin "o" as the ideographic full stop:
    // "Godot" comes out as "G。d。t", and the translator then cuts the
    // sentence at every "。".
    private static readonly Regex MisreadLatinO = new("(?<=[A-Za-z])[。〇○](?=[A-Za-z])", RegexOptions.Compiled);

    // Same full stop for the handakuten (the small circle) of a katakana:
    // "オフトピック" comes out as "オフトヒ。ック". Only fixed between two
    // katakana, where a real full stop never sits.
    private static readonly Regex MisreadHandakuten = new("([ハヒフヘホ])。(?=[ァ-ヺー])", RegexOptions.Compiled);

    internal static List<OcrLineInfo> Build(
        IReadOnlyList<(OcrResult Result, bool IsCjkEngine)> results, double scaleX, double scaleY)
    {
        var lines = new List<OcrLineInfo>();

        // Latin-script engines go first so a CJK engine only takes over a
        // line where it actually read CJK: the Latin engine turns Japanese
        // into garbage, while the Japanese engine reads Latin text too — but
        // less reliably than the Latin engine.
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var (result, isCjkEngine) in results)
            {
                if (isCjkEngine != (pass == 1)) continue;
                foreach (var line in result.Lines)
                {
                    if (ToLineInfo(line, scaleX, scaleY) is { } info)
                        AddOrReplace(lines, info);
                }
            }
        }

        JoinRowFragments(lines);
        return lines;
    }

    // The search looks for the query inside one word at a time, and the CJK
    // engines return every character as its own word: "日本語" fitted in no
    // word and was never found. The search words are therefore made like the
    // translation lines: one reading per line among the languages, the lines
    // the OCR cut in two joined again, the known misreadings fixed, and the
    // runs of Japanese or Chinese characters glued back into one word that
    // remembers where each character sits, so a match lights up exactly the
    // characters found. Latin words stay one word each, as before.
    internal static List<OcrWordInfo> BuildSearchWords(
        IReadOnlyList<(OcrResult Result, bool IsCjkEngine)> results, double scaleX, double scaleY)
    {
        // Same choice as Build: Latin engines first, a CJK reading takes a
        // line over only where it actually read CJK
        var chosen = new List<(OcrLineInfo Info, List<OcrLine> Lines)>();
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var (result, isCjkEngine) in results)
            {
                if (isCjkEngine != (pass == 1)) continue;
                foreach (var line in result.Lines)
                {
                    if (ToLineInfo(line, scaleX, scaleY) is not { } info) continue;
                    var at = chosen.FindIndex(c => MostlyOverlaps(c.Info.Bounds, info.Bounds));
                    if (at < 0)
                        chosen.Add((info, [line]));
                    else if (ScriptScore(info.Text) > ScriptScore(chosen[at].Info.Text))
                        chosen[at] = (info, [line]);
                }
            }
        }

        // Same joins as JoinRowFragments ("…小規模なゲ" + "ームを…"), keeping
        // the reading order of the lines: it is the order "Copy all" uses
        for (int i = 0; i < chosen.Count; i++)
        {
            for (int j = 0; j < chosen.Count; j++)
            {
                if (j == i || !AreRowFragments(chosen[i].Info, chosen[j].Info)) continue;

                chosen[i] = (new OcrLineInfo
                {
                    Text = chosen[i].Info.Text + chosen[j].Info.Text,
                    Bounds = Rect.Union(chosen[i].Info.Bounds, chosen[j].Info.Bounds)
                }, [.. chosen[i].Lines, .. chosen[j].Lines]);
                chosen.RemoveAt(j);
                if (j < i) i--;
                j = -1; // the longer line may now reach one more fragment
            }
        }

        var words = new List<OcrWordInfo>();
        foreach (var (_, lines) in chosen)
            AddSearchWords(ReadWords(lines.SelectMany(l => l.Words), scaleX, scaleY), words);
        return words;
    }

    // The words of a line in screen pixels, with the misreadings Build fixes
    // fixed here too, also when the OCR made the stray "。" a word of its own.
    private static List<(string Text, Rect Bounds)> ReadWords(IEnumerable<OcrWord> ocrWords, double scaleX, double scaleY)
    {
        var read = new List<(string Text, Rect Bounds)>();
        foreach (var word in ocrWords)
        {
            if (string.IsNullOrEmpty(word.Text)) continue;
            var r = word.BoundingRect;
            var text = MisreadLatinO.Replace(word.Text, "o");
            text = MisreadHandakuten.Replace(text, m => ((char)(m.Groups[1].Value[0] + 2)).ToString());
            read.Add((text, new Rect(r.X * scaleX, r.Y * scaleY, r.Width * scaleX, r.Height * scaleY)));
        }

        for (int i = 1; i + 1 < read.Count; i++)
        {
            var (before, mark, after) = (read[i - 1], read[i], read[i + 1]);
            if (mark.Text.Length != 1) continue;

            // "G" "。" "d" → "God": the Latin "o" read as a full stop
            if ("。〇○".Contains(mark.Text[0]) && IsLatinLetter(before.Text[^1]) && IsLatinLetter(after.Text[0]))
            {
                read[i - 1] = (before.Text + "o" + after.Text, Rect.Union(Rect.Union(before.Bounds, mark.Bounds), after.Bounds));
                read.RemoveRange(i, 2);
                i--;
            }
            // "ヒ" "。" "ッ" → "ピ" "ッ": the handakuten read as a full stop
            else if (mark.Text[0] == '。' && "ハヒフヘホ".Contains(before.Text[^1]) && IsKatakana(after.Text[0]))
            {
                read[i - 1] = (before.Text[..^1] + (char)(before.Text[^1] + 2), Rect.Union(before.Bounds, mark.Bounds));
                read.RemoveAt(i);
                i--;
            }
        }
        return read;
    }

    // Glues the words written without a space between them into one search
    // word (see Glues). Latin words, spaced, stay one word each.
    private static void AddSearchWords(List<(string Text, Rect Bounds)> read, List<OcrWordInfo> words)
    {
        var text = new StringBuilder();
        var pieces = new List<OcrPiece>();

        void Flush()
        {
            if (pieces.Count == 0) return;
            var bounds = pieces[0].Bounds;
            foreach (var piece in pieces)
                bounds = Rect.Union(bounds, piece.Bounds);
            words.Add(new OcrWordInfo
            {
                Text = text.ToString(),
                Bounds = bounds,
                Pieces = pieces.Count > 1 ? pieces.ToArray() : null,
            });
            text.Clear();
            pieces.Clear();
        }

        for (int i = 0; i < read.Count; i++)
        {
            if (i > 0 && !Glues(read[i - 1], read[i]))
                Flush();
            pieces.Add(new OcrPiece(text.Length, read[i].Text.Length, read[i].Bounds));
            text.Append(read[i].Text);
        }
        Flush();
    }

    // No space between two words, whatever the language: both sides of the
    // joint are a script written without spaces ("日" + "本"), or two single
    // characters sitting closer than a space would put them — an engine that
    // returns one character per word, in any script, gets its words back.
    // Measured both ways, for the scripts written right to left.
    private static bool Glues((string Text, Rect Bounds) left, (string Text, Rect Bounds) right)
    {
        if (IsUnspaced(left.Text[^1]) && IsUnspaced(right.Text[0])) return true;
        if (left.Text.Length != 1 || right.Text.Length != 1) return false;
        var gap = Math.Max(right.Bounds.Left - left.Bounds.Right, left.Bounds.Left - right.Bounds.Right);
        return gap < 0.25 * Math.Max(left.Bounds.Height, right.Bounds.Height);
    }

    private static bool IsLatinLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    // The katakana MisreadHandakuten accepts after the full stop: ァ to ヺ, and "ー"
    private static bool IsKatakana(char c) => c is >= 'ァ' and <= 'ヺ' or 'ー';

    internal static bool IsCjkLanguage(Windows.Globalization.Language lang)
    {
        var tag = lang.LanguageTag;
        return tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            || tag.StartsWith("ko", StringComparison.OrdinalIgnoreCase);
    }

    // A line is the translation unit: word-by-word translation is useless,
    // and CJK engines return every single character as its own word.
    private static OcrLineInfo? ToLineInfo(OcrLine line, double scaleX, double scaleY)
    {
        double left = double.MaxValue, top = double.MaxValue;
        double right = double.MinValue, bottom = double.MinValue;
        var text = new StringBuilder();

        foreach (var word in line.Words)
        {
            if (string.IsNullOrEmpty(word.Text)) continue;

            var r = word.BoundingRect;
            left = Math.Min(left, r.X);
            top = Math.Min(top, r.Y);
            right = Math.Max(right, r.X + r.Width);
            bottom = Math.Max(bottom, r.Y + r.Height);

            // Japanese and Chinese use no spaces: "設 定 を 保 存" → "設定を保存",
            // "Godot を" → "Godotを". Spaces stay between Latin (or hangul) words.
            if (text.Length > 0 && !IsUnspaced(text[^1]) && !IsUnspaced(word.Text[0]))
                text.Append(' ');
            text.Append(word.Text);
        }

        if (text.Length == 0) return null;

        var fixedText = MisreadLatinO.Replace(text.ToString(), "o");
        // ハ→パ, ヒ→ピ, フ→プ, ヘ→ペ, ホ→ポ: the handakuten form is 2 code points further
        fixedText = MisreadHandakuten.Replace(fixedText, m => ((char)(m.Groups[1].Value[0] + 2)).ToString());

        return new OcrLineInfo
        {
            Text = fixedText,
            Bounds = new Rect(
                left * scaleX,
                top * scaleY,
                (right - left) * scaleX,
                (bottom - top) * scaleY)
        };
    }

    // Several engines read the same line: keep one reading per position
    private static void AddOrReplace(List<OcrLineInfo> lines, OcrLineInfo info)
    {
        for (int i = 0; i < lines.Count; i++)
        {
            if (!MostlyOverlaps(lines[i].Bounds, info.Bounds)) continue;

            if (ScriptScore(info.Text) > ScriptScore(lines[i].Text))
                lines[i] = info;
            return;
        }
        lines.Add(info);
    }

    // Which reading of a line to trust when engines disagree:
    //   0 — Latin text, or a few stray CJK glyphs in it: a CJK engine
    //       misreading Latin text ("16 septembre 2026" → "16 pten 市化 2026")
    //   1 — mostly CJK: a CJK engine actually read CJK here
    //   2 — mostly CJK, with kana: only the Japanese engine produces kana,
    //       the Chinese engine turns them into look-alike hanzi ("を" → "在")
    private static int ScriptScore(string text)
    {
        int visible = 0, cjk = 0;
        bool kana = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) continue;
            visible++;
            if (!IsCjkLetter(c)) continue;
            cjk++;
            kana |= IsKana(c);
        }

        // A quarter is enough for mixed lines such as
        // "Python、C++、Godotなどを勉強しています。"
        if (cjk < 2 || cjk * 4 < visible) return 0;
        return kana ? 2 : 1;
    }

    // Windows OCR sometimes cuts one visual line in two, even mid-word in
    // Japanese ("…小規模なゲ" + "ームを作って…"), and "ーム" translated alone
    // is nonsense. Joins pieces sitting side by side on the same row when
    // the cut falls between two CJK characters — never Latin text, where a
    // gap on the same row usually separates distinct UI elements.
    private static void JoinRowFragments(List<OcrLineInfo> lines)
    {
        lines.Sort((a, b) => a.Bounds.Left.CompareTo(b.Bounds.Left));
        for (int i = 0; i < lines.Count; i++)
        {
            for (int j = i + 1; j < lines.Count; j++)
            {
                if (!AreRowFragments(lines[i], lines[j])) continue;

                lines[i] = new OcrLineInfo
                {
                    Text = lines[i].Text + lines[j].Text,
                    Bounds = Rect.Union(lines[i].Bounds, lines[j].Bounds)
                };
                lines.RemoveAt(j);
                j = i; // the longer line may now reach one more fragment
            }
        }
    }

    private static bool AreRowFragments(OcrLineInfo left, OcrLineInfo right)
    {
        Rect a = left.Bounds, b = right.Bounds;
        var h = Math.Max(a.Height, b.Height);

        // Same text size, same row
        if (Math.Min(a.Height, b.Height) < h * 0.7) return false;
        if (Math.Abs((a.Top + a.Bottom) - (b.Top + b.Bottom)) / 2 > h * 0.3) return false;

        // Measured on a Discord screenshot: real cuts leave 0.1 to 1.3 line
        // heights (1.3 when the OCR also dropped a small kana), while
        // distinct items on a row sit 2+ line heights apart.
        var gap = b.Left - a.Right;
        if (gap < -h * 0.5 || gap > h * 1.5) return false;

        return IsUnspaced(left.Text[^1]) && IsUnspaced(right.Text[0]);
    }

    // True when the intersection covers at least half of the smaller rect
    private static bool MostlyOverlaps(Rect a, Rect b)
    {
        var inter = Rect.Intersect(a, b);
        if (inter.IsEmpty) return false;
        var smaller = Math.Min(a.Width * a.Height, b.Width * b.Height);
        return smaller > 0 && inter.Width * inter.Height >= smaller * 0.5;
    }

    // Kana letters. The middle dot "・" (U+30FB) is punctuation, not a letter.
    private static bool IsKana(char c) =>
        c is >= '\u3041' and <= '\u30FA'   // hiragana + katakana
          or >= '\u30FC' and <= '\u30FF'   // prolonged sound mark "ー", iteration marks
          or >= '\u31F0' and <= '\u31FF'   // katakana phonetic extensions
          or >= '\uFF66' and <= '\uFF9F';  // half-width katakana

    // Kana, kanji, hangul: the characters only a CJK engine can read
    private static bool IsCjkLetter(char c) =>
        IsKana(c)
        || c is >= '\u3400' and <= '\u4DBF'   // CJK extension A
             or >= '\u4E00' and <= '\u9FFF'   // CJK unified ideographs
             or >= '\uAC00' and <= '\uD7AF';  // hangul syllables

    // Scripts written without spaces between words: kanji, kana, CJK
    // punctuation ("。", "、", "「") and full-width forms ("？", "（").
    // Hangul is left out: Korean separates its words with spaces.
    private static bool IsUnspaced(char c) =>
        c is >= '\u3000' and <= '\u303F'   // CJK symbols and punctuation
          or >= '\u3040' and <= '\u30FF'   // hiragana, katakana, "・"
          or >= '\u31F0' and <= '\u31FF'   // katakana phonetic extensions
          or >= '\u3400' and <= '\u4DBF'   // CJK extension A
          or >= '\u4E00' and <= '\u9FFF'   // CJK unified ideographs
          or >= '\uFF00' and <= '\uFFEF';  // full-width and half-width forms
}
