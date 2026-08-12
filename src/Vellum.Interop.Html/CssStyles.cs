using System.Globalization;
using AngleSharp.Css.Dom;
using AngleSharp.Css.Values;
using AngleSharp.Dom;

namespace Vellum.Interop.Html;

/// <summary>
/// Turns CSS declarations into the formatting the document model has.
/// </summary>
/// <remarks>
/// The reading of CSS is AngleSharp's; what is here is only the mapping onto a much smaller set of
/// concepts. Most of CSS has no equivalent in a document model and is simply not looked at.
/// </remarks>
internal static class CssStyles
{
    /// <summary>The largest indent level a margin can be translated into.</summary>
    private const int MaxIndentLevel = 8;

    /// <summary>
    /// The width of one indent level in pixels. Word emits half-inch steps, which at the CSS
    /// reference resolution of 96 dpi is 48 pixels; browsers use 40 for a list. Splitting the
    /// difference would mis-round both, so Word's number wins — it is the one that arrives most.
    /// </summary>
    private const double IndentPixels = 48;

    /// <summary>Reads the inline style of an element, or null when it has none.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The declaration, or null.</returns>
    internal static ICssStyleDeclaration? StyleOf(IElement element)
    {
        // An element only has a style declaration when the document was built with CSS support and
        // the attribute survived sanitizing. Both are true here, but neither is guaranteed by the
        // type, so this stays a question rather than an assumption.
        if (!element.HasAttribute("style"))
        {
            return null;
        }

        try
        {
            return element.GetStyle();
        }
        catch (Exception)
        {
            // A declaration AngleSharp cannot make sense of is not worth failing an import over.
            return null;
        }
    }

    /// <summary>Applies the character formatting an inline style asks for.</summary>
    /// <param name="mark">The formatting inherited from enclosing elements.</param>
    /// <param name="style">The declaration.</param>
    /// <returns>The formatting for content inside the element.</returns>
    internal static MarkSet ApplyInline(MarkSet mark, ICssStyleDeclaration style)
    {
        mark = ApplyWeight(mark, style.GetPropertyValue("font-weight"));
        mark = ApplySlant(mark, style.GetPropertyValue("font-style"));
        mark = ApplyDecoration(mark, style.GetPropertyValue("text-decoration-line"));
        mark = ApplyDecoration(mark, style.GetPropertyValue("text-decoration"));
        mark = ApplyVerticalAlign(mark, style.GetPropertyValue("vertical-align"));

        if (TryColor(style.GetPropertyValue("color"), out var foreground))
        {
            mark = mark with { Foreground = foreground };
        }

        if (TryColor(style.GetPropertyValue("background-color"), out var background))
        {
            // A fully transparent background is the absence of a highlight, not a clear one.
            mark = mark with { Highlight = background.IsTransparent ? null : background };
        }

        if (TryFontFamily(style.GetPropertyValue("font-family"), out var family))
        {
            mark = mark with { FontFamily = family };
        }

        if (TryLength(style.GetPropertyValue("font-size"), out var size) && size > 0)
        {
            mark = mark with { FontSize = size };
        }

        return mark;
    }

    private static MarkSet ApplyWeight(MarkSet mark, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return mark;
        }

        // "bolder" and "lighter" are relative to a parent weight this model does not track. Read
        // as the absolute they usually stand in for; it is right far more often than ignoring them.
        var bold = value switch
        {
            "bold" or "bolder" => true,
            "normal" or "lighter" => false,
            _ => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? n >= 600
                : (bool?)null,
        };

