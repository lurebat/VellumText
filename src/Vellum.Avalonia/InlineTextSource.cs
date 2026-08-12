using System.Collections.Immutable;
using Avalonia.Media.TextFormatting;

namespace Vellum.Avalonia;

/// <summary>
/// Presents one paragraph's <see cref="InlineContent"/> to Avalonia's text formatter, per
/// architecture 4.7.
/// </summary>
/// <remarks>
/// <para>
/// The formatter pulls runs by index and expects each call to return the longest run starting
/// there that shares one set of properties. Runs therefore end at the next mark boundary or
/// the next embed, whichever comes first, and the text is handed over as a
/// <see cref="ReadOnlyMemory{T}"/> slice of the paragraph's own string rather than copied.
/// </para>
/// <para>
/// This type is a view of an immutable <see cref="InlineContent"/>. Editing produces new
/// content and therefore a new source; nothing here mutates.
/// </para>
/// </remarks>
public sealed class InlineTextSource : ITextSource
{
    private readonly InlineContent _content;
    private readonly ITextStyleResolver _styles;
    private readonly IEmbedRenderer _embeds;

    /// <summary>Creates a source over one paragraph's content.</summary>
    /// <param name="content">The paragraph's inline content.</param>
    /// <param name="styles">Resolves mark sets to run properties.</param>
    /// <param name="embeds">Measures and draws embeds, or null for the placeholder renderer.</param>
    public InlineTextSource(
        InlineContent content,
        ITextStyleResolver styles,
        IEmbedRenderer? embeds = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(styles);

        _content = content;
        _styles = styles;
        _embeds = embeds ?? new PlaceholderEmbedRenderer();
    }

    /// <summary>The number of UTF-16 code units the formatter will be asked about.</summary>
    public int Length => _content.Length;

    /// <inheritdoc/>
    public TextRun? GetTextRun(int textSourceIndex)
    {
        // A negative index is the formatter asking about text before the paragraph, which for
        // a source that starts at zero means there is none.
        if (textSourceIndex < 0)
        {
            return null;
        }

        if (textSourceIndex >= _content.Length)
        {
            // Every paragraph ends with a break. Without it the formatter treats the end of
            // the text as the end of the whole source and a trailing empty line never forms.
            return new TextEndOfParagraph();
        }

        var marks = _content.MarkAt(textSourceIndex);
        var properties = _styles.Resolve(marks);

        if (_content.TryGetEmbedAt(textSourceIndex, out var embed))
        {
            return new EmbedRun(embed, properties, _embeds);
        }

        return new TextCharacters(
            _content.Text.AsMemory(textSourceIndex, RunEnd(textSourceIndex) - textSourceIndex),
            properties);
    }

    /// <summary>
    /// Where the run starting at <paramref name="start"/> ends: the next mark boundary, the
    /// next embed, or the end of the text.
    /// </summary>
    private int RunEnd(int start)
    {
        var end = _content.Length;
        var marks = _content.Marks;

        foreach (var span in marks)
        {
            // Spans are sorted and non-overlapping, so the first boundary strictly after the
            // start is the one that ends this run. A gap between spans is unformatted text
            // and its own run, which is why both edges of a span count as boundaries.
            if (span.Start > start)
            {
                end = Math.Min(end, span.Start);
                break;
            }

            if (span.End > start)
            {
                end = Math.Min(end, span.End);
                break;
            }
        }

        // An embed must be alone in its run, so the run before it stops at its placeholder.
        var placeholder = _content.Text.IndexOf(InlineContent.Placeholder, start + 1);

        if (placeholder >= 0)
        {
            end = Math.Min(end, placeholder);
        }

        return end;
    }

    /// <summary>
    /// The mark spans as Avalonia spans, for the simple <c>TextLayout</c> constructor's
    /// <c>textStyleOverrides</c> parameter.
    /// </summary>
    /// <remarks>
    /// This is the C1 fallback path from Increment 0. It expresses character formatting
    /// faithfully and cannot express embeds at all, so it is only usable for paragraphs with
    /// none. <see cref="GetTextRun"/> is the real path.
    /// </remarks>
    public ImmutableArray<global::Avalonia.Utilities.ValueSpan<TextRunProperties>> StyleOverrides()
    {
        var builder = ImmutableArray.CreateBuilder<
            global::Avalonia.Utilities.ValueSpan<TextRunProperties>>(_content.Marks.Length);

        foreach (var span in _content.Marks)
        {
            builder.Add(new global::Avalonia.Utilities.ValueSpan<TextRunProperties>(
                span.Start, span.Length, _styles.Resolve(span.Value)));
        }

        return builder.MoveToImmutable();
    }
}
