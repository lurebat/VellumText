using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike.Probe;

/// <summary>
/// A stable text source whose content mutates in place — the realistic shape for a
/// ParagraphView that survives edits, and the only shape under which a persistent
/// TextRunCache could possibly help.
/// </summary>
public sealed class BenchSource : ITextSource
{
    private readonly StyleSpan[] _spans;

    public BenchSource(string text, int spanEvery = 25)
    {
        Text = text;
        _spans = Scenarios.MixedSpans(text.Length + 4096, spanEvery).ToArray();
        Default = Scenarios.Default();
    }

    public string Text { get; set; }
    public SpikeRunProperties Default { get; }

    public TextRun? GetTextRun(int textSourceIndex)
    {
        if (textSourceIndex >= Text.Length)
            return new TextEndOfParagraph();

        var index = textSourceIndex / 25;
        var span = _spans[Math.Min(index, _spans.Length - 1)];
        var end = Math.Min(Text.Length, (index + 1) * 25);
        if (end <= textSourceIndex) end = Text.Length;

        return new TextCharacters(
            Text.AsMemory(textSourceIndex, end - textSourceIndex), span.Properties);
    }
}

/// <summary>Records every position the formatter asks for, to prove whether a cached build re-reads the text.</summary>
public sealed class CountingSource : ITextSource
{
    private readonly BenchSource _inner;

    public CountingSource(string text) => _inner = new BenchSource(text);

    public string Text { get => _inner.Text; set => _inner.Text = value; }
    public int Calls { get; private set; }
    public List<int> Indices { get; } = new();

    public void Reset() { Calls = 0; Indices.Clear(); }

    public TextRun? GetTextRun(int textSourceIndex)
    {
        Calls++;
        if (Indices.Count < 40) Indices.Add(textSourceIndex);
        return _inner.GetTextRun(textSourceIndex);
    }
}

public static class A1Common
{
    public const double MaxWidth = 480;

    public static TextParagraphProperties Props { get; } =
        new SpikeParagraphProperties(Scenarios.Default());

    public static TextLayout Build(ITextSource source, TextRunCache? cache)
        => cache is null
            ? new TextLayout(source, Props, TextTrimming.None, MaxWidth, double.PositiveInfinity, int.MaxValue)
            : new TextLayout(source, Props, TextTrimming.None, MaxWidth, double.PositiveInfinity, int.MaxValue, cache);

    /// <summary>Insert one character at <paramref name="offset"/>, as a keystroke would.</summary>
    public static string Insert(string text, int offset, char c)
        => text.Insert(Math.Clamp(offset, 0, text.Length), c.ToString());

    /// <summary>A geometry fingerprint, so a "faster" arm can be proven to produce the same layout.</summary>
    public static string Fingerprint(TextLayout layout)
        => string.Join(";", layout.TextLines.Select(l =>
            $"{l.FirstTextSourceIndex},{l.Length},{l.Width:0.####},{l.Height:0.####},{l.Baseline:0.####}"))
           + $"|{layout.Width:0.####}x{layout.Height:0.####}";
}
