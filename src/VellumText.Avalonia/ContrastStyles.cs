using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;

namespace VellumText.Avalonia;

/// <summary>The arithmetic of drawing a stated text colour readably on a background.</summary>
/// <remarks>
/// WCAG 2's contrast ratio, on its relative luminance. A colour below <see cref="Minimum"/> against
/// its background is unreadable enough to adapt; an adapted one is brought up to
/// <see cref="Target"/>, the ratio WCAG asks of body text. The two differ on purpose: a muted grey
/// an author chose at 4:1 on white is a design, not an accident, and is left alone.
/// </remarks>
internal static class TextContrast
{
    /// <summary>Below this a colour is adapted. WCAG's floor for large text.</summary>
    internal const double Minimum = 3;

    /// <summary>What an adapted colour is brought up to. WCAG AA for body text.</summary>
    internal const double Target = 4.5;

    /// <summary>The contrast ratio between two opaque colours, from 1 to 21.</summary>
    internal static double Ratio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);

        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    /// <summary>
    /// The colour to draw <paramref name="text"/> in on <paramref name="background"/>: itself when
    /// it is readable there, otherwise the same hue at a lightness that is.
    /// </summary>
    /// <remarks>
    /// The lightness is mirrored first, which is what dark modes in Office and mail clients do to a
    /// document's colours: black becomes white, navy becomes a light blue. Where the mirror is not
    /// far enough — a background of middling lightness — it is pushed on towards the far end, and
    /// failing that to black or white outright.
    /// </remarks>
    internal static Color Adapt(Color text, Color background)
    {
        var opaque = Color.FromRgb(text.R, text.G, text.B);

        if (Ratio(opaque, background) >= Minimum)
        {
            return text;
        }

        var hsl = opaque.ToHsl();
        var lighten = Luminance(background) < 0.5;
        var lightness = 1 - hsl.L;

        // Mirrored lightness that lands on the wrong side of the background is no use.
        lightness = lighten ? Math.Max(lightness, hsl.L) : Math.Min(lightness, hsl.L);

        for (var step = 0; step <= 20; step++)
        {
            var candidate = new HslColor(1, hsl.H, hsl.S, lightness).ToRgb();

            if (Ratio(candidate, background) >= Target)
            {
                return Color.FromArgb(text.A, candidate.R, candidate.G, candidate.B);
            }

            lightness = Math.Clamp(lightness + (lighten ? 0.05 : -0.05), 0, 1);
        }

        var extreme = lighten ? Colors.White : Colors.Black;

        return Color.FromArgb(text.A, extreme.R, extreme.G, extreme.B);
    }

    /// <summary><paramref name="top"/> painted over an opaque <paramref name="bottom"/>.</summary>
    internal static Color Over(Color top, Color bottom)
    {
        var a = top.A / 255.0;

        return Color.FromRgb(
            (byte)Math.Round((top.R * a) + (bottom.R * (1 - a))),
            (byte)Math.Round((top.G * a) + (bottom.G * (1 - a))),
            (byte)Math.Round((top.B * a) + (bottom.B * (1 - a))));
    }

    /// <summary><paramref name="top"/> painted over <paramref name="bottom"/>, either of which may be translucent.</summary>
    internal static Color Compose(Color top, Color bottom)
    {
        var a = top.A / 255.0;
        var b = bottom.A / 255.0;
        var alpha = a + (b * (1 - a));

        if (alpha <= 0)
        {
            return Colors.Transparent;
        }

        byte Channel(byte t, byte u) => (byte)Math.Round(((t * a) + (u * b * (1 - a))) / alpha);

        return Color.FromArgb(
            (byte)Math.Round(alpha * 255), Channel(top.R, bottom.R), Channel(top.G, bottom.G), Channel(top.B, bottom.B));
    }

    private static double Luminance(Color c) =>
        (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));

    private static double Linear(byte channel)
    {
        var s = channel / 255.0;

        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}

/// <summary>
/// A resolver that draws the colours a document states readably on the background behind them.
/// </summary>
/// <remarks>
/// <para>
/// A decorator, like the paragraph view's kind styles: the wrapped resolver — the derived one or
/// a host's own — still decides everything, and only the foreground brush it hands back is
/// replaced, and only where the marks named a colour. The document is never touched, so copying
/// and exporting keep the colour as written.
/// </para>
/// <para>
/// What the text is read against is the run's own highlight where it has one, otherwise the
/// backdrop: the block's fill, the cell's, or the editor's.
/// </para>
/// </remarks>
internal sealed class ContrastStyles : ITextStyleResolver
{
    private readonly Dictionary<MarkSet, TextRunProperties> _cache = [];
    private readonly ITextStyleResolver _inner;
    private readonly Color _backdrop;

    internal ContrastStyles(ITextStyleResolver inner, Color backdrop)
    {
        _inner = inner;
        _backdrop = backdrop;
    }

    public TextRunProperties Default => _inner.Default;

    /// <summary>The same adaptation against a different backdrop, partly or wholly covering this one.</summary>
    /// <param name="fill">What is painted over the backdrop: a code block's tint, a cell's fill.</param>
    /// <remarks>Shared per fill, so every code block in a document shares one cache.</remarks>
    internal ContrastStyles Over(Color fill)
    {
        if (!_over.TryGetValue(fill, out var styles))
        {
            styles = new ContrastStyles(_inner, TextContrast.Over(fill, _backdrop));
            _over[fill] = styles;
        }

        return styles;
    }

    private readonly Dictionary<Color, ContrastStyles> _over = [];

    public TextRunProperties Resolve(MarkSet marks)
    {
        if (marks.Foreground is null)
        {
            return _inner.Resolve(marks);
        }

        if (_cache.TryGetValue(marks, out var cached))
        {
            return cached;
        }

        var properties = _inner.Resolve(marks);
        var built = Adapted(marks, properties) ?? properties;

        _cache[marks] = built;

        return built;
    }

    private TextRunProperties? Adapted(MarkSet marks, TextRunProperties properties)
    {
        if (properties.ForegroundBrush is not ISolidColorBrush foreground)
        {
            return null;
        }

        var behind = marks.Highlight is { } highlight
            ? TextContrast.Over(highlight.ToAvalonia(), _backdrop)
            : _backdrop;
        var adapted = TextContrast.Adapt(foreground.Color, behind);

        if (adapted == foreground.Color)
        {
            return null;
        }

        return new GenericTextRunProperties(
            properties.Typeface,
            fontRenderingEmSize: properties.FontRenderingEmSize,
            textDecorations: properties.TextDecorations,
            foregroundBrush: new ImmutableSolidColorBrush(adapted, foreground.Opacity),
            backgroundBrush: properties.BackgroundBrush,
            baselineAlignment: properties.BaselineAlignment,
            cultureInfo: properties.CultureInfo ?? CultureInfo.CurrentCulture);
    }
}
