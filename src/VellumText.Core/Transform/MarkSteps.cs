using System.Collections.Immutable;

namespace VellumText;

/// <summary>
/// Shared machinery for the three steps that change formatting without moving anything.
/// </summary>
internal static class MarkStepSupport
{
    /// <summary>
    /// Applies a formatting change across every paragraph a range touches.
    /// </summary>
    internal static StepResult Apply(
        DocumentNode doc,
        int from,
        int to,
        MarkSet value,
        MarkFields fields)
    {
        if (Validate(doc, from, to) is { } bad)
        {
            return bad;
        }

        if (from == to || fields == MarkFields.None)
        {
            return StepResult.Ok(doc);
        }

        try
        {
            var result = TreeSurgery.MapParagraphs(
                doc,
                from,
                to,
                (paragraph, contentStart, start, end) =>
                    paragraph.WithContent(
                        paragraph.Content.ApplyMarks(start, end - start, value, fields)));

            return StepResult.Ok(result);
        }
        catch (ArgumentException ex)
        {
            // The only way here is an endpoint that splits a surrogate pair.
            return StepResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Records the formatting a range currently has, in document coordinates.
    /// </summary>
    /// <remarks>
    /// This is what makes formatting invertible. A single "make it bold" step cannot undo itself,
    /// because the range it covered may have been half bold already and one step of its own kind
    /// cannot express "restore this mixture". Capturing the mixture is the only honest inverse.
    /// </remarks>
    internal static ImmutableArray<ValueSpan<MarkSet>> Capture(DocumentNode doc, int from, int to)
    {
        var spans = ImmutableArray.CreateBuilder<ValueSpan<MarkSet>>();

        // Walked for its visits, not its result: the callback returns each paragraph unchanged,
        // so no part of the tree is rebuilt.
        TreeSurgery.MapParagraphs(
            doc,
            from,
            to,
            (paragraph, contentStart, start, end) =>
            {
                var runStart = start;
                var runValue = paragraph.Content.MarkAt(start);

                for (var offset = start + 1; offset <= end; offset++)
                {
                    var next = offset < end ? paragraph.Content.MarkAt(offset) : default;

                    if (offset == end || !next.Equals(runValue))
                    {
                        spans.Add(new ValueSpan<MarkSet>(
                            contentStart + runStart,
                            offset - runStart,
                            runValue));

                        runStart = offset;
                        runValue = next;
                    }
                }

                return paragraph;
            });

        return spans.ToImmutable();
    }

    private static StepResult? Validate(DocumentNode doc, int from, int to)
    {
        if (from < 0 || to < from || to > doc.ContentSize)
        {
            return StepResult.Failed($"range [{from}, {to}) is outside the document");
        }

        return null;
    }
}

/// <summary>
/// Turns on, or sets, the selected formatting fields across a range.
/// </summary>
/// <param name="From">Where the range starts.</param>
/// <param name="To">Where it ends.</param>
/// <param name="Value">Supplies the new value of each selected field.</param>
/// <param name="Fields">Which fields to change.</param>
/// <remarks>
/// One step covers both "make it bold" and "make it 14pt red", because a mark is a record of
/// fields rather than a set of named marks. The field mask is what keeps setting the colour from
/// silently clearing the boldness.
/// </remarks>
public sealed record AddMarkStep(int From, int To, MarkSet Value, MarkFields Fields) : Step
{
    /// <inheritdoc/>
    public override StepResult Apply(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        return MarkStepSupport.Apply(doc, From, To, Value, Fields);
    }

    /// <inheritdoc/>
    public override Step Invert(DocumentNode docBefore)
    {
        ArgumentNullException.ThrowIfNull(docBefore);

        return new RestoreMarksStep(
            From,
            To,
            MarkStepSupport.Capture(docBefore, From, To));
    }

    /// <inheritdoc/>
    public override StepMap GetMap() => StepMap.Identity;

    /// <inheritdoc/>
    public override Step? Map(Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var from = mapping.MapWithResult(From, Assoc.After);
        var to = mapping.MapWithResult(To, Assoc.Before);

        // The ends bind to opposite edges, so an insertion inside the range can carry them past
        // one another. Either way there is no text left to format, and an inverted range is a
        // step nothing downstream could apply.
        return to.Pos <= from.Pos ? null : this with { From = from.Pos, To = to.Pos };
    }
}

/// <summary>
/// Clears the selected formatting fields across a range.
/// </summary>
/// <param name="From">Where the range starts.</param>
/// <param name="To">Where it ends.</param>
/// <param name="Fields">Which fields to clear.</param>
public sealed record RemoveMarkStep(int From, int To, MarkFields Fields) : Step
{
    /// <inheritdoc/>
    public override StepResult Apply(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        return MarkStepSupport.Apply(doc, From, To, MarkSet.Empty, Fields);
    }

    /// <inheritdoc/>
    public override Step Invert(DocumentNode docBefore)
    {
        ArgumentNullException.ThrowIfNull(docBefore);

        return new RestoreMarksStep(
            From,
            To,
            MarkStepSupport.Capture(docBefore, From, To));
    }

    /// <inheritdoc/>
    public override StepMap GetMap() => StepMap.Identity;

    /// <inheritdoc/>
    public override Step? Map(Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var from = mapping.MapWithResult(From, Assoc.After);
        var to = mapping.MapWithResult(To, Assoc.Before);

        // The ends bind to opposite edges, so an insertion inside the range can carry them past
        // one another. Either way there is no text left to format, and an inverted range is a
        // step nothing downstream could apply.
        return to.Pos <= from.Pos ? null : this with { From = from.Pos, To = to.Pos };
    }
}

/// <summary>
/// Puts back an exact recorded run of formatting. The inverse of the other two mark steps.
/// </summary>
/// <param name="From">Where the recorded range started.</param>
/// <param name="To">Where it ended.</param>
/// <param name="Spans">
/// The formatting each stretch had, in document coordinates, covering the range exactly.
/// </param>
/// <remarks>
/// <para>
/// This exists rather than reusing a replace step because a replace step would report a map of
/// <c>StepMap(from, len, len)</c>, which collapses every interior position onto one of the range's
/// two edges. Undoing a bold command would then scatter every cursor and selection inside it.
/// Formatting moves nothing, so its map must be the identity, and only a step that changes marks
/// in place can honestly claim that.
/// </para>
/// <para>
/// It is not meant to be built by hand; it is what the other two steps invert into.
/// </para>
/// </remarks>
public sealed record RestoreMarksStep(
    int From,
    int To,
    ImmutableArray<ValueSpan<MarkSet>> Spans) : Step
{
    /// <inheritdoc/>
    public override StepResult Apply(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (From < 0 || To < From || To > doc.ContentSize)
        {
            return StepResult.Failed($"range [{From}, {To}) is outside the document");
        }

        if (Spans.IsDefaultOrEmpty)
        {
            return StepResult.Ok(doc);
        }

        try
        {
            var ordered = InDocumentOrder(Spans);
            var result = TreeSurgery.MapParagraphs(
                doc,
                From,
                To,
                (paragraph, contentStart, start, end) =>
                {
                    var content = paragraph.Content;

                    // Only the spans that reach into this paragraph. Every span was tried against
                    // every paragraph, which made undoing a format across the document quadratic:
                    // 272 ms at 8,000 plain paragraphs and 4 s with every other word bold.
                    var first = ordered ? FirstEndingAfter(contentStart + start) : 0;

                    for (var i = first; i < Spans.Length; i++)
                    {
                        var span = Spans[i];

                        WorkCounters.RestoredSpanVisits++;

                        if (ordered && span.Start >= contentStart + end)
                        {
                            break;
                        }

                        var lo = Math.Max(span.Start - contentStart, start);
                        var hi = Math.Min(span.End - contentStart, end);

                        if (hi > lo)
                        {
                            content = content.ApplyMarks(
                                lo,
                                hi - lo,
                                span.Value,
                                MarkFields.All);
                        }
                    }

                    return paragraph.WithContent(content);
                });

            return StepResult.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return StepResult.Failed(ex.Message);
        }
    }

    /// <summary>
    /// Whether every span starts at or after the end of the one before it, which is how a captured
    /// inverse records them and what makes searching them sound.
    /// </summary>
    /// <remarks>
    /// The type is public and a hand-built step can list its spans in any order, or overlap them.
    /// Such a step is applied the way it always was — every span against every paragraph, later
    /// spans overwriting earlier ones — so that only its speed depends on the order, never its
    /// result.
    /// </remarks>
    private static bool InDocumentOrder(ImmutableArray<ValueSpan<MarkSet>> spans)
    {
        for (var i = 1; i < spans.Length; i++)
        {
            if (spans[i].Start < spans[i - 1].End)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The index of the first span that ends after a position.</summary>
    /// <param name="position">A document position.</param>
    /// <remarks>Only meaningful when the spans are in document order.</remarks>
    private int FirstEndingAfter(int position)
    {
        var low = 0;
        var high = Spans.Length;

        while (low < high)
        {
            var mid = low + ((high - low) / 2);

            if (Spans[mid].End > position)
            {
                high = mid;
            }
            else
            {
                low = mid + 1;
            }
        }

        return low;
    }

    /// <inheritdoc/>
    public override Step Invert(DocumentNode docBefore)
    {
        ArgumentNullException.ThrowIfNull(docBefore);

        return new RestoreMarksStep(From, To, MarkStepSupport.Capture(docBefore, From, To));
    }

    /// <inheritdoc/>
    public override StepMap GetMap() => StepMap.Identity;

    /// <inheritdoc/>
    public override Step? Map(Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        // Deliberately not remapped. The spans are offsets into paragraphs that the intervening
        // steps may have cut, joined or reflowed, and there is no sound way to carry a recorded
        // formatting run across that. History rebases whole transactions rather than reordering
        // an undo behind a later edit, so this case does not arise there; anywhere else, dropping
        // the step is the safe answer.
        return mapping.IsIdentity ? this : null;
    }

    /// <inheritdoc/>
    public bool Equals(RestoreMarksStep? other) =>
        other is not null
        && From == other.From
        && To == other.To
        && Spans.AsSpan().SequenceEqual(other.Spans.AsSpan());

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(From);
        hash.Add(To);

        foreach (var span in Spans)
        {
            hash.Add(span);
        }

        return hash.ToHashCode();
    }
}
