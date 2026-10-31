namespace VellumText.Core.Tests.Model;

/// <summary>
/// Validating an edited table re-places its cells only when the edit changed the table's shape,
/// and never accepts what a full validation would refuse.
/// </summary>
/// <remarks>
/// A keystroke in a cell rebuilds the table node, so the schema reaches it on every step. Placing
/// every cell of a 10,000-row table to re-prove a tiling the keystroke cannot have changed was 6 MB
/// per keystroke. See <c>docs/plans/2026-10-28-table-edit-scaling-research.md</c>, option B′.
/// </remarks>
public sealed class TableShapeValidationTests
{
    private const int Columns = 5;

    private static TableNode Grid(int rows) =>
        new(Enumerable.Range(0, rows).Select(_ =>
            new TableRowNode(Enumerable.Range(0, Columns).Select(_ => TableCellNode.FromText("cell")))));

    /// <summary>The first text position of row <paramref name="row"/>, column 0, of <see cref="Grid"/> at the start of a document.</summary>
    private static int TextStart(int row) => 1 + (row * (2 + (Columns * 8))) + 3;

    private static (EditorState State, long Bytes) Type(EditorState state, int at)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var tr = state.Transaction();
        tr.InsertText(at, "x");
        Assert.Empty(tr.Failures);
        var next = state.Apply(tr);
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        return (next, bytes);
    }

    [Fact]
    public void Typing_in_a_large_table_does_not_place_every_cell_again()
    {
        var state = EditorState.Create(new DocumentNode([Grid(10_000)]));
        var at = TextStart(5_000);

        // The first edit of a table nobody has validated yet pays for one full validation.
        (state, _) = Type(state, at);

        // Every keystroke after that leaves the shape alone. Placing the cells was 5.8 MB of a
        // 6 MB keystroke; what is left is the spine, including the 10,000-row array (80 KB).
        for (var i = 0; i < 2; i++)
        {
            (state, var bytes) = Type(state, at);

            Assert.True(bytes < 1024 * 1024, $"keystroke {i + 2} allocated {bytes / 1024} KB");
        }

        Assert.Equal("xxxcell", ((ParagraphNode)((TableNode)state.Doc.Blocks[0]).Rows[5_000].Cells[0].Blocks[0]).Content.Text);
        Assert.Empty(DocumentSchema.Validate(state.Doc));
    }

    // ------------------------------------------------------------ named corruptions

    private static TableCellNode Cell(int rowSpan = 1, int columnSpan = 1) =>
        new([ParagraphNode.FromText("c")], rowSpan, columnSpan);

    /// <summary>A valid table, validated once so that it is known to be valid.</summary>
    private static TableNode KnownValid(TableNode table)
    {
        Assert.Empty(DocumentSchema.Validate(new DocumentNode([table])));

        return table;
    }

    private static TableNode WithRow(TableNode table, int index, TableRowNode row) =>
        new(table.Rows.SetItem(index, row), table.ColumnWidths);

    private static void AssertRefusedLikeAFullValidation(TableNode before, TableNode after)
    {
        var full = DocumentSchema.Validate(new DocumentNode([after]));
        var incremental = DocumentSchema.Validate(new DocumentNode([after]), new DocumentNode([before]));

        Assert.NotEmpty(full);
        Assert.Equal(full.ToArray(), incremental.ToArray());
    }

    [Fact]
    public void A_row_span_grown_past_the_last_row_is_refused()
    {
        var before = KnownValid(Grid(4));
        var after = WithRow(before, 2, new TableRowNode([Cell(rowSpan: 3), .. before.Rows[2].Cells.Skip(1)]));

        AssertRefusedLikeAFullValidation(before, after);
    }

    [Fact]
    public void A_column_span_grown_in_the_edited_row_is_refused()
    {
        var before = KnownValid(Grid(4));
        var after = WithRow(before, 1, new TableRowNode([Cell(columnSpan: 2), .. before.Rows[1].Cells.Skip(1)]));

        AssertRefusedLikeAFullValidation(before, after);
    }

    [Fact]
    public void A_cell_removed_from_the_edited_row_is_refused()
    {
        var before = KnownValid(Grid(4));
        var after = WithRow(before, 1, new TableRowNode(before.Rows[1].Cells.Skip(1)));

        AssertRefusedLikeAFullValidation(before, after);
    }

    [Fact]
    public void A_removed_row_that_leaves_a_span_hanging_is_refused()
    {
        var before = KnownValid(new TableNode(
        [
            new TableRowNode([Cell(rowSpan: 2), Cell(), Cell()]),
            new TableRowNode([Cell(), Cell()]),
        ]));

        // Row 0 is the very same object; only the row count changed.
        var after = new TableNode([before.Rows[0]]);

        AssertRefusedLikeAFullValidation(before, after);
    }

    [Fact]
    public void Column_widths_that_no_longer_match_the_width_are_refused()
    {
        var before = KnownValid(new TableNode(Grid(3).Rows, [1, 1, 1, 1, 1]));
        var after = new TableNode(before.Rows, [1, 1, 1, 1]);

        AssertRefusedLikeAFullValidation(before, after);
    }

    [Fact]
    public void An_edited_cell_left_with_no_blocks_is_refused()
    {
        var before = KnownValid(Grid(4));

        // Same spans, so the same shape: the tiling holds, and the cell rule must still be checked.
        var after = WithRow(before, 2, new TableRowNode([new TableCellNode([]), .. before.Rows[2].Cells.Skip(1)]));

        AssertRefusedLikeAFullValidation(before, after);
    }

    [Fact]
    public void A_ragged_table_nobody_validated_still_refuses_an_edit_that_keeps_its_shape()
    {
        var ragged = new DocumentNode(
        [
            new TableNode(
            [
                new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
                new TableRowNode([TableCellNode.FromText("c")]),
            ]),
        ]);
        var state = EditorState.Create(ragged);

        var tr = state.Transaction();
        tr.InsertText(4, "x");

        Assert.Contains(tr.Failures, f => f.Contains("No cell covers column 1", StringComparison.Ordinal));

        // Validating it — and finding it wanting — must not make it count as known to be valid.
        Assert.NotEmpty(DocumentSchema.Validate(ragged));

        var again = state.Transaction();
        again.InsertText(4, "x");

        Assert.Contains(again.Failures, f => f.Contains("No cell covers column 1", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ oracle

    /// <summary>
    /// Over generated valid tables and generated edits — ones that keep the shape and ones that
    /// break it — validating against the table before the edit reports exactly what validating
    /// from scratch reports.
    /// </summary>
    [Fact]
    public void Validating_against_the_previous_table_agrees_with_a_full_validation()
    {
        var random = new Random(20261028);
        var skipped = 0;
        var refused = 0;

        for (var i = 0; i < 3_000; i++)
        {
            var before = ValidTable(random);
            var knownValid = random.Next(4) != 0;

            if (knownValid)
            {
                KnownValid(before);
            }

            var after = Edit(random, before);
            var full = DocumentSchema.Validate(new DocumentNode([after]));
            var incremental = DocumentSchema.Validate(new DocumentNode([after]), new DocumentNode([before]));

            Assert.True(
                full.SequenceEqual(incremental),
                $"case {i}: full [{string.Join("; ", full)}] but incremental [{string.Join("; ", incremental)}]");

            refused += full.IsEmpty ? 0 : 1;
            skipped += full.IsEmpty && knownValid ? 1 : 0;
        }

        // The generator must exercise both sides, or agreement proves nothing.
        Assert.True(refused > 500, $"only {refused} corrupt edits");
        Assert.True(skipped > 500, $"only {skipped} valid edits of known-valid tables");
    }

    /// <summary>A table that tiles its grid, built by placing random rectangles on it.</summary>
    private static TableNode ValidTable(Random random)
    {
        var height = random.Next(1, 7);
        var width = random.Next(1, 6);
        var owner = new (int Top, int Left, int Bottom, int Right)?[height, width];
        var cells = new List<(int Top, int Left, int Bottom, int Right)>();

        for (var r = 0; r < height; r++)
        {
            for (var c = 0; c < width; c++)
            {
                if (owner[r, c] is not null)
                {
                    continue;
                }

                var bottom = random.Next(4) == 0 ? Math.Min(height, r + random.Next(1, 4)) : r + 1;
                var right = random.Next(4) == 0 ? Math.Min(width, c + random.Next(1, 4)) : c + 1;

                // Shrink until the rectangle covers only free slots.
                while (!Free(owner, r, c, bottom, right))
                {
                    if (right - c > 1)
                    {
                        right--;
                    }
                    else
                    {
                        bottom--;
                    }
                }

                var cell = (r, c, bottom, right);

                for (var y = r; y < bottom; y++)
                {
                    for (var x = c; x < right; x++)
                    {
                        owner[y, x] = cell;
                    }
                }

                cells.Add(cell);
            }
        }

        var rows = Enumerable.Range(0, height).Select(r => new TableRowNode(
            cells.Where(p => p.Top == r).OrderBy(p => p.Left).Select(p =>
                new TableCellNode([ParagraphNode.FromText($"{p.Top}.{p.Left}")], p.Bottom - p.Top, p.Right - p.Left))));
        var widths = random.Next(2) == 0 ? null : Enumerable.Repeat(1.0, width);

        return new TableNode(rows, widths);
    }

    private static bool Free((int, int, int, int)?[,] owner, int top, int left, int bottom, int right)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (owner[y, x] is not null)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>One edit of the kind a step makes: every row it does not touch is the same object.</summary>
    private static TableNode Edit(Random random, TableNode table)
    {
        var rows = table.Rows;
        var r = random.Next(rows.Length);
        var cells = rows[r].Cells;

        TableCellNode Respan(TableCellNode cell, int rowSpan, int columnSpan) =>
            new(cell.Blocks, Math.Max(1, rowSpan), Math.Max(1, columnSpan));

        switch (random.Next(11))
        {
            case 0 when !cells.IsEmpty:
            {
                var c = random.Next(cells.Length);
                var text = new TableCellNode([ParagraphNode.FromText("typed")], cells[c].RowSpan, cells[c].ColumnSpan);

                return new TableNode(rows.SetItem(r, new TableRowNode(cells.SetItem(c, text))), table.ColumnWidths);
            }

            case 1 when !cells.IsEmpty:
            {
                var c = random.Next(cells.Length);
                var empty = new TableCellNode([], cells[c].RowSpan, cells[c].ColumnSpan);

                return new TableNode(rows.SetItem(r, new TableRowNode(cells.SetItem(c, empty))), table.ColumnWidths);
            }

            case 2 when !cells.IsEmpty:
            {
                var c = random.Next(cells.Length);
                var cell = Respan(cells[c], cells[c].RowSpan + (random.Next(2) == 0 ? 1 : -1), cells[c].ColumnSpan);

                return new TableNode(rows.SetItem(r, new TableRowNode(cells.SetItem(c, cell))), table.ColumnWidths);
            }

            case 3 when !cells.IsEmpty:
            {
                var c = random.Next(cells.Length);
                var cell = Respan(cells[c], cells[c].RowSpan, cells[c].ColumnSpan + (random.Next(2) == 0 ? 1 : -1));

                return new TableNode(rows.SetItem(r, new TableRowNode(cells.SetItem(c, cell))), table.ColumnWidths);
            }

            case 4 when !cells.IsEmpty:
                return new TableNode(rows.SetItem(r, new TableRowNode(cells.RemoveAt(random.Next(cells.Length)))), table.ColumnWidths);

            case 5:
                return new TableNode(rows.SetItem(r, new TableRowNode(cells.Insert(random.Next(cells.Length + 1), Cell()))), table.ColumnWidths);

            case 6 when rows.Length > 1:
                return new TableNode(rows.RemoveAt(r), table.ColumnWidths);

            case 7:
                return new TableNode(rows.Insert(r, new TableRowNode(cells)), table.ColumnWidths);

            case 8:
            {
                var widths = table.ColumnWidths.IsEmpty
                    ? Enumerable.Repeat(1.0, random.Next(1, 7))
                    : random.Next(3) == 0 ? null : table.ColumnWidths.Add(1);

                return new TableNode(rows, widths);
            }

            case 9 when !table.ColumnWidths.IsEmpty:
                return new TableNode(rows, table.ColumnWidths.RemoveAt(0));

            default:
                // The same shape, rebuilt: a different row object holding the same cells.
                return new TableNode(rows.SetItem(r, new TableRowNode(cells)), table.ColumnWidths);
        }
    }
}
