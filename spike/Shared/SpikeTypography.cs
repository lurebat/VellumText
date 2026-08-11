using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace Vellum.Spike;

/// <summary>
/// Concrete <see cref="TextRunProperties"/>. Written by hand rather than using
/// GenericTextRunProperties so the spike controls exactly what the formatter sees.
/// </summary>
public sealed class SpikeRunProperties : TextRunProperties
{
    public SpikeRunProperties(
        Typeface typeface,
        double fontRenderingEmSize = 16,
        IBrush? foreground = null,
        IBrush? background = null,
        TextDecorationCollection? decorations = null,
        BaselineAlignment baselineAlignment = BaselineAlignment.Baseline)
    {
        Typeface = typeface;
        FontRenderingEmSize = fontRenderingEmSize;
        ForegroundBrush = foreground ?? Brushes.Black;
        BackgroundBrush = background;
        TextDecorations = decorations;
        BaselineAlignment = baselineAlignment;
        CultureInfo = CultureInfo.InvariantCulture;
    }

    public override Typeface Typeface { get; }
    public override double FontRenderingEmSize { get; }
    public override TextDecorationCollection? TextDecorations { get; }
    public override IBrush? ForegroundBrush { get; }
    public override IBrush? BackgroundBrush { get; }
    public override BaselineAlignment BaselineAlignment { get; }
    public override CultureInfo? CultureInfo { get; }

    public SpikeRunProperties With(
        FontWeight? weight = null,
        FontStyle? style = null,
        double? size = null,
        IBrush? foreground = null)
    {
        var tf = new Typeface(
            Typeface.FontFamily,
            style ?? Typeface.Style,
            weight ?? Typeface.Weight,
            Typeface.Stretch);

        return new SpikeRunProperties(
            tf,
            size ?? FontRenderingEmSize,
            foreground ?? ForegroundBrush,
            BackgroundBrush,
            TextDecorations,
            BaselineAlignment);
    }

    public override string ToString()
        => $"{Typeface.FontFamily.Name} {Typeface.Weight} {Typeface.Style} {FontRenderingEmSize}px {Describe(ForegroundBrush)}";

    private static string Describe(IBrush? b)
        => b is ISolidColorBrush s ? s.Color.ToString() : b?.ToString() ?? "<null>";
}

/// <summary>Concrete <see cref="TextParagraphProperties"/>.</summary>
public sealed class SpikeParagraphProperties : TextParagraphProperties
{
    public SpikeParagraphProperties(
        TextRunProperties defaultProperties,
        TextWrapping wrapping = TextWrapping.Wrap,
        TextAlignment alignment = TextAlignment.Left,
        FlowDirection flowDirection = FlowDirection.LeftToRight,
        double lineHeight = double.NaN,
        bool firstLineInParagraph = true)
    {
        DefaultTextRunProperties = defaultProperties;
        TextWrapping = wrapping;
        TextAlignment = alignment;
        FlowDirection = flowDirection;
        LineHeight = lineHeight;
        FirstLineInParagraph = firstLineInParagraph;
    }

    public override FlowDirection FlowDirection { get; }
    public override TextAlignment TextAlignment { get; }
    public override double LineHeight { get; }
    public override bool FirstLineInParagraph { get; }
    public override TextRunProperties DefaultTextRunProperties { get; }
    public override TextWrapping TextWrapping { get; }
    public override double Indent => 0;
}
