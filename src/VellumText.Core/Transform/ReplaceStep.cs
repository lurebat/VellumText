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
        // Depth 0 is included when there is no pivot, which is where the whole-document route
        // gets the container it splices against.
        IReadOnlyList<Node> middle = Content.Content;

        for (var depth = extra; depth > pivot; depth--)
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

        // Exactly one node either way: below the pivot the loop above wrapped everything into
        // one, and at the pivot itself the slice is a single node or there is no pivot.
        var joined = middle[0];

        return pivot < 0
            ? RebuildWholeDocument(doc, start, end, joined)
            : RebuildOneChild(doc, start, end, pivot, joined);
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

    /// <summary>Cuts the document in two, splices the slice in, and joins it back up.</summary>
    /// <param name="doc">The document being changed.</param>
    /// <param name="start">Where the replaced range starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="middle">The slice, wrapped up to a node that sits beside the document's own.</param>
    private static StepResult RebuildWholeDocument(
        DocumentNode doc, ResolvedPos start, ResolvedPos end, Node middle)
    {
        var left = TreeSurgery.CutBefore(start, 0);
        var right = TreeSurgery.CutAfter(end, 0);

        if (TreeSurgery.Splice(left, middle, start.Depth) is not { } withMiddle)
        {
            return StepResult.Failed("The slice cannot be joined to the content before it.");
        }

        if (TreeSurgery.Splice(withMiddle, right, end.Depth) is not DocumentNode result)
        {
            return StepResult.Failed("The slice cannot be joined to the content after it.");
        }

        return Validated(result, doc);
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
