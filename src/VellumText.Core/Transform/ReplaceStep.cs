using System.Runtime.InteropServices;

namespace VellumText;

/// <summary>
/// Replaces a range of the document with a slice.
/// </summary>
/// <remarks>
/// <para>
/// Almost every edit is this one step. Typing is a replacement of an empty range with one
/// character; Backspace is a replacement of one character with nothing; Enter is a replacement
/// of an empty range with a slice that happens to be open at both ends; paste is the general
/// case. Keeping them one step means undo, mapping and rebasing are written once.
/// </para>
/// </remarks>
/// <param name="From">Where the replaced range starts.</param>
/// <param name="To">Where it ends.</param>
/// <param name="Content">What to put there.</param>
public sealed record ReplaceStep(int From, int To, Slice Content) : Step
{
    /// <summary>Deletes a range.</summary>
    /// <param name="from">Where the range starts.</param>
    /// <param name="to">Where it ends.</param>
    public static ReplaceStep Delete(int from, int to) => new(from, to, Slice.Empty);

    /// <summary>Inserts a slice at a position.</summary>
    /// <param name="at">Where to insert.</param>
    /// <param name="content">What to insert.</param>
    public static ReplaceStep Insert(int at, Slice content) => new(at, at, content);

    /// <inheritdoc/>
    public override StepMap GetMap() => new(From, To - From, Content.Size);

    /// <inheritdoc/>
    public override StepResult Apply(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (From < 0 || To < From || To > doc.ContentSize)
        {
            return StepResult.Failed($"Range [{From}, {To}) is not inside the document.");
        }

        var start = doc.Resolve(From);
        var end = doc.Resolve(To);

        if (Content.OpenStart > start.Depth || Content.OpenEnd > end.Depth)
        {
            return StepResult.Failed("The slice is open deeper than the position it goes into.");
        }

        // The slice's two edges must sit at the same depth as each other once its open levels
        // are accounted for, or there is no single level at which the document closes back up
        // and the step's own size prediction stops being true.
        var extra = start.Depth - Content.OpenStart;

        if (extra != end.Depth - Content.OpenEnd)
        {
            return StepResult.Failed(
                $"Inconsistent open depths: {start.Depth}-{Content.OpenStart} against "
                + $"{end.Depth}-{Content.OpenEnd}.");
        }

        if (TakesATableApart(start, end, extra))
        {
            return StepResult.Failed("The range would join two parts of a table together.");
        }

        try
        {
            return Rebuild(doc, start, end, extra);
        }
        catch (ArgumentException ex)
        {
            // The model refuses invalid content at construction - a fragment cut through a
            // surrogate pair, a node type that cannot live where the slice would put it. Those
            // are step failures, not editor crashes.
            return StepResult.Failed(ex.Message);
        }
    }

    /// <inheritdoc/>
    public override Step Invert(DocumentNode docBefore)
    {
        ArgumentNullException.ThrowIfNull(docBefore);

        return new ReplaceStep(From, From + Content.Size, Slice.Cut(docBefore, From, To));
    }

    /// <inheritdoc/>
    public override Step? Map(Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        // The two ends bind outwards, so that concurrent edits inside the range shrink it
        // rather than pushing its edges around the surrounding content.
        var from = mapping.MapWithResult(From, Assoc.After);
        var to = mapping.MapWithResult(To, Assoc.Before);

        if (from.Deleted && to.Deleted)
        {
            return null;
        }

        return new ReplaceStep(from.Pos, Math.Max(from.Pos, to.Pos), Content);
    }

    /// <summary>
    /// Whether this step, applied to <paramref name="doc"/>, replaces text inside one paragraph
    /// with text and touches nothing else.
    /// </summary>
    /// <param name="doc">The document the step applies to.</param>
    /// <remarks>
    /// The shape <see cref="FollowedBy"/> is sound for: typing, Backspace and Delete within a
    /// paragraph. A split, a join or a paste of blocks changes the tree, and two of those folded
    /// into one step are not shown to mean what the pair meant.
    /// </remarks>
    internal bool IsTextEditIn(DocumentNode doc)
    {
        var textOnly = Content.IsEmpty
            || (Content.Content.Length == 1
                && Content.OpenStart == 1
                && Content.OpenEnd == 1
                && Content.Content[0] is ParagraphNode);

        if (!textOnly || From < 0 || To < From || To > doc.ContentSize)
        {
            return false;
        }

        var start = doc.Resolve(From);

        return start.Paragraph is { } paragraph
            && To <= start.Start(start.Depth) + paragraph.Content.Length;
    }

