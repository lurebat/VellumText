using System.Collections.Immutable;

namespace VellumText.Tests;

/// <summary>
/// Why a table edit has to be one step.
/// </summary>
/// <remarks>
/// <see cref="DocumentSchema"/> requires a table's cells to tile its grid exactly, and every
/// step has to leave a document the schema accepts. A column reaches into every row, so doing it
/// a cell at a time means standing in a document where one row is a cell wider than the rest —
/// which is precisely the document the schema exists to refuse. These tests pin that refusal,
/// because it is the reason a table edit is one step rather than a composition of replacements.
/// </remarks>
public class TableTilingIsAtomicTests
{
    [Fact]
    public void AddingACellToOneRowOfATableIsRefused()
    {
        var doc = Tables.Grid(2, 2);
        var table = Tables.Table(doc);

        // The end of the first row: where a new cell for a third column would go.
        var at = 2 + table.Rows[0].ContentSize;

        var result = new ReplaceStep(at, at, Slice.OfBlocks(TableCellNode.FromText("c1")))
            .Apply(doc);

        Assert.False(result.IsOk);
        Assert.Contains("column", result.Failure!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemovingACellFromOneRowOfATableIsRefused()
    {
        var doc = Tables.Grid(2, 2);
        var cell = Tables.CellAt(doc, 0, 1);

        var result = ReplaceStep
            .Delete(cell, cell + doc.Resolve(cell).NodeAfter!.NodeSize)
            .Apply(doc);

        Assert.False(result.IsOk);
        Assert.Contains("column", result.Failure!, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Building and reading the tables the structure tests work on.</summary>
internal static class Tables
{
    /// <summary>A document holding one table of plain cells, labelled by row and column.</summary>
    public static DocumentNode Grid(int rows, int columns, IEnumerable<double>? widths = null)
    {
        var built = ImmutableArray.CreateBuilder<TableRowNode>(rows);

        for (var row = 0; row < rows; row++)
        {
            var cells = ImmutableArray.CreateBuilder<TableCellNode>(columns);

            for (var column = 0; column < columns; column++)
            {
                cells.Add(TableCellNode.FromText($"{(char)('a' + column)}{row + 1}"));
            }

            built.Add(new TableRowNode(cells.ToImmutable()));
        }

        return new DocumentNode([new TableNode(built.ToImmutable(), widths)]);
    }

    public static DocumentNode Of(params TableRowNode[] rows) => new([new TableNode(rows)]);

    public static TableNode Table(DocumentNode doc) => (TableNode)doc.Blocks[0];

    /// <summary>The position before the cell occupying a slot.</summary>
    public static int CellAt(DocumentNode doc, int row, int column) =>
        TableGrid.Build(0, Table(doc)).At(row, column)!.Value.Pos;

    /// <summary>
    /// Every row of a table as text: its cells' content joined by a bar, with a span in
    /// parentheses where a cell covers more than one slot.
    /// </summary>
    /// <remarks>
    /// Whole rows rather than one cell, because the failure worth catching is not a wrong cell
    /// but a table that no longer tiles its own grid — and that only shows up in the shape.
    /// </remarks>
    public static string[] Shape(DocumentNode doc) =>
        [.. Table(doc).Rows.Select(row => string.Join("|", row.Cells.Select(Describe)))];

    /// <summary>Applies a step and insists the result is a document the schema accepts.</summary>
    public static DocumentNode Apply(DocumentNode doc, Step? step)
    {
        Assert.NotNull(step);

        var result = step.Apply(doc);

        Assert.True(result.IsOk, result.Failure);

        var violations = DocumentSchema.Validate(result.Doc!);

        Assert.True(violations.IsEmpty, string.Join("; ", violations));

        return result.Doc!;
    }

    private static string Describe(TableCellNode cell)
    {
        var text = string.Join(
            " ", cell.Blocks.OfType<ParagraphNode>().Select(paragraph => paragraph.Content.Text));

        return cell.RowSpan == 1 && cell.ColumnSpan == 1
            ? text
            : $"{text}({cell.RowSpan}x{cell.ColumnSpan})";
    }
}

/// <summary>Rows and columns, merging and splitting.</summary>
public class TableEditsTests
{
    [Fact]
    public void ARowGoesInAboveTheCaret()
    {
        var doc = Tables.Grid(2, 2);
        var cell = Tables.CellAt(doc, 1, 0);

        var after = Tables.Apply(doc, TableEdits.InsertRows(doc, cell, cell, below: false));

        Assert.Equal(["a1|b1", "|", "a2|b2"], Tables.Shape(after));
    }

    [Fact]
    public void ARowGoesInBelowTheCaret()
    {
        var doc = Tables.Grid(2, 2);
        var cell = Tables.CellAt(doc, 0, 0);

        var after = Tables.Apply(doc, TableEdits.InsertRows(doc, cell, cell, below: true));

        Assert.Equal(["a1|b1", "|", "a2|b2"], Tables.Shape(after));
    }

    [Fact]
    public void AsManyRowsGoInAsAreSelected()
    {
        var doc = Tables.Grid(3, 2);

        var after = Tables.Apply(
            doc,
            TableEdits.InsertRows(
                doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 1, 1), below: true));

        Assert.Equal(["a1|b1", "a2|b2", "|", "|", "a3|b3"], Tables.Shape(after));
    }

    [Fact]
    public void ARowInsertedThroughAMergedCellStretchesItRatherThanCuttingIt()
    {
        // The tall cell covers rows 0 and 1. A row between them must appear neither as a hole
        // inside it nor as a second cell: a reader inserting a row through a merged cell means
        // the merge to grow.
        var doc = Tables.Of(
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                 TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("b2")]));

        var after = Tables.Apply(
            doc,
            TableEdits.InsertRows(
                doc, Tables.CellAt(doc, 0, 1), Tables.CellAt(doc, 0, 1), below: true));

        Assert.Equal(["tall(3x1)|b1", "", "b2"], Tables.Shape(after));
    }

    [Fact]
    public void ANewRowTakesItsLookFromTheRowItWasAddedBeside()
    {
        var doc = Tables.Of(
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("h")], isHeader: true),
                 new TableCellNode([ParagraphNode.FromText("i")], isHeader: true)]),
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]));

        var above = Tables.Apply(
            doc,
            TableEdits.InsertRows(
                doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 0), below: false));

        Assert.All(Tables.Table(above).Rows[0].Cells, cell => Assert.True(cell.IsHeader));

        var below = Tables.Apply(
            doc,
            TableEdits.InsertRows(
                doc, Tables.CellAt(doc, 1, 0), Tables.CellAt(doc, 1, 0), below: true));

        Assert.All(Tables.Table(below).Rows[2].Cells, cell => Assert.False(cell.IsHeader));
    }

    [Fact]
    public void ARowGoes()
    {
        var doc = Tables.Grid(3, 2);
        var cell = Tables.CellAt(doc, 1, 0);

        var after = Tables.Apply(doc, TableEdits.DeleteRows(doc, cell, cell));

        Assert.Equal(["a1|b1", "a3|b3"], Tables.Shape(after));
    }

    [Fact]
    public void EverySelectedRowGoes()
    {
        var doc = Tables.Grid(4, 2);

        var after = Tables.Apply(
            doc,
            TableEdits.DeleteRows(doc, Tables.CellAt(doc, 1, 0), Tables.CellAt(doc, 2, 1)));

        Assert.Equal(["a1|b1", "a4|b4"], Tables.Shape(after));
    }

    [Fact]
    public void TheLastRowMayNotBeDeleted()
    {
        // Deleting a table is a different gesture from editing one, and a table with no rows is
        // not a document the schema accepts.
        var doc = Tables.Grid(1, 2);
        var cell = Tables.CellAt(doc, 0, 0);

        Assert.Null(TableEdits.DeleteRows(doc, cell, cell));
    }

    [Fact]
    public void ACellThatBeganInADeletedRowKeepsItsContentInTheNextOne()
    {
        var doc = Tables.Of(
            new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                 TableCellNode.FromText("b2")]),
            new TableRowNode([TableCellNode.FromText("b3")]));

        var after = Tables.Apply(
            doc,
            TableEdits.DeleteRows(doc, Tables.CellAt(doc, 1, 1), Tables.CellAt(doc, 1, 1)));

        // "tall" began in the row that went. Dropping it would lose text the reader never
        // selected, so it moves down into what is left of its own span.
        Assert.Equal(["a1|b1", "tall|b3"], Tables.Shape(after));
    }

    [Fact]
    public void ACellSpanningOverADeletedRowShrinks()
    {
        var doc = Tables.Of(
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 3),
                 TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("b2")]),
            new TableRowNode([TableCellNode.FromText("b3")]));

        var after = Tables.Apply(
            doc,
            TableEdits.DeleteRows(doc, Tables.CellAt(doc, 1, 1), Tables.CellAt(doc, 1, 1)));

        Assert.Equal(["tall(2x1)|b1", "b3"], Tables.Shape(after));
    }

    [Fact]
    public void AColumnGoesInOnEitherSideOfTheCaret()
    {
        var doc = Tables.Grid(2, 2);
        var cell = Tables.CellAt(doc, 0, 1);

        Assert.Equal(
            ["a1||b1", "a2||b2"],
            Tables.Shape(Tables.Apply(
                doc, TableEdits.InsertColumns(doc, cell, cell, after: false))));

        Assert.Equal(
            ["a1|b1|", "a2|b2|"],
            Tables.Shape(Tables.Apply(
                doc, TableEdits.InsertColumns(doc, cell, cell, after: true))));
    }

    [Fact]
    public void AColumnInsertedThroughAWideCellStretchesIt()
    {
        var doc = Tables.Of(
            new TableRowNode([new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2)]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]));

        var after = Tables.Apply(
            doc,
            TableEdits.InsertColumns(
                doc, Tables.CellAt(doc, 1, 0), Tables.CellAt(doc, 1, 0), after: true));

        Assert.Equal(["wide(1x3)", "a2||b2"], Tables.Shape(after));
    }

    [Fact]
    public void EveryRowGetsACellInANewColumnEvenWhenItHeldNoneOfItsOwn()
    {
        // The second row carries nothing: the tall cell covers it. A new column still has to
        // reach it, or the table gains a hole exactly where the reader asked for a cell.
        var doc = Tables.Of(
            new TableRowNode([new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2)]),
            new TableRowNode([]));

        var after = Tables.Apply(
            doc,
            TableEdits.InsertColumns(
                doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 0), after: true));

        Assert.Equal(["tall(2x1)|", ""], Tables.Shape(after));
    }

    [Fact]
    public void AColumnGoes()
    {
        var doc = Tables.Grid(2, 3);
        var cell = Tables.CellAt(doc, 0, 1);

        var after = Tables.Apply(doc, TableEdits.DeleteColumns(doc, cell, cell));

        Assert.Equal(["a1|c1", "a2|c2"], Tables.Shape(after));
    }

    [Fact]
    public void TheLastColumnMayNotBeDeleted()
    {
        var doc = Tables.Grid(2, 1);
        var cell = Tables.CellAt(doc, 0, 0);

        Assert.Null(TableEdits.DeleteColumns(doc, cell, cell));
    }

    [Fact]
    public void DeletingAColumnMayLeaveARowCarryingNothing()
    {
        // The tall cell covers both rows of column 0, so once column 1 goes the second row holds
        // no cell of its own. That is a legal table - every slot is covered - and the alternative
        // of dropping the row would halve a merged cell the reader can see.
        var doc = Tables.Of(
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                 TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("b2")]));

        var after = Tables.Apply(
            doc,
            TableEdits.DeleteColumns(doc, Tables.CellAt(doc, 0, 1), Tables.CellAt(doc, 0, 1)));

        Assert.Equal(["tall(2x1)", ""], Tables.Shape(after));
    }

    [Fact]
    public void ColumnWidthsFollowTheColumns()
    {
        var doc = Tables.Grid(2, 3, [10d, 20d, 30d]);
        var cell = Tables.CellAt(doc, 0, 1);

        var narrower = Tables.Apply(doc, TableEdits.DeleteColumns(doc, cell, cell));

        Assert.Equal([10d, 30d], Tables.Table(narrower).ColumnWidths.AsEnumerable());

        var wider = Tables.Apply(doc, TableEdits.InsertColumns(doc, cell, cell, after: true));

        // The new column copies the one it was added beside, which is the only width in the table
        // known to suit the content around it.
        Assert.Equal([10d, 20d, 20d, 30d], Tables.Table(wider).ColumnWidths.AsEnumerable());
    }

    [Fact]
    public void TwoCellsMergeIntoOneThatKeepsBothTexts()
    {
        var doc = Tables.Grid(2, 2);

        var after = Tables.Apply(
            doc,
            TableEdits.MergeCells(doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 1)));

        Assert.Equal(["a1 b1(1x2)", "a2|b2"], Tables.Shape(after));
    }

    [Fact]
    public void AWholeRectangleMergesAndLeavesTheRowBelowCovered()
    {
        var doc = Tables.Grid(2, 2);

        var after = Tables.Apply(
            doc,
            TableEdits.MergeCells(doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 1, 1)));

        Assert.Equal(["a1 b1 a2 b2(2x2)", ""], Tables.Shape(after));
    }

    [Fact]
    public void MergingEmptyCellsDoesNotStackUpBlankLines()
    {
        var doc = Tables.Of(
            new TableRowNode(
                [TableCellNode.FromText("only"), TableCellNode.FromText(string.Empty)]),
            new TableRowNode(
                [TableCellNode.FromText(string.Empty), TableCellNode.FromText(string.Empty)]));

        var after = Tables.Apply(
            doc,
            TableEdits.MergeCells(doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 1, 1)));

        Assert.Equal(["only(2x2)", ""], Tables.Shape(after));
    }

    [Fact]
    public void MergingCellsThatAreAllEmptyStillLeavesSomewhereToType()
    {
        var doc = Tables.Of(
            new TableRowNode(
                [TableCellNode.FromText(string.Empty), TableCellNode.FromText(string.Empty)]));

        var after = Tables.Apply(
            doc,
            TableEdits.MergeCells(doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 1)));

        Assert.Single(Tables.Table(after).Rows[0].Cells[0].Blocks);
    }

    [Fact]
    public void OneCellHasNothingToMergeWith()
    {
        var doc = Tables.Grid(2, 2);
        var cell = Tables.CellAt(doc, 0, 0);

        Assert.Null(TableEdits.MergeCells(doc, cell, cell));
    }

    [Fact]
    public void AColumnBesideAMergedCellMergesWithoutDisturbingIt()
    {
        // The two cells of column 1 make a rectangle that never reaches column 0, so the tall
        // cell beside them is not touched and not swallowed. The row below is left carrying
        // nothing of its own, and covered by the two merges either side of it.
        var doc = Tables.Of(
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                 TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("b2")]));

        var after = Tables.Apply(
            doc,
            TableEdits.MergeCells(doc, Tables.CellAt(doc, 0, 1), Tables.CellAt(doc, 1, 1)));

        Assert.Equal(["tall(2x1)|b1 b2(2x1)", ""], Tables.Shape(after));
    }

    [Fact]
    public void AMergeThatWouldBeRaggedTakesTheWholeOverhangingCellWithIt()
    {
        // Selecting one cell of the top row and the wide cell below it gives a rectangle the wide
        // cell hangs out of, so the rectangle grows sideways until it contains it - and that
        // sweeps in the second cell of the top row. That growth is TableGrid.Rectangle's doing,
        // and it is why merging needs no raggedness check of its own: there is no ragged
        // rectangle for it to be handed.
        var doc = Tables.Of(
            new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
            new TableRowNode([new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2)]));

        var after = Tables.Apply(
            doc,
            TableEdits.MergeCells(doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 1, 0)));

        Assert.Equal(["a1 b1 wide(2x2)", ""], Tables.Shape(after));
    }

    [Fact]
    public void AMergedCellSplitsBackIntoOneCellPerSlot()
    {
        var doc = Tables.Of(
            new TableRowNode(
                [new TableCellNode(
                    [ParagraphNode.FromText("merged")], rowSpan: 2, columnSpan: 2)]),
            new TableRowNode([]));

        var after = Tables.Apply(doc, TableEdits.SplitCell(doc, Tables.CellAt(doc, 0, 0)));

        Assert.Equal(["merged|", "|"], Tables.Shape(after));
    }

    [Fact]
    public void SplittingKeepsTheLookOfTheCellItCameFrom()
    {
        var doc = Tables.Of(
            new TableRowNode(
                [new TableCellNode(
                    [ParagraphNode.FromText("merged")],
                    columnSpan: 2,
                    background: new Rgba(1, 2, 3, 255),
                    isHeader: true)]));

        var after = Tables.Apply(doc, TableEdits.SplitCell(doc, Tables.CellAt(doc, 0, 0)));

        Assert.All(
            Tables.Table(after).Rows[0].Cells,
            cell =>
            {
                Assert.True(cell.IsHeader);
                Assert.Equal(new Rgba(1, 2, 3, 255), cell.Background);
            });
    }

    [Fact]
    public void APlainCellHasNothingToSplit()
    {
        var doc = Tables.Grid(2, 2);

        Assert.Null(TableEdits.SplitCell(doc, Tables.CellAt(doc, 0, 0)));
    }

    [Fact]
    public void AnEditOutsideATableIsNoEdit()
    {
        var doc = DocumentNode.FromParagraphs("not a table");

        Assert.Null(TableEdits.InsertRows(doc, 1, 1, below: true));
        Assert.Null(TableEdits.DeleteRows(doc, 1, 1));
        Assert.Null(TableEdits.InsertColumns(doc, 1, 1, after: true));
        Assert.Null(TableEdits.DeleteColumns(doc, 1, 1));
        Assert.Null(TableEdits.MergeCells(doc, 1, 1));
        Assert.Null(TableEdits.SplitCell(doc, 1));
    }
}

