using System.Collections.Immutable;
using System.Text;

namespace VellumText;

/// <summary>One way in which a document breaks the schema.</summary>
/// <param name="Path">Where the problem is, as a slash-separated node path.</param>
/// <param name="Message">What is wrong.</param>
public readonly record struct SchemaViolation(string Path, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Path}: {Message}";
}

/// <summary>
/// The structural rules a document must satisfy.
/// </summary>
/// <remarks>
/// <para>
/// Child <em>types</em> are enforced by the node constructors — a
/// <see cref="TableRowNode"/> simply cannot hold a paragraph. What is left for the schema is
/// cardinality and the rules that span several nodes, which a constructor cannot see.
/// </para>
/// <para>
/// Validation reports rather than throws, because the caller that needs it most is
/// <c>Step.Apply</c>, whose contract is to return a failure instead of producing an invalid
/// document (architecture §7). A partially-valid tree must never reach the editor.
/// </para>
/// </remarks>
public static class DocumentSchema
{
    /// <summary>Whether <paramref name="node"/> and everything under it is valid.</summary>
    /// <param name="node">The node to check.</param>
    public static bool IsValid(Node node) => Validate(node).IsEmpty;

    /// <summary>Finds everything wrong with <paramref name="node"/> and its descendants.</summary>
    /// <param name="node">The node to check.</param>
    /// <returns>The violations, empty when the node is valid.</returns>
    public static ImmutableArray<SchemaViolation> Validate(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var violations = ImmutableArray.CreateBuilder<SchemaViolation>();

        Visit(node, null, node.TypeName, [], violations);

        return violations.ToImmutable();
    }

    /// <summary>
    /// Finds everything wrong with <paramref name="node"/>, checking only what changed since
    /// <paramref name="previous"/>.
    /// </summary>
    /// <param name="node">The node to check.</param>
    /// <param name="previous">
    /// What it was before. <b>Must already be valid</b> — this is the precondition the whole
    /// shortcut rests on, and it holds for the caller that matters because a step only ever
    /// starts from a document the editor has already accepted.
    /// </param>
    /// <returns>The violations, empty when the node is valid.</returns>
    /// <remarks>
    /// <para>
    /// The nodes are immutable and edits share structure, so a subtree that came through an edit
    /// untouched is the very same object — and it was valid a moment ago, so it is valid now.
    /// Reference equality therefore prunes the walk, and what is left is the spine from the root
    /// to the edit plus whatever the edit produced: a handful of nodes rather than the document.
    /// </para>
    /// <para>
    /// When children have been inserted or removed the pairing by index goes out of step, and
    /// then this simply validates more than it strictly needs to. That direction is the safe one:
    /// the worst case is the cost of <see cref="Validate(Node)"/>, never a violation missed.
    /// </para>
    /// </remarks>
    public static ImmutableArray<SchemaViolation> Validate(Node node, Node previous)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(previous);

        var violations = ImmutableArray.CreateBuilder<SchemaViolation>();

        Visit(node, previous, node.TypeName, [], violations);