    /// <summary>
    /// This step and then <paramref name="next"/> as a single step, or null if their ranges do
    /// not meet.
    /// </summary>
    /// <param name="next">The step applied to the document this one produced.</param>
    /// <remarks>
    /// <para>
    /// Only for two steps that are each <see cref="IsTextEditIn">a text edit</see> of the
    /// document it applies to, which the caller checks: their ranges then meet inside one
    /// paragraph, and replacing two touching stretches of its text one after the other is the
    /// same as replacing their union once. ProseMirror's <c>ReplaceStep.merge</c> is the same
    /// idea; history uses it so that undoing a burst of typing replays one step rather than one
    /// per keystroke.
    /// </para>
    /// <para>
    /// Two ways to meet. <paramref name="next"/> can start where this step's inserted content
    /// ends — undoing typing deletes leftwards, so each earlier keystroke's range follows the one
    /// just removed — or end where this step starts, which is how undoing Backspace re-inserts.
    /// </para>
    /// </remarks>
    internal ReplaceStep? FollowedBy(ReplaceStep next)
    {
        if (From + Content.Size == next.From)
        {
            return new ReplaceStep(From, To + (next.To - next.From), Joined(Content, next.Content));
        }

        if (next.To == From)
        {
            return new ReplaceStep(next.From, To, Joined(next.Content, Content));
        }

        return null;
    }

    /// <summary>Two slices of text, one after the other, as one.</summary>
    private static Slice Joined(Slice first, Slice second)
    {
        if (first.IsEmpty)
        {
            return second;
        }

        if (second.IsEmpty)
        {
            return first;
        }

        var before = (ParagraphNode)first.Content[0];
        var after = (ParagraphNode)second.Content[0];

        return new Slice([before.WithContent(before.Content.Concat(after.Content))], 1, 1);
    }

    /// <summary>Whether closing the range back up would merge two parts of a table.</summary>
    /// <remarks>
    /// <para>
    /// The levels the step has to merge are the ones the slice does not supply — the first
    /// <paramref name="extra"/> of them, which are rebuilt from the range's <em>start</em> and
    /// then joined to the tree the range's end left behind. Where the two ends share an ancestor
    /// that join puts a node back together with itself and is what editing inside one cell is
    /// made of. Where they do not, it merges two different nodes, and if either is a table, a row
    /// or a cell then a piece of the table disappears into another one.
    /// </para>
    /// <para>
    /// This cannot be left to <see cref="DocumentSchema"/>, which is only asked whether the
    /// result is a legal table and not whether it is the same one: two cells of a two-by-two
    /// table merge into a grid with a hole in it and are caught, while two cells of a table with
    /// one row merge into a table with one cell, which is perfectly rectangular and perfectly
    /// wrong. Nor can <see cref="TreeSurgery.Splice"/> decide it — at the seam it sees two cells
    /// and cannot tell two halves of one cell from two different ones. Only the two resolved
    /// positions know that, so this is where it is asked.
    /// </para>
    /// <para>
    /// A slice open all the way down is the case this must <em>not</em> refuse: cutting a range
    /// that spans two cells and putting the very same slice back is an identity, and it is an
    /// identity precisely because the slice carries the cells with it and nothing is merged.
    /// </para>
    /// </remarks>
    private static bool TakesATableApart(ResolvedPos start, ResolvedPos end, int extra)
    {
        for (var depth = start.SharedDepth(end) + 1; depth <= extra; depth++)
        {
            if (start.NodeAt(depth) is TableNode or TableRowNode or TableCellNode
                || end.NodeAt(depth) is TableNode or TableRowNode or TableCellNode)
            {
                return true;
            }
        }

        return false;
    }