/// <summary>
/// What a table edit says about where positions went.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of the feature that has no visible symptom until it is wrong. A step that
/// rewrites the table correctly and describes the movement badly leaves every held position —
/// the caret, a find match, a link anchor — pointing somewhere else, and the document itself
/// looks perfect.
/// </para>
/// <para>
/// The available cheap answer is to call the whole table one replaced range. These tests exist
/// to refuse it: they check that a caret in a row the edit did not disturb comes out at the same
/// offset in the same text, which that answer cannot do.
/// </para>
/// </remarks>
public class TableStructureStepMapTests
{
    public static TheoryData<string> Operations() =>
        ["row above", "row below", "row gone", "column left", "column right", "column gone",
         "merge", "split"];

    [Theory]
    [MemberData(nameof(Operations))]
    public void TheMapAccountsForExactlyAsMuchAsTheDocumentGrewOrShrank(string operation)
    {
        var (doc, step) = Sample(operation);
        var after = Tables.Apply(doc, step);

        Assert.Equal(after.ContentSize - doc.ContentSize, step.GetMap().SizeDelta);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void EveryUndeletedPositionSurvivesTheRoundTripThroughTheInverse(string operation)
    {
        var (doc, step) = Sample(operation);
        var map = step.GetMap();
        var back = map.Invert();
        var survivors = 0;

        for (var pos = 0; pos <= doc.ContentSize; pos++)
        {
            var forward = map.MapWithResult(pos);

            if (forward.Deleted)
            {
                continue;
            }

            Assert.Equal(pos, back.Map(forward.Pos));
            survivors++;
        }

        Assert.True(survivors > doc.ContentSize / 2, $"only {survivors} positions round tripped");
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void UndoingAStepPutsBackTheDocumentItStartedFrom(string operation)
    {
        var (doc, step) = Sample(operation);
        var after = Tables.Apply(doc, step);
        var undone = step.Invert(doc).Apply(after);

        Assert.True(undone.IsOk, undone.Failure);
        Assert.Equal(doc, undone.Doc);
    }

    [Fact]
    public void ACaretInAnUntouchedRowKeepsItsPlaceWhenARowIsInserted()
    {
        var doc = Tables.Grid(3, 2);
        var step = TableEdits.InsertRows(
            doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 0), below: true);

        // Between the two characters of the last cell of the last row - as far from the edit as
        // the table goes, and the first thing a whole-table range would throw away.
        AssertStaysPut(doc, step!, Tables.CellAt(doc, 2, 1) + 2);
    }

    [Fact]
    public void ACaretKeepsItsPlaceInEveryRowWhenAColumnIsInserted()
    {
        var doc = Tables.Grid(3, 2);
        var step = TableEdits.InsertColumns(
            doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 0), after: true);

        for (var row = 0; row < 3; row++)
        {
            AssertStaysPut(doc, step!, Tables.CellAt(doc, row, 1) + 2);
        }
    }

