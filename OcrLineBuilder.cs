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
// the lines the OCR cut in two joined again.
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
