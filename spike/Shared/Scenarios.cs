using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike;

/// <summary>Fixed content shared by the headless probe and the visual app, so both agree.</summary>
public static class Scenarios
{
    public const char OBJ = SpikeTextSource.ObjectReplacement;

    /// <summary>String form of <see cref="OBJ"/>, for use in <c>const</c> concatenation.</summary>
    public const string OBJS = "\uFFFC";

    public static SpikeRunProperties Default(double size = 16)
        => new(Typeface.Default, size);

    // ---- A2: embeds inside a custom ITextSource -------------------------------------------

    /// <summary>Text with two embeds, plus mixed character formatting around them.</summary>
    public const string EmbedText =
        "Start bold italic " + OBJS + " then a longer stretch of plain text that must wrap "
        + "across several lines so the embed's effect on line breaking is visible " + OBJS + " end.";

    public static int[] EmbedIndices { get; } = FindAll(EmbedText, OBJ);

    public static (SpikeTextSource Source, IReadOnlyList<StyleSpan> Spans) BuildEmbedSource(
        double boxBaseline = 24, double boxHeight = 24,
        BaselineAlignment? alignment = BaselineAlignment.Baseline)
    {
        var def = Default();
        var spans = new List<StyleSpan>
        {
            new(6, 4, def.With(weight: FontWeight.Bold)),                       // "bold"
            new(11, 6, def.With(style: FontStyle.Italic, foreground: Brushes.DarkRed)), // "italic"
            new(24, 12, def.With(size: 24, foreground: Brushes.DarkBlue)),      // larger blue chunk
        };

        var embeds = new Dictionary<int, DrawableTextRun>
        {
            [EmbedIndices[0]] = new BoxRun(40, boxHeight, boxBaseline, Colors.Goldenrod, "A", alignment),
            [EmbedIndices[1]] = new BoxRun(40, boxHeight, boxBaseline, Colors.MediumSeaGreen, "B", alignment),
        };

        return (new SpikeTextSource(EmbedText, def, spans, embeds), spans);
    }

    // ---- C1: textStyleOverrides ------------------------------------------------------------

    public const string StyledText = "Plain bold italic big red plain again.";
    public const string StyledTextWithObj = "Plain bold italic " + OBJS + " big red plain again.";

    /// <summary>Character-formatting spans expressed the C1 way.</summary>
    public static IReadOnlyList<Avalonia.Utilities.ValueSpan<TextRunProperties>> StyleOverrides(string text)
    {
        var def = Default();
        var bold = text.IndexOf("bold", StringComparison.Ordinal);
        var italic = text.IndexOf("italic", StringComparison.Ordinal);
        var big = text.IndexOf("big red", StringComparison.Ordinal);

        return new List<Avalonia.Utilities.ValueSpan<TextRunProperties>>
        {
            new(bold, 4, def.With(weight: FontWeight.Bold)),
            new(italic, 6, def.With(style: FontStyle.Italic)),
            new(big, 7, def.With(size: 26, foreground: Brushes.Crimson)),
        };
    }

    // ---- Bidi & graphemes ------------------------------------------------------------------

    public const string BidiText = "Hello \u05E9\u05DC\u05D5\u05DD world \u05E2\u05D5\u05DC\u05DD 123";

    /// <summary>Family ZWJ sequence + a combining acute accent.</summary>
    public const string ClusterText =
        "Family \U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 then e\u0301 done";

    // ---- A1: benchmark paragraphs ----------------------------------------------------------

    private const string Lorem =
        "the quick brown fox jumps over the lazy dog while packing my box with five dozen "
        + "liquor jugs and waltzing badly near the sphinx of black quartz judge my vow ";

    /// <summary>Deterministic paragraph of roughly <paramref name="length"/> characters.</summary>
    public static string Paragraph(int length)
    {
        var sb = new System.Text.StringBuilder(length);
        while (sb.Length < length)
            sb.Append(Lorem);
        return sb.ToString(0, length);
    }

    /// <summary>A style-run boundary roughly every <paramref name="every"/> characters.</summary>
    public static List<StyleSpan> MixedSpans(int textLength, int every = 25)
    {
        var def = Default();
        var spans = new List<StyleSpan>();
        var i = 0;
        var n = 0;

        while (i < textLength)
        {
            var len = Math.Min(every, textLength - i);
            var props = (n % 4) switch
            {
                0 => def,
                1 => def.With(weight: FontWeight.Bold),
                2 => def.With(style: FontStyle.Italic, foreground: Brushes.DarkRed),
                _ => def.With(size: 18, foreground: Brushes.DarkBlue),
            };
            spans.Add(new StyleSpan(i, len, props));
            i += len;
            n++;
        }

        return spans;
    }

    private static int[] FindAll(string s, char c)
    {
        var result = new List<int>();
        for (var i = 0; i < s.Length; i++)
            if (s[i] == c) result.Add(i);
        return result.ToArray();
    }

    public static string Escape(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var ch in s)
        {
            if (ch == OBJ) sb.Append("[OBJ]");
            else if (ch == '\u200D') sb.Append("[ZWJ]");
            else if (ch < 32 || (ch >= 0x0300 && ch <= 0x036F) || char.IsSurrogate(ch) || ch > 0x2100)
                sb.Append($"\\u{(int)ch:X4}");
            else sb.Append(ch);
        }
        return sb.ToString();
    }
}
