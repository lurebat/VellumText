using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace Vellum.Spike;

/// <summary>A half-open run of uniform character formatting over the backing text.</summary>
public readonly record struct StyleSpan(int Start, int Length, SpikeRunProperties Properties)
{
    public int End => Start + Length;
    public bool Contains(int i) => i >= Start && i < End;
}

/// <summary>
/// A <see cref="DrawableTextRun"/> that paints a solid box inline. <see cref="Length"/> is 1 so it
/// lines up with exactly one <c>\uFFFC</c> in the backing text, per architecture.md §4.7.
/// </summary>
public sealed class BoxRun : DrawableTextRun
{
    private readonly Color _fill;
    private readonly string _label;
    private readonly TextRunProperties? _properties;

    public BoxRun(
        double width, double height, double baseline, Color fill, string label,
        BaselineAlignment? alignment = BaselineAlignment.Baseline)
    {
        Size = new Size(width, height);
        Baseline = baseline;
        _fill = fill;
        _label = label;

        // Passing null here reproduces the "no Properties" case, which is what a naive
        // DrawableTextRun subclass gets by default.
        _properties = alignment is null
            ? null
            : new SpikeRunProperties(Typeface.Default, 16, baselineAlignment: alignment.Value);
    }

    public override Size Size { get; }
    public override double Baseline { get; }
    public override int Length => 1;
    public override TextRunProperties? Properties => _properties;

    /// <summary>Where Draw() was last called, so the probe can report origin semantics.</summary>
    public Point? LastDrawOrigin { get; private set; }

    public override void Draw(DrawingContext ctx, Point origin)
    {
        LastDrawOrigin = origin;

        // Box drawn DOWN from origin. Whether that is correct is exactly what probe (b) measures:
        // if origin is the baseline-left point, this paints below the baseline and looks wrong.
        var rect = new Rect(origin.X, origin.Y, Size.Width, Size.Height);
        ctx.DrawRectangle(new SolidColorBrush(_fill), new Pen(Brushes.Black, 1), rect);

        // A 3px marker exactly at the origin point, so a screenshot reveals the origin convention.
        ctx.DrawRectangle(Brushes.Magenta, null, new Rect(origin.X - 1.5, origin.Y - 1.5, 3, 3));
    }

    public override string ToString() => $"BoxRun[{_label} {Size.Width}x{Size.Height} baseline={Baseline}]";
}

/// <summary>A <see cref="DrawableTextRun"/> that paints a real bitmap inline (AC4's shape).</summary>
public sealed class ImageRun : DrawableTextRun
{
    private readonly Bitmap _bitmap;

    public ImageRun(Bitmap bitmap, double height, double baseline,
        BaselineAlignment alignment = BaselineAlignment.Baseline)
    {
        _bitmap = bitmap;
        var scale = height / bitmap.PixelSize.Height;
        Size = new Size(bitmap.PixelSize.Width * scale, height);
        Baseline = baseline;
        Properties = new SpikeRunProperties(Typeface.Default, 16, baselineAlignment: alignment);
    }

    public override Size Size { get; }
    public override double Baseline { get; }
    public override int Length => 1;
    public override TextRunProperties? Properties { get; }

    public override void Draw(DrawingContext ctx, Point origin)
        => ctx.DrawImage(_bitmap, new Rect(origin.X, origin.Y, Size.Width, Size.Height));

    public override string ToString() => $"ImageRun[{Size.Width:0.##}x{Size.Height:0.##} baseline={Baseline}]";

    /// <summary>Generates a checkerboard bitmap in code so the spike ships no binary assets.</summary>
    public static Bitmap CreateCheckerboard(int size = 32)
    {
        var bmp = new WriteableBitmap(
            new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);

        var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        {
            var dark = ((x / 8) + (y / 8)) % 2 == 0;
            var i = (y * size + x) * 4;
            pixels[i + 0] = dark ? (byte)0x20 : (byte)0xE0; // B
            pixels[i + 1] = dark ? (byte)0x80 : (byte)0xF0; // G
            pixels[i + 2] = dark ? (byte)0x20 : (byte)0xC0; // R
            pixels[i + 3] = 0xFF;                           // A
        }

        using (var fb = bmp.Lock())
        {
            Marshal.Copy(pixels, 0, fb.Address, pixels.Length);
        }

        return bmp;
    }
}

/// <summary>
/// The C2 shape from architecture.md §4.7: a custom <see cref="ITextSource"/> over flat text with
/// sorted style spans and <c>\uFFFC</c>-anchored embeds.
/// </summary>
public sealed class SpikeTextSource : ITextSource
{
    public const char ObjectReplacement = '\uFFFC';

    private readonly SpikeRunProperties _default;
    private readonly IReadOnlyList<StyleSpan> _spans;
    private readonly IReadOnlyDictionary<int, DrawableTextRun> _embeds;

    public SpikeTextSource(
        string text,
        SpikeRunProperties defaultProperties,
        IReadOnlyList<StyleSpan>? spans = null,
        IReadOnlyDictionary<int, DrawableTextRun>? embeds = null)
    {
        Text = text;
        _default = defaultProperties;
        _spans = spans ?? Array.Empty<StyleSpan>();
        _embeds = embeds ?? new Dictionary<int, DrawableTextRun>();

        foreach (var (index, run) in _embeds)
        {
            if (index < 0 || index >= text.Length || text[index] != ObjectReplacement)
            {
                throw new ArgumentException(
                    $"Embed at {index} is not anchored to a U+FFFC placeholder (found " +
                    $"{(index < text.Length ? "U+" + ((int)text[index]).ToString("X4") : "<past end>")}).");
            }

            if (run.Length != 1)
                throw new ArgumentException($"Embed at {index} has Length {run.Length}, expected 1.");
        }
    }

    public string Text { get; }

    /// <summary>Every index at which <see cref="GetTextRun"/> was asked for a run, in call order.</summary>
    public List<int> RequestedIndices { get; } = new();

    public TextRun? GetTextRun(int textSourceIndex)
    {
        RequestedIndices.Add(textSourceIndex);

        if (textSourceIndex >= Text.Length)
            return new TextEndOfParagraph();

        if (_embeds.TryGetValue(textSourceIndex, out var embed))
            return embed;

        var properties = _default;
        var end = Text.Length;

        foreach (var span in _spans)
        {
            if (span.Contains(textSourceIndex))
            {
                properties = span.Properties;
                end = Math.Min(end, span.End);
            }
            else if (span.Start > textSourceIndex)
            {
                end = Math.Min(end, span.Start);
            }
        }

        // Never run past the next embed, or its placeholder would be shaped as a glyph.
        foreach (var index in _embeds.Keys)
        {
            if (index > textSourceIndex)
                end = Math.Min(end, index);
        }

        return new TextCharacters(Text.AsMemory(textSourceIndex, end - textSourceIndex), properties);
    }
}
