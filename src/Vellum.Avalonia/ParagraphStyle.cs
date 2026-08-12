using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace Vellum.Avalonia;

/// <summary>
/// How a <see cref="ParagraphKind"/> is presented: the block-level part of a paragraph's look,
/// as distinct from the character-level part a <see cref="MarkSet"/> carries.
/// </summary>
/// <remarks>
/// <para>
/// The split matters. A heading is not "bold 24pt text" in the document — it is a heading, and
/// the size and weight are presentation the host is free to change. So the model stores the kind
/// and this decides what it looks like, which is why a heading whose words were never marked
/// bold stops being bold the moment it becomes a body paragraph.
/// </para>
/// <para>
/// <see cref="FontScale"/> multiplies whatever size the marks resolved to rather than replacing
/// it, so explicitly sized text inside a heading keeps its relative size. <see cref="Weight"/>
/// and <see cref="Family"/> are defaults the marks override, not overrides of the marks: a word
/// set to a specific family inside a code block keeps that family.
/// </para>
/// </remarks>
/// <param name="FontScale">A multiplier on the resolved font size. Must be positive and finite.</param>
/// <param name="Weight">The weight to use where the marks ask for none.</param>
/// <param name="Family">The family to use where the marks ask for none.</param>
/// <param name="SpaceBefore">Empty space above the block, in device pixels.</param>
/// <param name="SpaceAfter">Empty space below the block, in device pixels.</param>
/// <param name="Bar">A rule drawn down the left edge, as a blockquote has, or null for none.</param>
/// <param name="BarWidth">How wide that rule is.</param>
/// <param name="BarGap">How far the text sits from the rule.</param>
/// <param name="Background">A fill behind the whole block, as a code block has, or null for none.</param>
/// <remarks>
/// A reference type on purpose. As a struct its default value would have a
/// <see cref="FontScale"/> of zero — a member initialiser cannot reach <c>default(T)</c> for a
/// value type — and every run in the paragraph would be laid out at nothing. Measured: the text
/// formatter rejects it outright with "Invalid FontRenderingEmSize".
/// </remarks>
public sealed record ParagraphStyle(
    double FontScale = 1,
    FontWeight? Weight = null,
    FontFamily? Family = null,
    double SpaceBefore = 0,
    double SpaceAfter = 0,
    IBrush? Bar = null,
    double BarWidth = 0,
    double BarGap = 0,
    IBrush? Background = null)
{
    /// <summary>How far the text is inset from the block's left edge by the bar, if there is one.</summary>
    public double LeftInset => Bar is null ? 0 : BarWidth + BarGap;

    /// <summary>The presentation of an ordinary body paragraph: no change to anything.</summary>
    public static ParagraphStyle Body { get; } = new();
}

/// <summary>Decides what each <see cref="ParagraphKind"/> looks like.</summary>
public interface IParagraphStyleResolver
{
    /// <summary>The presentation of a paragraph kind.</summary>
    /// <param name="kind">The kind to resolve.</param>
    ParagraphStyle Resolve(ParagraphKind kind);
}

/// <summary>
/// The default <see cref="IParagraphStyleResolver"/>: headings on a modest scale, a bar down the
/// side of a quote, and a monospace fill behind code.
/// </summary>
/// <remarks>
/// The heading scale is a geometric-ish ramp rather than the HTML defaults, which put h4 and
/// below at or under body size and make a six-level hierarchy read as five. Every number here is
/// a default a host is free to replace by supplying its own resolver.
/// </remarks>
public sealed class ParagraphStyleResolver : IParagraphStyleResolver
{
    private static readonly double[] HeadingScales = [2.0, 1.6, 1.35, 1.2, 1.1, 1.0];

    private readonly IBrush _bar;
    private readonly IBrush _codeBackground;
    private readonly FontFamily? _codeFamily;
    private readonly double _spacing;

    /// <summary>Creates a resolver.</summary>
    /// <param name="bar">The brush for a quote's bar, or null for a mid grey.</param>
    /// <param name="codeBackground">The fill behind a code block, or null for a faint grey.</param>
    /// <param name="codeFamily">The family for a code block, or null to leave it to the marks.</param>
    /// <param name="spacing">
    /// The space a body paragraph leaves after itself, in device pixels. Headings scale from it.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="spacing"/> is negative or not finite.</exception>
    public ParagraphStyleResolver(
        IBrush? bar = null,
        IBrush? codeBackground = null,
        FontFamily? codeFamily = null,
        double spacing = 6)
    {
        if (spacing < 0 || !double.IsFinite(spacing))
        {
            throw new ArgumentOutOfRangeException(
                nameof(spacing), spacing, "Spacing must be a non-negative finite number.");
        }

        _bar = bar ?? new ImmutableSolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC0));
        _codeBackground = codeBackground
            ?? new ImmutableSolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80));
        _codeFamily = codeFamily;
        _spacing = spacing;
    }

    /// <inheritdoc/>
    public ParagraphStyle Resolve(ParagraphKind kind) => kind switch
    {
        ParagraphKind.Heading1 => Heading(0),
        ParagraphKind.Heading2 => Heading(1),
        ParagraphKind.Heading3 => Heading(2),
        ParagraphKind.Heading4 => Heading(3),
        ParagraphKind.Heading5 => Heading(4),
        ParagraphKind.Heading6 => Heading(5),

        ParagraphKind.Quote => new ParagraphStyle(
            SpaceBefore: _spacing,
            SpaceAfter: _spacing,
            Bar: _bar,
            BarWidth: 3,
            BarGap: _spacing + 2),

        ParagraphKind.Code => new ParagraphStyle(
            FontScale: 0.95,
            Family: _codeFamily,
            SpaceBefore: _spacing,
            SpaceAfter: _spacing,
            Background: _codeBackground),

        _ => new ParagraphStyle(SpaceAfter: _spacing),
    };

    private ParagraphStyle Heading(int level) => new(
        FontScale: HeadingScales[level],
        Weight: FontWeight.Bold,

        // A heading belongs to what follows it, so it leaves more room above than below.
        SpaceBefore: _spacing * (2.0 - (level * 0.25)),
        SpaceAfter: _spacing * (0.8 - (level * 0.1)));
}