        return violations.ToImmutable();
    }

    /// <summary>
    /// Walks the tree, recording where each node sits without writing it down.
    /// </summary>
    /// <param name="node">The node being checked.</param>
    /// <param name="previous">
    /// What this node was before the edit, when that is known and known to have been valid.
    /// <see langword="null"/> means check it from scratch.
    /// </param>
    /// <param name="root">The root node's type name, the first component of every path.</param>
    /// <param name="trail">
    /// The descent so far, as (child index, type name) pairs. Pushed and popped rather than
    /// concatenated: a path is only ever read when a violation is found, which on a valid
    /// document is never, and building one per node made validation - which runs after every
    /// step - spend about half its time formatting strings it then discarded.
    /// </param>
    /// <param name="violations">Where to record what is wrong.</param>
    private static void Visit(
        Node node,
        Node? previous,
        string root,
        List<(int Index, string TypeName)> trail,
        ImmutableArray<SchemaViolation>.Builder violations)
    {
        // The same object as before the edit, and it was valid before the edit.
        if (ReferenceEquals(node, previous))
        {
            return;
        }

        switch (node)
        {
            case DocumentNode doc:
                // Without a block there is nowhere for the caret to be, and an editor with
                // no valid caret position has no coherent behaviour to fall back on.
                RequireNonEmpty(doc.Blocks.Length, root, trail, "block", violations);
                break;

            case ListNode list:
                RequireNonEmpty(list.Items.Length, root, trail, "item", violations);
                break;

            case ListItemNode item:
                RequireNonEmpty(item.Blocks.Length, root, trail, "block", violations);
                break;

            case TableNode table:
                RequireNonEmpty(table.Rows.Length, root, trail, "row", violations);
                ValidateTable(table, root, trail, violations);
                break;

            case TableRowNode row:
                RequireNonEmpty(row.Cells.Length, root, trail, "cell", violations);
                break;

            case TableCellNode cell:
                RequireNonEmpty(cell.Blocks.Length, root, trail, "block", violations);
                break;

            default:
                break;
        }

        // Read once. The property is declared as IReadOnlyList<Node> and implemented by an
        // ImmutableArray, so every read boxes - and in the loop condition that was one box per
        // child rather than one per node.
        var children = node.Children;
        var before = previous is null ? ImmutableArray<Node>.Empty : previous.Children;

        for (var i = 0; i < children.Length; i++)
        {
            var child = children[i];

            trail.Add((i, child.TypeName));
            Visit(child, i < before.Length ? before[i] : null, root, trail, violations);
            trail.RemoveAt(trail.Count - 1);
        }
    }

    /// <summary>Materialises the path to the node currently being visited.</summary>
    /// <remarks>Called only when a violation is being recorded, which is the whole point.</remarks>
    private static string PathOf(string root, List<(int Index, string TypeName)> trail)
    {
        if (trail.Count == 0)
        {
            return root;
        }

        var path = new StringBuilder(root);

        foreach (var (index, typeName) in trail)
        {
            path.Append('/').Append(index).Append(':').Append(typeName);
        }

        return path.ToString();
    }

    private static void ValidateTable(
        TableNode table,
        string root,
        List<(int Index, string TypeName)> trail,
        ImmutableArray<SchemaViolation>.Builder violations)
    {
        if (table.Rows.IsEmpty)
        {
            return;
        }

        // Occupancy, not cell counts: a cell spanning rows fills slots in the rows below it,
        // so those rows legitimately carry fewer cells. What must hold is that the placements
        // tile the grid exactly - no hole a renderer would have to invent a cell for, and no
        // span hanging off the bottom.
        var placements = TableGeometry.Place(table, out var width);
        var height = table.Rows.Length;
        var covered = new bool[height, Math.Max(width, 1)];

        foreach (var placement in placements)
        {
            if (placement.Bottom > height)
            {
                violations.Add(new SchemaViolation(
                    $"{PathOf(root, trail)}/{placement.Top}:table-row",
                    $"A cell spans rows {placement.Top} to {placement.Bottom - 1} but the "
                    + $"table has only {height} row(s)."));
            }

            for (var r = placement.Top; r < Math.Min(placement.Bottom, height); r++)
            {
                for (var c = placement.Left; c < placement.Right; c++)
                {
                    covered[r, c] = true;
                }
            }
        }

        for (var r = 0; r < height; r++)
        {
            for (var c = 0; c < width; c++)
            {
                if (!covered[r, c])
                {
                    violations.Add(new SchemaViolation(
                        $"{PathOf(root, trail)}/{r}:table-row",
                        $"No cell covers column {c}, but the table is {width} column(s) wide."));
                }
            }
        }

        if (!table.ColumnWidths.IsEmpty && table.ColumnWidths.Length != width)
        {
            violations.Add(new SchemaViolation(
                PathOf(root, trail),
                $"{table.ColumnWidths.Length} column width(s) supplied for {width} column(s)."));
        }
    }

    private static void RequireNonEmpty(
        int count,
        string root,
        List<(int Index, string TypeName)> trail,
        string childName,
        ImmutableArray<SchemaViolation>.Builder violations)
    {
        if (count == 0)
        {
            violations.Add(new SchemaViolation(
                PathOf(root, trail), $"Must contain at least one {childName}."));
        }
    }
}
