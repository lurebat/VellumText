namespace VellumText;

/// <summary>
/// Replaces one table with another.
/// </summary>
/// <remarks>
/// <para>
/// A table edit cannot be a composition of replacements, because
/// <see cref="DocumentSchema"/> requires a table's cells to tile its grid exactly and every step
/// has to leave a document it accepts. Inserting a column reaches into every row, so doing it a
/// cell at a time would stand, between steps, in a document with one row a cell wider than the
/// rest — which is precisely the document the schema exists to refuse. So the whole table changes
/// at once, or not at all.
/// </para>
/// <para>
/// The step carries the table it expects to find as well as the one it puts there. That is not
/// bookkeeping: <see cref="GetMap"/> has no document to consult, and a map supplied by the caller
/// alongside a new table is a second description of the same edit that can quietly disagree with
/// the first. Carrying both tables makes the map <em>derived</em>, so there is nothing to
/// disagree with — and it costs two references, since the old table is the one already in the
/// document and the new one had to be built regardless.
/// </para>
/// <para>
/// The same pair is what makes the step honest about rebasing. A table that has changed
/// underneath it is not one this step knows how to rewrite, and it fails rather than applying a
/// replacement whose map describes a document that is no longer there.
/// </para>
/// </remarks>
/// <param name="Pos">The position directly before the table.</param>
/// <param name="Before">The table this step expects to find there.</param>
/// <param name="After">The table to put in its place.</param>
public sealed record TableStructureStep(int Pos, TableNode Before, TableNode After) : Step
{
    /// <inheritdoc/>
    public override StepResult Apply(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        if (Pos < 0 || Pos > doc.ContentSize)
        {
            return StepResult.Failed($"Position {Pos} is outside the document.");
        }

        var at = doc.Resolve(Pos);

        if (at.NodeAfter is not TableNode current)
        {
            return StepResult.Failed($"No table starts at position {Pos}.");
        }

        if (!current.Equals(Before))
        {
            return StepResult.Failed(
                $"The table at position {Pos} is not the one this step was built against.");
        }

        var result = TreeSurgery.ReplaceNodeAfter(at, After);

        // Only what the step touched: everything outside the table is the very same object as in
        // a document the editor has already accepted.
        var violations = DocumentSchema.Validate(result, doc);

        return violations.IsEmpty
            ? StepResult.Ok(result)
            : StepResult.Failed($"The result is not a valid document: {violations[0]}.");
    }

    /// <inheritdoc/>
    public override Step Invert(DocumentNode docBefore)
    {
        ArgumentNullException.ThrowIfNull(docBefore);

        return new TableStructureStep(Pos, After, Before);
    }

    /// <inheritdoc/>
    public override StepMap GetMap()
    {
        var ranges = new List<ReplacedRange>();

        Diff(Before, After, Pos + 1, ranges);

        return StepMap.Of(ranges);
    }

    /// <inheritdoc/>
    public override Step? Map(Mapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        var mapped = mapping.MapWithResult(Pos, Assoc.After);

        // A table that no longer exists has no structure to change.
        return mapped.Deleted ? null : this with { Pos = mapped.Pos };
    }

    /// <summary>
    /// Records how two versions of one node differ, as ranges of the document before the edit.
    /// </summary>
    /// <param name="before">The node as it was.</param>
    /// <param name="after">The node as it will be.</param>
    /// <param name="contentStart">Where <paramref name="before"/>'s content begins.</param>
    /// <param name="ranges">Where to record the differences, in ascending order.</param>
    /// <remarks>
    /// <para>
    /// Children that match at the front and the back are shared — an edit rebuilds only the rows
    /// and cells it touches, so most comparisons stop at reference equality — and what is left
    /// between them is the change. Where the two middles have the same number of children they
    /// are paired up and the walk continues into each, which is what makes inserting a column
    /// report one small range per row rather than one range swallowing the table.
    /// </para>
    /// <para>
    /// Where the counts differ there is no honest pairing, so the whole differing run becomes one
    /// range. That is coarser than the truth and never wrong: the ranges only have to bound what
    /// changed and get the sizes right, and everything outside them really is untouched.
    /// </para>
    /// <para>
    /// The walk descends only into tables, rows and cells. A paragraph reports no children at all
    /// — its content is text, not a child list — so descending into one would compare nothing,
    /// find nothing, and report a range of zero for two paragraphs of different lengths.
    /// </para>
    /// <para>
    /// A change confined to a cell's <em>attributes</em> — its spans, background or header flag —
    /// therefore reports no range at all, because only the children are compared. That is the
    /// right answer and the same one <see cref="SetBlockAttrStep"/> gives: an attribute moves no
    /// position, so a map that claimed it had moved one would throw away a caret for a change the
    /// reader would describe as nothing having moved. It does mean this map is not a complete
    /// description of the difference between two tables, only of where the positions went.
    /// </para>
    /// </remarks>
    private static void Diff(Node before, Node after, int contentStart, List<ReplacedRange> ranges)
    {
        var a = before.Children;
        var b = after.Children;

        var prefix = 0;

        while (prefix < a.Length && prefix < b.Length && a[prefix].Equals(b[prefix]))
        {
            prefix++;
        }

        var suffix = 0;

        while (suffix < a.Length - prefix
            && suffix < b.Length - prefix
            && a[a.Length - 1 - suffix].Equals(b[b.Length - 1 - suffix]))
        {
            suffix++;
        }

        var start = contentStart;

        for (var i = 0; i < prefix; i++)
        {
            start += a[i].NodeSize;
        }

        var oldCount = a.Length - prefix - suffix;
        var newCount = b.Length - prefix - suffix;

        if (oldCount == 0 && newCount == 0)
        {
            return;
        }

        if (oldCount == newCount)
        {
            var pos = start;

            for (var i = 0; i < oldCount; i++)
            {
                var oldChild = a[prefix + i];
                var newChild = b[prefix + i];

                if (!oldChild.Equals(newChild))
                {
                    if (CanDescend(oldChild, newChild))
                    {
                        Diff(oldChild, newChild, pos + 1, ranges);
                    }
                    else
                    {
                        ranges.Add(
                            new ReplacedRange(pos, oldChild.NodeSize, newChild.NodeSize));
                    }
                }

                // The old size, because these are positions in the document before the edit.
                pos += oldChild.NodeSize;
            }

            return;
        }

        var oldSize = 0;
        var newSize = 0;

        for (var i = 0; i < oldCount; i++)
        {
            oldSize += a[prefix + i].NodeSize;
        }

        for (var i = 0; i < newCount; i++)
        {
            newSize += b[prefix + i].NodeSize;
        }

        ranges.Add(new ReplacedRange(start, oldSize, newSize));
    }

    /// <summary>Whether two nodes are the kind whose children are worth comparing separately.</summary>
    private static bool CanDescend(Node before, Node after) =>
        before.GetType() == after.GetType()
            && before is TableNode or TableRowNode or TableCellNode;
}