        return bold switch
        {
            true => mark with { Style = mark.Style | TextStyle.Bold },
            false => mark with { Style = mark.Style & ~TextStyle.Bold },
            null => mark,
        };
    }

    private static MarkSet ApplySlant(MarkSet mark, string? value) => value switch
    {
        "italic" or "oblique" => mark with { Style = mark.Style | TextStyle.Italic },
        "normal" => mark with { Style = mark.Style & ~TextStyle.Italic },
        _ => mark,
    };

    private static MarkSet ApplyDecoration(MarkSet mark, string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return mark;
        }

        // "text-decoration" is a shorthand, so its value can carry a style and a colour as well as
        // the line. Only the words that name lines are of interest.
        var style = mark.Style;

        if (value.Contains("underline", StringComparison.OrdinalIgnoreCase))
        {
            style |= TextStyle.Underline;
        }

        if (value.Contains("line-through", StringComparison.OrdinalIgnoreCase))
        {
            style |= TextStyle.Strikethrough;
        }

        // "none" turns both off, but only when it is the whole value: it is also a legal value for
        // the style part of the shorthand, where it means something else entirely.
        if (value.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            style &= ~(TextStyle.Underline | TextStyle.Strikethrough);
        }

        return mark with { Style = style };
    }

    private static MarkSet ApplyVerticalAlign(MarkSet mark, string? value)
    {
        // Sub and Super are mutually exclusive in the model and it enforces that on assignment, so
        // each has to clear the other rather than simply being set.
        var style = mark.Style;

        switch (value)
        {
            case "super":
                style = (style & ~TextStyle.Sub) | TextStyle.Super;
                break;

            case "sub":
                style = (style & ~TextStyle.Super) | TextStyle.Sub;
                break;

            case "baseline":
                style &= ~(TextStyle.Sub | TextStyle.Super);
                break;

            default:
                return mark;
        }

        return mark with { Style = style };
    }

    /// <summary>Applies the paragraph formatting an inline style asks for.</summary>
    /// <param name="block">The formatting inherited from enclosing elements.</param>
    /// <param name="style">The declaration.</param>
    /// <returns>The formatting for paragraphs inside the element.</returns>
    internal static BlockStyle ApplyBlock(BlockStyle block, ICssStyleDeclaration style)
    {
        var align = style.GetPropertyValue("text-align") switch
        {
            "left" or "start" => TextAlign.Left,
            "center" => TextAlign.Center,
            "right" or "end" => TextAlign.Right,
            "justify" => TextAlign.Justify,
            _ => (TextAlign?)null,
        };

        if (align is not null)
        {
            block = block with { Align = align.Value };
        }

        // A margin and a padding on the same element both push the text right, and Word uses one
        // where a browser uses the other, so whichever is larger is the indent the author meant.
        var margin = Math.Max(
            LengthOrZero(style.GetPropertyValue("margin-left")),
            LengthOrZero(style.GetPropertyValue("padding-left")));

        if (margin > 0)
        {
            var levels = (int)Math.Round(margin / IndentPixels, MidpointRounding.AwayFromZero);

            if (levels > 0)
            {
                block = block with { Indent = Math.Min(block.Indent + levels, MaxIndentLevel) };
            }
        }

        return block;
    }

    private static double LengthOrZero(string? value) =>
        TryLength(value, out var length) && length > 0 ? length : 0;

    /// <summary>
    /// Reads a CSS length as a number of pixels, for the absolute units only.
    /// </summary>
    /// <remarks>
    /// Relative units are deliberately not resolved. An <c>em</c> or a percentage is a fraction of
    /// something this importer does not have — there is no cascade, no viewport and no layout — so
    /// a number invented for one would be a guess presented as a measurement.
    /// </remarks>
    internal static bool TryLength(string? value, out double pixels)
    {
        pixels = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var unitStart = text.Length;

        while (unitStart > 0 && !char.IsAsciiDigit(text[unitStart - 1]) && text[unitStart - 1] != '.')
        {
            unitStart--;
        }

        var number = text[..unitStart];
        var unit = text[unitStart..].Trim().ToLowerInvariant();

        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var magnitude)
            || !double.IsFinite(magnitude))
        {
            return false;
        }

        // The conversions are the CSS absolute ones, against the reference 96 pixels per inch.
        var factor = unit switch
        {
            "px" or "" => 1.0,
            "pt" => 96.0 / 72.0,
            "pc" => 16.0,
            "in" => 96.0,
            "cm" => 96.0 / 2.54,
            "mm" => 9.6 / 2.54,
            "q" => 2.4 / 2.54,
            _ => 0.0,
        };

        if (factor == 0)
        {
            return false;
        }

        pixels = magnitude * factor;
        return double.IsFinite(pixels);
    }

    /// <summary>Reads a CSS colour.</summary>
    /// <param name="value">The declared value.</param>
    /// <param name="color">The colour.</param>
    /// <returns>Whether the value named a colour.</returns>
    /// <remarks>
    /// The named-colour table, the hex forms and the conversion out of the cylindrical spaces are
    /// all AngleSharp's. What is here is the dispatch between them, because there is no single
    /// entry point that accepts any colour value.
    /// </remarks>
    internal static bool TryColor(string? value, out Rgba color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            color = Rgba.Transparent;
            return true;
        }

        // "currentColor" and "inherit" name whatever the surrounding text is, which is exactly what
        // leaving the mark unset already means.
        if (text.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
            || text.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            || text.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CssColorValue parsed;

        if (text.StartsWith('#'))
        {
            if (!CssColorValue.TryFromHex(text[1..], out parsed))
            {
                return false;
            }
        }
        else if (text.Contains('('))
        {
            if (!TryFunctional(text, out parsed))
            {
                return false;
            }
        }
        else
        {
            var named = CssColorValue.FromName(text);

            if (named is null)
            {
                return false;
            }

            parsed = named.Value;
        }

        color = new Rgba(parsed.R, parsed.G, parsed.B, parsed.A);
        return true;
    }

    private static bool TryFunctional(string text, out CssColorValue color)
    {
        color = default;

        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');

        if (close <= open)
        {
            return false;
        }

        var name = text[..open].Trim().ToLowerInvariant();
        var args = text[(open + 1)..close]
            .Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (args.Length < 3)
        {
            return false;
        }

        if (!TryNumber(args[0], out var a)
            || !TryNumber(args[1], out var b)
            || !TryNumber(args[2], out var c))
        {
            return false;
        }

        var alpha = 1.0;

        if (args.Length >= 4 && TryNumber(args[3], out var declared))
        {
            // A percentage alpha has already been divided by a hundred by TryNumber.
            alpha = Math.Clamp(args[3].EndsWith('%') ? declared / 100.0 : declared, 0, 1);
        }

        switch (name)
        {
            case "rgb":
            case "rgba":
                color = CssColorValue.FromRgba(
                    Math.Clamp(args[0].EndsWith('%') ? a * 2.55 : a, 0, 255),
                    Math.Clamp(args[1].EndsWith('%') ? b * 2.55 : b, 0, 255),
                    Math.Clamp(args[2].EndsWith('%') ? c * 2.55 : c, 0, 255),
                    alpha);
                return true;

            case "hsl":
            case "hsla":
                color = CssColorValue.FromHsla(a / 360.0, Math.Clamp(b / 100.0, 0, 1), Math.Clamp(c / 100.0, 0, 1), alpha);
                return true;

            default:
                return false;
        }
    }

    private static bool TryNumber(string text, out double value)
    {
        var trimmed = text.EndsWith('%') ? text[..^1] : text;

        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    /// <summary>Reads the first usable name out of a font stack.</summary>
    /// <param name="value">The declared value.</param>
    /// <param name="family">The family name.</param>
    /// <returns>Whether a name was found.</returns>
    /// <remarks>
    /// Only the first is taken. The rest of a stack is a list of what to do when the first is
    /// missing, which is a question for the text layer at the moment it resolves a typeface, not
    /// something the document can answer now.
    /// </remarks>
    internal static bool TryFontFamily(string? value, out string? family)
    {
        family = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var first = value.Split(',')[0].Trim().Trim('"', '\'').Trim();

        if (first.Length == 0)
        {
            return false;
        }

        // The generic families name a category rather than a font. The model stores a family name
        // that will be handed to the platform, and no platform has a font called "sans-serif".
        if (first.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            || first.Equals("initial", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        family = first switch
        {
            _ when first.Equals("monospace", StringComparison.OrdinalIgnoreCase) => "Consolas",
            _ when first.Equals("serif", StringComparison.OrdinalIgnoreCase) => "Times New Roman",
            _ when first.Equals("sans-serif", StringComparison.OrdinalIgnoreCase) => "Segoe UI",
            _ when first.Equals("cursive", StringComparison.OrdinalIgnoreCase) => null,
            _ when first.Equals("fantasy", StringComparison.OrdinalIgnoreCase) => null,
            _ when first.Equals("system-ui", StringComparison.OrdinalIgnoreCase) => null,
            _ => first,
        };

        return family is not null;
    }
}
