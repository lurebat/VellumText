using System.Collections.Immutable;

namespace Vellum.Avalonia;

/// <summary>How a list item is marked.</summary>
/// <param name="Kind">Whether the item is numbered.</param>
/// <param name="Ordinal">The item's number, honouring <see cref="ListNode.Start"/>.</param>
public readonly record struct ListMarker(ListKind Kind, int Ordinal);

/// <summary>
/// One block as the view layer sees it: a leaf block together with everything the document
/// tree knew about it that its own node does not.
/// </summary>
/// <remarks>
/// A block view lays out and draws one block, and it should not have to walk the tree to find
/// out that it is the third item of a nested numbered list. The walk happens once, here, and
/// what it learned travels with the block.
/// </remarks>
/// <param name="Node">The block itself.</param>
/// <param name="Start">
/// The document position of the block's first inner position — the position a caret at the very
/// start of the block occupies. For a leaf block, which has no inside, this is the position
/// the block itself occupies.
/// </param>
/// <param name="Depth">
/// How deeply the block is nested in lists. Zero for a top-level block, one for a block in a
/// top-level list item, and so on.
/// </param>
/// <param name="Marker">
/// The marker to draw beside the block, or null. Only the first block of a list item carries
/// one, so a multi-paragraph item is marked once rather than once per paragraph.
/// </param>
public readonly record struct BlockSlot(BlockNode Node, int Start, int Depth, ListMarker? Marker)
{
    /// <summary>The number of positions inside the block.</summary>
    public int ContentSize => Node.ContentSize;

    /// <summary>The document position just past the block's content.</summary>
    public int End => Start + Node.ContentSize;
}

/// <summary>
/// Flattens a document tree into the sequence of blocks that actually get laid out.
/// </summary>
/// <remarks>
/// <para>
/// The document is a tree, but what is drawn is a vertical run of paragraphs and rules. Lists
/// and list items are structure, not layout: they contribute no box of their own, only an
/// indent and a marker on the blocks inside them. Flattening once and handing the view layer a
/// list is what keeps every geometry question — which block is at this pixel, which blocks does
/// this selection touch — a search over an array rather than a walk of a tree.
/// </para>
/// <para>
/// Tables are the exception this design will have to face in Increment 6, and they are exactly
/// why this returns a list rather than being folded into the view: a table is a block whose
/// cells contain blocks side by side, which no flat run can express. It will need a block view
/// that hosts its own child views, and this walk will stop at it rather than descending.
/// </para>
/// </remarks>
public static class DocumentBlocks
{
    /// <summary>The blocks of a document, in the order they are drawn.</summary>
    /// <param name="doc">The document.</param>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is null.</exception>
    public static ImmutableArray<BlockSlot> Flatten(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        var slots = ImmutableArray.CreateBuilder<BlockSlot>(doc.Blocks.Length);

        // A document's content starts at position 0, so the first block's boundary is there.
        Walk(doc.Blocks, 0, 0, null, slots);

        return slots.ToImmutable();
    }

    private static void Walk(
        IEnumerable<BlockNode> blocks,
        int start,
        int depth,
        ListMarker? marker,
        ImmutableArray<BlockSlot>.Builder slots)
    {
        var pos = start;

        foreach (var block in blocks)
        {
            switch (block)
            {
                case ListNode list:
                    WalkList(list, pos + 1, depth, slots);
                    break;

                case ListItemNode item:
                    // Reached only through WalkList, which supplies the marker.
                    Walk(item.Blocks, pos + 1, depth, marker, slots);
                    marker = null;
                    break;

                case TableNode table:
                    WalkTable(table, pos + 1, depth, slots);
                    break;

                default:
                    // A leaf block has no inside, so its content start is the position it
                    // occupies rather than one past its opening boundary. Getting this wrong
                    // would put every later block one position out.
                    slots.Add(new BlockSlot(block, block.IsLeaf ? pos : pos + 1, depth, marker));
                    marker = null;
                    break;
            }

            pos += block.NodeSize;
        }
    }

    private static void WalkList(
        ListNode list,
        int start,
        int depth,
        ImmutableArray<BlockSlot>.Builder slots)
    {
        var pos = start;
        var ordinal = list.Start;

        foreach (var item in list.Items)
        {
            var marker = new ListMarker(list.Kind, ordinal);

            // The marker belongs to the item's first block only. An item holding two paragraphs
            // is one bullet with two paragraphs under it, not two bullets.
            Walk(item.Blocks, pos + 1, depth + 1, marker, slots);

            pos += item.NodeSize;
            ordinal++;
        }
    }

    /// <summary>Walks a table, emitting the blocks inside its cells.</summary>
    /// <remarks>
    /// <para>
    /// A grid is not a flat run of blocks, so this is a stand-in rather than the finished
    /// article: cells come out stacked in reading order, one under the next, indented a level,
    /// with no rules drawn between them. Real table layout needs a block view that hosts child
    /// views and lays them out side by side, which is a separate piece of work.
    /// </para>
    /// <para>
    /// It descends rather than stopping at the table because stopping was measurably worse than
    /// degraded: the view has no table view to build, so a document holding a table -- which the
    /// HTML importer produces from any pasted web page -- threw on the way to the screen.
    /// Descending keeps every position mapped to a real paragraph, so the caret, selection and
    /// editing all keep working inside a pasted table, and the document keeps the true
    /// <see cref="TableNode"/> for export and round-tripping. Only the drawing is degraded.
    /// </para>
    /// </remarks>
    private static void WalkTable(
        TableNode table,
        int start,
        int depth,
        ImmutableArray<BlockSlot>.Builder slots)
    {
        var pos = start;

        foreach (var row in table.Rows)
        {
            var cellPos = pos + 1;

            foreach (var cell in row.Cells)
            {
                Walk(cell.Blocks, cellPos + 1, depth + 1, null, slots);

                cellPos += cell.NodeSize;
            }

            pos += row.NodeSize;
        }
    }
}