    private StepResult Rebuild(DocumentNode doc, ResolvedPos start, ResolvedPos end, int extra)
    {
        var pivot = PivotDepth(start, end, extra);

        // The slice arrives at whatever depth it was cut from, so it has to be re-wrapped in
        // the ancestors of the position it is going into before the seams can be joined. The
        // wrapping stops at the pivot: above it nothing is cut, so there is nothing to wrap for.
        // With no pivot it stops at the top-level blocks, which the document's own block array
        // is spliced around.
        IReadOnlyList<Node> middle = Content.Content;

        for (var depth = extra; depth > Math.Max(pivot, 0); depth--)
        {
            if (start.NodeAt(depth) is ParagraphNode paragraph)
            {
                if (middle.Count > 0)
                {
                    return StepResult.Failed("Cannot put block nodes inside a paragraph.");
                }

                middle = [paragraph.WithContent(InlineContent.Empty)];
            }
            else
            {
                middle = [start.NodeAt(depth).WithChildren(middle)];
            }
        }

        // With a pivot, exactly one node: below it the loop above wrapped everything into one,
        // and at the pivot itself the slice is a single node.
        return pivot < 0
            ? RebuildTopLevel(doc, start, end, middle)
            : RebuildOneChild(doc, start, end, pivot, middle[0]);
    }

    /// <summary>
    /// The deepest ancestor of the range whose children this replacement leaves in place, or -1
    /// when there is none.
    /// </summary>
    /// <param name="start">Where the replaced range starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="extra">How many levels the step has to merge rather than the slice supply.</param>
    /// <remarks>
    /// <para>
    /// Two bounds, and both are needed. A node above <see cref="ResolvedPos.SharedDepth"/> holds
    /// the range's start and end in <em>different</em> children, so closing the range up removes
    /// everything between them and its child list is a different length afterwards. And a node at
    /// or below the level the slice's own content arrives at receives that content, so its child
    /// list grows by one less than the slice has nodes — which is nothing only when the slice is
    /// a single node, the shape typing and a paragraph merge both have.
    /// </para>
    /// <para>
    /// Above the answer, every level of the result is its old children with one child replaced.
    /// That is worth knowing because the alternative is to cut the level in two, splice the
    /// slice into the left half, splice the right half back on, and rebuild its child array
    /// three times over to arrive at an array differing in one place — and at the top level that
    /// array is the whole document. Below the answer nothing is claimed and the general
    /// cut-and-splice does the work, which is why this is a pivot and not a special case: the
    /// two routes meet at whatever depth the edit stops being confined.
    /// </para>
    /// </remarks>
    private int PivotDepth(ResolvedPos start, ResolvedPos end, int extra) =>
        Math.Min(
            start.SharedDepth(end) - 1,
            Content.Content.Length == 1 ? extra : extra - 1);

