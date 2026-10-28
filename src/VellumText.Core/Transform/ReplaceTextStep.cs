using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace VellumText;

/// <summary>One stretch of paragraph text to replace, and what to put there.</summary>
/// <param name="From">Where the replaced range starts.</param>
/// <param name="To">Where it ends. Equal to <paramref name="From"/> for a pure insertion.</param>
/// <param name="Content">The text, with its formatting, that takes the range's place.</param>
public readonly record struct TextReplacement(int From, int To, InlineContent Content);

/// <summary>
/// Replaces several stretches of paragraph text at once, in one rebuild of the document.
/// </summary>
/// <param name="Replacements">
/// The replacements, in ascending document order, each starting strictly after the one before it
/// ends, and each within the text of a single paragraph.
/// </param>
/// <remarks>
/// <para>
/// Replace All is what this is for. Written as one <see cref="ReplaceStep"/> per match, every step
/// rebuilt the spine of the document — or the whole of a long paragraph — and undo replayed one
/// inverted step per match, so replacing a word in every paragraph was quadratic in the document:
/// 8,000 paragraphs allocated 1.6 GB, and as much again to undo. This step rewrites each paragraph
/// it touches once and the tree above them once, and its inverse is one step of the same kind.
/// </para>
/// <para>
/// The map is exactly what the per-match steps report, not an approximation of it: one
/// <see cref="ReplacedRange"/> per replacement, so a caret between two matches stays between them.
/// That is why two replacements may not touch. A <see cref="StepMap"/> merges touching ranges, and
/// a position on the seam would then be moved to an edge of the union rather than where the two
/// separate replacements leave it. Replacements that touch go in separate steps instead; see
/// <see cref="Batches"/>.
/// </para>
/// <para>
/// The inverse holds, for each replacement, its range in the new document and the text it
/// displaced.
/// </para>
/// <para>
/// Refused, as a failed <see cref="StepResult"/> like every other step's refusal: a missing list
/// or a missing content; replacements out of order, overlapping or touching; a range inverted,
/// outside the document, or not within one paragraph's text; and a range edge inside a surrogate
/// pair; and a result the schema rejects.
/// </para>
/// </remarks>
public sealed record ReplaceTextStep(ImmutableArray<TextReplacement> Replacements) : Step
{
    /// <inheritdoc/>
    public override StepResult Apply(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var groups = Gather(doc, out var failure);

        if (failure is not null)
        {
            return StepResult.Failed(failure);
        }

        // Nothing to replace leaves the very same document, which a transaction drops rather than
        // recording an undo entry that undoes nothing.
        if (groups.Count == 0)
        {
            return StepResult.Ok(doc);
        }

        try
        {
            var rewritten = new List<(int Start, ParagraphNode Paragraph)>(groups.Count);

            foreach (var group in groups)
            {
                var edits = new (int Start, int Length, InlineContent Content)[group.Count];

                for (var i = 0; i < group.Count; i++)
                {
                    var replacement = Replacements[group.First + i];

                    edits[i] = (
                        replacement.From - group.Start,
                        replacement.To - replacement.From,
                        replacement.Content);
                }

                rewritten.Add((
                    group.Start,
                    group.Paragraph.WithContent(group.Paragraph.Content.ReplaceMany(edits))));
            }

            BlockNode[] blocks = [.. doc.Blocks];
            var next = 0;

            PutBack(doc, 0, blocks, rewritten, ref next);

            // Run past the schema like every other step's result. No rule reads paragraph text or
            // formatting today, so this refuses nothing yet - but a rule that one day does must
            // not be skipped by the one step that rewrites the most text, and against the document
            // before, the walk is the spines of the paragraphs replaced: linear, like the rest.
            // Handed over rather than copied: the array is new and nothing else holds it.
            return ReplaceStep.Validated(
                new DocumentNode(ImmutableCollectionsMarshal.AsImmutableArray(blocks)), doc);
        }
        catch (ArgumentException ex)
        {
            // A range edge inside a surrogate pair is refused by the content it would cut, as it is
            // for every other step: a failure, not a crash.
            return StepResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    /// <exception cref="InvalidOperationException">
    /// This step does not apply to <paramref name="docBefore"/>.
    /// </exception>
    public override Step Invert(DocumentNode docBefore)
    {
        ArgumentNullException.ThrowIfNull(docBefore);

        var groups = Gather(docBefore, out var failure);

        if (failure is not null)
        {
            throw new InvalidOperationException(
                "The step cannot be inverted against a document it does not apply to: " + failure);
        }

        var inverse = new TextReplacement[Replacements.Length];
        var shift = 0;

        foreach (var group in groups)
        {
            var ranges = new (int Start, int Length)[group.Count];

            for (var i = 0; i < group.Count; i++)
            {
                var replacement = Replacements[group.First + i];

                ranges[i] = (replacement.From - group.Start, replacement.To - replacement.From);
            }

            var displaced = group.Paragraph.Content.Excerpts(ranges);

            for (var i = 0; i < group.Count; i++)
            {
                var replacement = Replacements[group.First + i];
                var from = replacement.From + shift;

                inverse[group.First + i] =
                    new TextReplacement(from, from + replacement.Content.Length, displaced[i]);

                shift += replacement.Content.Length - (replacement.To - replacement.From);
            }
        }

        return new ReplaceTextStep(ImmutableCollectionsMarshal.AsImmutableArray(inverse));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// One range per replacement, in the coordinates of the document the step applies to: the
    /// ranges the per-replacement steps report when they are applied back to front.
    /// </remarks>
    public override StepMap GetMap() =>
        StepMap.Of(Replacements.Select(r => new ReplacedRange(r.From, r.To - r.From, r.Content.Length)));

    /// <inheritdoc/>
    /// <remarks>
    /// Each range is rebased as a <see cref="ReplaceStep"/>'s is, its two ends binding outwards, and
    /// one whose ends were both deleted is dropped. Mapping never reorders positions, so the ranges
    /// stay in order; but concurrent edits can bring two of them together, and the rebased step
    /// then refuses to apply rather than merging them.
    /// </remarks>
    public override Step? Map(Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var mapped = ImmutableArray.CreateBuilder<TextReplacement>(Replacements.Length);

        foreach (var replacement in Replacements)
        {
            var from = mapping.MapWithResult(replacement.From, Assoc.After);
            var to = mapping.MapWithResult(replacement.To, Assoc.Before);

            if (from.Deleted && to.Deleted)
            {
                continue;
            }

            mapped.Add(replacement with { From = from.Pos, To = Math.Max(from.Pos, to.Pos) });
        }

        return mapped.Count == 0 ? null : new ReplaceTextStep(mapped.ToImmutable());
    }

    /// <summary>Whether two steps make the same replacements.</summary>
    /// <param name="other">The step to compare against.</param>
    /// <remarks>
    /// Written out because the compiler's would compare the two lists by reference, and history
    /// compares recorded steps by value.
    /// </remarks>
    public bool Equals(ReplaceTextStep? other) =>
        other is not null && Replacements.AsSpan().SequenceEqual(other.Replacements.AsSpan());

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();

        foreach (var replacement in Replacements.AsSpan())
        {
            hash.Add(replacement);
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// The steps that make a list of replacements, splitting it only where two of them touch.
    /// </summary>
    /// <param name="replacements">
    /// The replacements, ascending and not overlapping, all measured against the document the
    /// first step applies to. Unlike a step's own, two of them may touch.
    /// </param>
    /// <returns>
    /// The steps, to apply in order. One, unless two replacements touch, which a search's matches
    /// do only when one ends exactly where the next begins. A later step's ranges are moved by the
    /// replacements before them in the earlier steps.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Each replacement goes into the first step it would not touch the last replacement of, in
    /// that step's own coordinates — after the steps before it have run, so two replacements that
    /// only come together once everything between them has been deleted are kept apart too. Two
    /// abutting matches land in different steps, and a run of them alternates.
    /// </para>
    /// <para>
    /// The result is exactly the per-replacement steps' when every replacement replaces something,
    /// however they are shared out. A replacement's map collapses whatever it covers onto one of
    /// its edges, and pins a position on an edge to that edge, whatever has been applied before
    /// it; so a position on the seam of two touching replacements lands on the seam and is
    /// reported deleted in either order, and replacements that do not touch do not interact.
    /// </para>
    /// <para>
    /// A pure insertion breaks that: where it lands against a touching neighbour depends on which
    /// of the two was applied first, and the old order was back to front. A list with an empty
    /// range in it is therefore made one step per replacement, back to front, which is the old path
    /// and its old cost. Replace All never has one, since a match is never empty.
    /// </para>
    /// <para>
    /// A list that is out of order or overlapping is not repaired: those replacements share a step,
    /// which refuses them.
    /// </para>
    /// </remarks>
    internal static List<ReplaceTextStep> Batches(IReadOnlyList<TextReplacement> replacements)
    {
        if (replacements.Any(replacement => replacement.From == replacement.To))
        {
            return [.. replacements.Reverse().Select(replacement => new ReplaceTextStep([replacement]))];
        }

        var batches = new List<List<TextReplacement>>();
        var lastTo = new List<int>();
        var growth = new List<int>();

        foreach (var replacement in replacements)
        {
            var batch = 0;

            // What the earlier steps replaced before this point has already moved it.
            var shift = 0;

            while (batch < batches.Count && lastTo[batch] == replacement.From + shift)
            {
                shift += growth[batch];
                batch++;
            }

            var shifted = replacement with
            {
                From = replacement.From + shift,
                To = replacement.To + shift,
            };
            var delta = replacement.Content.Length - (replacement.To - replacement.From);

            if (batch == batches.Count)
            {
                batches.Add([shifted]);
                lastTo.Add(shifted.To);
                growth.Add(delta);
            }
            else
            {
                batches[batch].Add(shifted);
                lastTo[batch] = shifted.To;
                growth[batch] += delta;
            }
        }

        return [.. batches.Select(batch => new ReplaceTextStep([.. batch]))];
    }
    /// <summary>
    /// Puts the rewritten paragraphs inside a node into a copy of its children.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="contentStart">Where the node's content starts.</param>
    /// <param name="children">A copy of the node's children, to be written into.</param>
    /// <param name="rewritten">Each paragraph to put in, by where its content starts, ascending.</param>
    /// <param name="next">The first entry of <paramref name="rewritten"/> not yet placed.</param>
    /// <remarks>
    /// Each paragraph's child is found by its position rather than by walking the children, and a
    /// container's children are copied once however many of them change — at the top of a
    /// document that is one copy of the block array in all, where a step per match made one each.
    /// </remarks>
    private static void PutBack(
        Node node,
        int contentStart,
        Node[] children,
        List<(int Start, ParagraphNode Paragraph)> rewritten,
        ref int next)
    {
        var contentEnd = contentStart + node.ContentSize;

        while (next < rewritten.Count && rewritten[next].Start < contentEnd)
        {
            var index = node.ChildIndexAt(rewritten[next].Start - contentStart, out var offset);
            var child = children[index];

            if (child is ParagraphNode)
            {
                children[index] = rewritten[next++].Paragraph;
            }
            else
            {
                Node[] grandchildren = [.. child.Children];

                PutBack(child, contentStart + offset + 1, grandchildren, rewritten, ref next);
                children[index] = child.WithChildren(grandchildren);
            }
        }
    }

    /// <summary>
    /// The replacements gathered by the paragraph they fall in, or why the step cannot apply.
    /// </summary>
    /// <param name="doc">The document the step applies to.</param>
    /// <param name="failure">Set to the first rule broken, or null.</param>
    /// <remarks>
    /// One position is resolved per paragraph, not per replacement: a later replacement starting
    /// no further than the paragraph's end is in the paragraph, because the ones before it are in
    /// order and the first one is in it.
    /// </remarks>
    private List<Group> Gather(DocumentNode doc, out string? failure)
    {
        var groups = new List<Group>();

        if (Replacements.IsDefault)
        {
            failure = "The step has no list of replacements.";

            return groups;
        }

        var previousTo = -1;
        var i = 0;

        while (i < Replacements.Length)
        {
            if (Broken(Replacements[i], previousTo, doc.ContentSize) is { } broken)
            {
                failure = broken;

                return groups;
            }

            var at = doc.Resolve(Replacements[i].From);

            if (at.Paragraph is not { } paragraph)
            {
                failure = $"Position {Replacements[i].From} is not in a paragraph's text.";

                return groups;
            }

            var start = at.Start(at.Depth);
            var end = start + paragraph.Content.Length;
            var first = i;

            while (i < Replacements.Length && Replacements[i].From <= end)
            {
                if (Broken(Replacements[i], previousTo, end) is { } outside)
                {
                    failure = outside;

                    return groups;
                }

                previousTo = Replacements[i].To;
                i++;
            }

            groups.Add(new Group(paragraph, start, first, i - first));
        }

        failure = null;

        return groups;
    }

    /// <summary>
    /// Why a replacement breaks the rules — after its predecessor, inside a limit — or null.
    /// </summary>
    private static string? Broken(TextReplacement replacement, int previousTo, int limit) =>
        replacement.Content is null
            ? $"The replacement at [{replacement.From}, {replacement.To}) has no content."
        : replacement.From < 0 || replacement.To < replacement.From || replacement.To > limit
            ? $"Range [{replacement.From}, {replacement.To}) is not inside one paragraph's text."
        : replacement.From <= previousTo
            ? $"The replacement at [{replacement.From}, {replacement.To}) is out of order with, "
                + "overlaps or touches the one before it."
        : null;

    /// <summary>The replacements that fall in one paragraph.</summary>
    /// <param name="Paragraph">The paragraph.</param>
    /// <param name="Start">Where its content starts.</param>
    /// <param name="First">The index of its first replacement.</param>
    /// <param name="Count">How many replacements it has.</param>
    private readonly record struct Group(ParagraphNode Paragraph, int Start, int First, int Count);
}