    [Fact]
    public void ACaretKeepsItsPlaceWhenAnotherRowIsDeleted()
    {
        var doc = Tables.Grid(3, 2);
        var cell = Tables.CellAt(doc, 1, 0);

        AssertStaysPut(doc, TableEdits.DeleteRows(doc, cell, cell)!, Tables.CellAt(doc, 2, 1) + 2);
    }

    [Fact]
    public void ACaretKeepsItsPlaceWhenAnotherColumnIsDeleted()
    {
        var doc = Tables.Grid(3, 3);
        var cell = Tables.CellAt(doc, 0, 1);

        AssertStaysPut(
            doc, TableEdits.DeleteColumns(doc, cell, cell)!, Tables.CellAt(doc, 2, 2) + 2);
    }

    [Fact]
    public void AnInsertedColumnIsOneRangePerRowRatherThanOneOverTheWholeTable()
    {
        // The point of the whole multi-range map, stated as a number. One range here would be
        // arithmetically sound and would collapse every position in the table onto its edge.
        var doc = Tables.Grid(3, 2);
        var step = TableEdits.InsertColumns(
            doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 0), after: true);

        Assert.Equal(3, step!.GetMap().RangeCount);
    }

    [Fact]
    public void AnInsertedRowIsOneRangeBecauseThatIsAllItDisturbs()
    {
        var doc = Tables.Grid(3, 2);
        var step = TableEdits.InsertRows(
            doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 0, 0), below: true);

        Assert.Equal(1, step!.GetMap().RangeCount);
    }

    [Fact]
    public void AStepBuiltAgainstADifferentTableRefusesToApply()
    {
        // The map is derived from the pair of tables, so applying the replacement to a table that
        // has changed underneath would move positions by an amount describing a document that is
        // no longer there.
        var doc = Tables.Grid(2, 2);
        var cell = Tables.CellAt(doc, 0, 0);
        var step = TableEdits.InsertRows(doc, cell, cell, below: true)!;

        var typed = new ReplaceStep(cell + 2, cell + 2, Slice.OfText("x")).Apply(doc);

        Assert.True(typed.IsOk, typed.Failure);

        var result = step.Apply(typed.Doc!);

        Assert.False(result.IsOk);
        Assert.Contains("not the one this step was built against", result.Failure!, StringComparison.Ordinal);
    }

    [Fact]
    public void AStepWhoseTableWasDeletedRebasesToNothing()
    {
        var doc = Tables.Grid(2, 2);
        var cell = Tables.CellAt(doc, 0, 0);
        var step = TableEdits.InsertRows(doc, cell, cell, below: true)!;

        Assert.Null(step.Map(Mapping.Of(new StepMap(0, doc.ContentSize, 1))));
    }

    /// <summary>
    /// Asserts that a caret lands at the same offset in the same text after the step.
    /// </summary>
    /// <remarks>
    /// Comparing the paragraph's text rather than the raw number is what makes this a check on
    /// the map rather than on arithmetic: a position that survives numerically but lands in the
    /// wrong cell would pass an equality on the integer alone.
    /// </remarks>
    private static void AssertStaysPut(DocumentNode doc, Step step, int pos)
    {
        var after = Tables.Apply(doc, step);
        var mapped = step.GetMap().MapWithResult(pos);

        Assert.False(mapped.Deleted, $"position {pos} was reported deleted");

        var before = doc.Resolve(pos);
        var now = after.Resolve(mapped.Pos);

        Assert.Equal(before.Paragraph?.Content.Text, now.Paragraph?.Content.Text);
        Assert.Equal(before.ParentOffset, now.ParentOffset);
    }

    private static (DocumentNode Doc, Step Step) Sample(string operation)
    {
        var doc = Tables.Grid(3, 3);
        var first = Tables.CellAt(doc, 0, 0);
        var middle = Tables.CellAt(doc, 1, 1);

        Step? step = operation switch
        {
            "row above" => TableEdits.InsertRows(doc, middle, middle, below: false),
            "row below" => TableEdits.InsertRows(doc, middle, middle, below: true),
            "row gone" => TableEdits.DeleteRows(doc, middle, middle),
            "column left" => TableEdits.InsertColumns(doc, middle, middle, after: false),
            "column right" => TableEdits.InsertColumns(doc, middle, middle, after: true),
            "column gone" => TableEdits.DeleteColumns(doc, middle, middle),
            "merge" => TableEdits.MergeCells(doc, first, middle),
            "split" => Split(ref doc),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null),
        };

        Assert.NotNull(step);

        return (doc, step);
    }

    /// <summary>Merges first, so that there is something to split.</summary>
    private static Step? Split(ref DocumentNode doc)
    {
        var merged = Tables.Apply(
            doc,
            TableEdits.MergeCells(doc, Tables.CellAt(doc, 0, 0), Tables.CellAt(doc, 1, 1)));

        doc = merged;

        return TableEdits.SplitCell(doc, Tables.CellAt(doc, 0, 0));
    }
}