    /// <summary>
    /// Replaces the top-level blocks the range touches, splicing the document's block array once.
    /// </summary>
    /// <param name="doc">The document being changed.</param>
    /// <param name="start">Where the replaced range starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="middle">The slice, wrapped up to top-level blocks.</param>
    /// <remarks>
    /// <para>
    /// The route for an edit no level of the tree survives unchanged in length, which is what
    /// Enter (one paragraph becomes two) and a join (two become one) are at the top level. It
    /// used to cut the whole document in two, splice the slice into the left half and the right
    /// half back on — six copies of the document's block array, 2.4 MB a keystroke at 50,000
    /// paragraphs. Only the blocks the range reaches are rebuilt here, and the array is copied
    /// once with them in place.
    /// </para>
    /// <para>
    /// The seams are exactly those of the cut-and-splice it replaces, decided by the same
    /// <see cref="TreeSurgery.Merge"/>: the block the range starts in, cut short, joins the first
    /// block of the slice when the start is inside a block; the last block of what that made
    /// joins the remainder of the block the range ends in when the end is inside one. A start or
    /// end between top-level blocks has no block to join, and the whole route agreed on that by
    /// concatenating there.
    /// </para>
    /// </remarks>
    private static StepResult RebuildTopLevel(
        DocumentNode doc, ResolvedPos start, ResolvedPos end, IReadOnlyList<Node> middle)
    {
        var replaced = new List<Node>(middle.Count + 1);

        if (start.Depth > 0)
        {
            // There is a slice to join here: one that leaves the start inside a block is either
            // open at its start, so it has content, or was wrapped up to a top-level block.
            if (TreeSurgery.Merge(TreeSurgery.CutBefore(start, 1), middle[0], start.Depth)
                is not { } first)
            {
                return StepResult.Failed("The slice cannot be joined to the content before it.");
            }

            replaced.Add(first);

            for (var i = 1; i < middle.Count; i++)
            {
                replaced.Add(middle[i]);
            }
        }
        else
        {
            replaced.AddRange(middle);
        }

        if (end.Depth > 0)
        {
            // Never empty here: a start between blocks and nothing to insert means an empty
            // slice, which is closed, so the end is between blocks too.
            if (TreeSurgery.Merge(replaced[^1], TreeSurgery.CutAfter(end, 1), end.Depth)
                is not { } last)
            {
                return StepResult.Failed("The slice cannot be joined to the content after it.");
            }

            replaced[^1] = last;
        }

        var blocks = doc.Blocks;
        var from = start.IndexAt(0);
        var to = end.Depth > 0 ? end.IndexAt(0) + 1 : end.IndexAt(0);
        var spliced = new BlockNode[blocks.Length - (to - from) + replaced.Count];

        blocks.CopyTo(0, spliced, 0, from);

        for (var i = 0; i < replaced.Count; i++)
        {
            spliced[from + i] = replaced[i] as BlockNode ?? throw new ArgumentException(
                $"Expected BlockNode but found {replaced[i].TypeName}.");
        }

        blocks.CopyTo(to, spliced, from + replaced.Count, blocks.Length - to);

        // Handed over rather than copied: the array is new and nothing else holds it, and copying
        // it again into the document's own would be the second of the copies this route exists
        // to avoid.
        return Validated(new DocumentNode(ImmutableCollectionsMarshal.AsImmutableArray(spliced)), doc);
    }

    /// <summary>
    /// Rebuilds the one child of the pivot that the range touches, and the spine above it.
    /// </summary>
    /// <param name="doc">The document being changed.</param>
    /// <param name="start">Where the replaced range starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="pivot">The depth whose child list survives unchanged in length.</param>
    /// <param name="middle">The slice, wrapped up to the depth just below the pivot.</param>
    /// <remarks>
    /// The same three pieces as the whole-document route and the same seam decisions, joined
    /// directly to each other instead of through a parent that would have to be taken apart and
    /// put back together to hold them.
    /// </remarks>
    private static StepResult RebuildOneChild(
        DocumentNode doc, ResolvedPos start, ResolvedPos end, int pivot, Node middle)
    {
        var below = pivot + 1;

        if (TreeSurgery.Merge(TreeSurgery.CutBefore(start, below), middle, start.Depth - pivot)
            is not { } withMiddle)
        {
            return StepResult.Failed("The slice cannot be joined to the content before it.");
        }

        if (TreeSurgery.Merge(withMiddle, TreeSurgery.CutAfter(end, below), end.Depth - pivot)
            is not { } result)
        {
            return StepResult.Failed("The slice cannot be joined to the content after it.");
        }

        return Validated(TreeSurgery.ReplaceChild(start, pivot, result), doc);
    }

    /// <summary>The result, if the schema accepts what the step made of the document.</summary>
    /// <param name="result">What the step produced.</param>
    /// <param name="before">The document it started from.</param>
    /// <remarks>
    /// Only what the step touched. The document it started from was valid - the editor never
    /// holds one that is not - and the nodes it did not rewrite are the same objects, so the
    /// walk prunes to the spine plus the new content. Validating the whole document after
    /// every keystroke was the largest single cost on the edit path.
    /// </remarks>
    private static StepResult Validated(DocumentNode result, DocumentNode before)
    {
        var violations = DocumentSchema.Validate(result, before);

        return violations.IsEmpty
            ? StepResult.Ok(result)
            : StepResult.Failed($"The result is not a valid document: {violations[0]}.");
    }
}
