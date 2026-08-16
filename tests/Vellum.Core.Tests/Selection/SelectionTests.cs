namespace Vellum.Tests.Selection;

/// <summary>Documents the selection tests share.</summary>
public static class SelectionSample
{
    /// <summary>
    /// A paragraph, a rule, and another paragraph.
    /// </summary>
    /// <remarks>
    /// <code>
    /// 0 &lt;p&gt; 1 o n e 4 &lt;/p&gt; 5   rule 6   6 &lt;p&gt; 7 t w o 10 &lt;/p&gt; 11
    /// </code>
    /// </remarks>
    public static DocumentNode WithRule { get; } = new(
    [
        ParagraphNode.FromText("one"),
        RuleNode.Instance,
        ParagraphNode.FromText("two"),
    ]);

    /// <summary>Nothing a caret can sit in.</summary>
    public static DocumentNode RuleOnly { get; } = new([RuleNode.Instance]);

    /// <summary>
    /// A two by two table of one-character cells.
    /// </summary>
    /// <remarks>
    /// Each cell holds a paragraph of one character, so a cell is 5 positions wide and a row is
    /// 12. The table starts at 0.
    /// <code>
    /// 0  table
    /// 1  row      2 cell 3 &lt;p&gt; 4 a 5 &lt;/p&gt; 6 &lt;/cell&gt;
    ///             7 cell 8 &lt;p&gt; 9 b 10 &lt;/p&gt; 11 &lt;/cell&gt;   12 &lt;/row&gt;
    /// 13 row     14 cell ... c ...
    ///            19 cell ... d ...             24 &lt;/row&gt;
    /// 25 &lt;/table&gt;
    /// </code>
    /// </remarks>
    public static DocumentNode Table { get; } = new(
    [
        new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]),
    ]);

    /// <summary>
    /// A three column table whose middle row merges two cells across columns 0 and 1.
    /// </summary>
    public static DocumentNode MergedTable { get; } = new(
    [
        new TableNode(
        [
            new TableRowNode(
            [
                TableCellNode.FromText("a"),
                TableCellNode.FromText("b"),
                TableCellNode.FromText("c"),
            ]),
            new TableRowNode(
            [
                new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2),
                TableCellNode.FromText("f"),
            ]),
        ]),
    ]);

    /// <summary>A two column table whose first cell spans both rows.</summary>
    public static DocumentNode TallTable { get; } = new(
    [
        new TableNode(
        [
            new TableRowNode(
            [
                new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                TableCellNode.FromText("b"),
            ]),
            new TableRowNode([TableCellNode.FromText("d")]),
        ]),
    ]);
}

/// <summary>Finding somewhere a selection can legally be.</summary>
public class SelectionNearTests
{
    [Fact]
    public void APositionInTextIsItsOwnAnswer()
    {
        var selection = Vellum.Selection.Near(SelectionSample.WithRule, 2);

        Assert.Equal(new TextSelection(2, 2), selection);
    }

    [Fact]
    public void APositionOnARuleSelectsTheRule()
    {
        var selection = Vellum.Selection.Near(SelectionSample.WithRule, 5);

        Assert.Equal(new NodeSelection(5, 1), selection);
    }

    [Fact]
    public void ADocumentWithNoTextStillYieldsASelection()
    {
        Assert.Equal(new NodeSelection(0, 1), Vellum.Selection.Near(SelectionSample.RuleOnly, 0));
    }

    [Fact]
    public void ADocumentWithNothingInItStillYieldsASelection()
    {
        Assert.Equal(new TextSelection(0, 0), Vellum.Selection.Near(new DocumentNode([]), 0));
    }

    [Fact]
    public void TheStartAndEndAreInTheOutermostParagraphs()
    {
        Assert.Equal(
            new TextSelection(1, 1),
            Vellum.Selection.AtStart(SelectionSample.WithRule));

        Assert.Equal(
            new TextSelection(10, 10),
            Vellum.Selection.AtEnd(SelectionSample.WithRule));
    }

    [Fact]
    public void DirectionBreaksTheTie()
    {
        // Position 5 is the rule itself, so both directions find it. Position 6 is the boundary
        // between the rule and the second paragraph: forward finds text at 7, backward the rule.
        Assert.Equal(
            new TextSelection(7, 7),
            Vellum.Selection.Near(SelectionSample.WithRule, 6));

        Assert.Equal(
            new NodeSelection(5, 1),
            Vellum.Selection.Near(SelectionSample.WithRule, 6, forward: false));
    }

    [Fact]
    public void AnchorAndHeadKeepTheirOrder()
    {
        var backwards = new TextSelection(9, 7);

        Assert.Equal(7, backwards.From);
        Assert.Equal(9, backwards.To);
        Assert.False(backwards.IsEmpty);
    }
}

/// <summary>Selecting text.</summary>
public class TextSelectionTests
{
    [Fact]
    public void CreateKeepsAValidPair()
    {
        Assert.Equal(
            new TextSelection(1, 4),
            TextSelection.Create(SelectionSample.WithRule, 1, 4));
    }

    [Fact]
    public void CreateRepairsAHeadThatIsNotInText()
    {
        // 5 is the rule.
        var selection = TextSelection.Create(SelectionSample.WithRule, 1, 5);

        Assert.Equal(new NodeSelection(5, 1), selection);
    }

    [Fact]
    public void CreateCollapsesOntoTheHeadWhenOnlyTheAnchorIsBad()
    {
        var selection = TextSelection.Create(SelectionSample.WithRule, 5, 8);

        Assert.Equal(new TextSelection(8, 8), selection);
    }

    [Fact]
    public void CreateClampsAnOutOfRangePosition()
    {
        var selection = TextSelection.Create(SelectionSample.WithRule, 1, 9999);

        Assert.IsType<TextSelection>(selection);
        Assert.InRange(selection.Head, 0, SelectionSample.WithRule.ContentSize);
    }

    [Fact]
    public void InsertingBeforeASelectionMovesIt()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("xxxone")]);
        var mapped = new TextSelection(1, 4).Map(doc, Mapping.Of(new StepMap(1, 0, 3)));

        Assert.Equal(new TextSelection(4, 7), mapped);
    }

    [Fact]
    public void DeletingThroughASelectionCollapsesIt()
    {
        var doc = new DocumentNode([ParagraphNode.Empty]);
        var mapped = new TextSelection(1, 4).Map(doc, Mapping.Of(new StepMap(1, 3, 0)));

        Assert.Equal(new TextSelection(1, 1), mapped);
    }

    [Fact]
    public void DeletingTheParagraphAParagraphSelectionWasInLandsSomewhereValid()
    {
        // The first paragraph is gone entirely; what remains is a rule then "two".
        var doc = new DocumentNode([RuleNode.Instance, ParagraphNode.FromText("two")]);
        var mapped = new TextSelection(1, 3).Map(doc, Mapping.Of(new StepMap(0, 5, 0)));

        Assert.Equal(new NodeSelection(0, 1), mapped);
    }

    [Fact]
    public void MappingNeverReturnsAPositionOutsideTheDocument()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("ab")]);

        for (var anchor = 0; anchor <= 11; anchor++)
        {
            for (var head = 0; head <= 11; head++)
            {
                var mapped = new TextSelection(anchor, head)
                    .Map(doc, Mapping.Of(new StepMap(0, 11, 4)));

                Assert.InRange(mapped.Anchor, 0, doc.ContentSize);
                Assert.InRange(mapped.Head, 0, doc.ContentSize);
            }
        }
    }
}

/// <summary>Selecting a node whole.</summary>
public class NodeSelectionTests
{
    [Fact]
    public void ARuleCanBeSelected()
    {
        var selection = NodeSelection.At(SelectionSample.WithRule, 5);

        Assert.NotNull(selection);
        Assert.Equal(5, selection.From);
        Assert.Equal(6, selection.To);
    }

    [Fact]
    public void ItCoversTheNodeItSelected()
    {
        // Which is what makes deleting it the same operation as deleting a range of text.
        var selection = NodeSelection.At(SelectionSample.WithRule, 5)!;
        var deleted = ReplaceStep.Delete(selection.From, selection.To)
            .Apply(SelectionSample.WithRule);

        Assert.True(deleted.IsOk, deleted.Failure);
        Assert.Equal(2, deleted.Doc!.Children.Count);
        Assert.DoesNotContain(deleted.Doc!.Children, child => child is RuleNode);
    }

    [Fact]
    public void AParagraphIsNotSelectableAsAUnit()
    {
        Assert.Null(NodeSelection.At(SelectionSample.WithRule, 0));
    }

    [Fact]
    public void APositionInsideTextSelectsNothing()
    {
        Assert.Null(NodeSelection.At(SelectionSample.WithRule, 2));
    }

    [Fact]
    public void ATableIsNotSelectableAsAUnit()
    {
        Assert.Null(NodeSelection.At(SelectionSample.Table, 0));
    }

    [Fact]
    public void InsertingBeforeItMovesIt()
    {
        var doc = new DocumentNode(
        [
            ParagraphNode.FromText("onexxx"),
            RuleNode.Instance,
            ParagraphNode.FromText("two"),
        ]);

        var mapped = new NodeSelection(5, 1).Map(doc, Mapping.Of(new StepMap(4, 0, 3)));

        Assert.Equal(new NodeSelection(8, 1), mapped);
    }

    [Fact]
    public void DeletingItLeavesACaretNearby()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("one"), ParagraphNode.FromText("two")]);
        var mapped = new NodeSelection(5, 1).Map(doc, Mapping.Of(new StepMap(5, 1, 0)));

        Assert.IsType<TextSelection>(mapped);
        Assert.InRange(mapped.Head, 0, doc.ContentSize);
    }
}

/// <summary>Laying a table out as a grid.</summary>
public class TableGridTests
{
    [Fact]
    public void APlainTableIsItsOwnGrid()
    {
        var grid = TableGrid.ForCell(SelectionSample.Table, 2);

        Assert.NotNull(grid);
        Assert.Equal(0, grid.TablePos);
        Assert.Equal(2, grid.Rows);
        Assert.Equal(2, grid.Columns);
        Assert.Equal(2, grid.At(0, 0)!.Value.Pos);
        Assert.Equal(7, grid.At(0, 1)!.Value.Pos);
        Assert.Equal(14, grid.At(1, 0)!.Value.Pos);
        Assert.Equal(19, grid.At(1, 1)!.Value.Pos);
    }

    [Fact]
    public void EveryCellPositionIsActuallyBeforeACell()
    {
        var grid = TableGrid.ForCell(SelectionSample.Table, 2)!;

        foreach (var pos in grid.Cells.Keys)
        {
            Assert.IsType<TableCellNode>(SelectionSample.Table.Resolve(pos).NodeAfter);
        }
    }

    [Fact]
    public void APositionThatIsNotACellHasNoGrid()
    {
        Assert.Null(TableGrid.ForCell(SelectionSample.Table, 0));
        Assert.Null(TableGrid.ForCell(SelectionSample.WithRule, 5));
    }

    [Fact]
    public void AMergedCellOccupiesBothItsColumns()
    {
        var grid = TableGrid.ForCell(SelectionSample.MergedTable, 2)!;

        Assert.Equal(3, grid.Columns);

        var wide = grid.At(1, 0)!.Value;
        Assert.Equal(wide.Pos, grid.At(1, 1)!.Value.Pos);
        Assert.Equal(0, wide.Left);
        Assert.Equal(2, wide.Right);
    }

    [Fact]
    public void ACellThatSpansRowsShiftsTheRowBelowIt()
    {
        // The second row lists one cell, but it belongs in column 1, not column 0.
        var grid = TableGrid.ForCell(SelectionSample.TallTable, 2)!;

        var tall = grid.At(0, 0)!.Value;
        Assert.Equal(tall.Pos, grid.At(1, 0)!.Value.Pos);
        Assert.NotEqual(tall.Pos, grid.At(1, 1)!.Value.Pos);
    }

    [Fact]
    public void ARectangleOfOneCellIsThatCell()
    {
        var grid = TableGrid.ForCell(SelectionSample.Table, 2)!;

        Assert.Equal(new[] { 2 }, grid.Rectangle(2, 2).ToArray());
    }

    [Fact]
    public void ARectangleAcrossADiagonalTakesTheWholeTable()
    {
        var grid = TableGrid.ForCell(SelectionSample.Table, 2)!;

        Assert.Equal(new[] { 2, 7, 14, 19 }, grid.Rectangle(2, 19).ToArray());
    }

    [Fact]
    public void ARectangleIsTheSameDraggedEitherWay()
    {
        var grid = TableGrid.ForCell(SelectionSample.Table, 2)!;

        Assert.Equal(grid.Rectangle(2, 19).ToArray(), grid.Rectangle(19, 2).ToArray());
    }

    [Fact]
    public void ARectangleGrowsToSwallowAMergedCellItTouches()
    {
        // Column 0 alone is not a closed rectangle: the merged cell in row 1 reaches into
        // column 1, so selecting it drags column 1 in as well.
        var grid = TableGrid.ForCell(SelectionSample.MergedTable, 2)!;
        var topLeft = grid.At(0, 0)!.Value.Pos;
        var wide = grid.At(1, 0)!.Value.Pos;

        var rectangle = grid.Rectangle(topLeft, wide);

        Assert.Equal(3, rectangle.Length);
        Assert.Contains(grid.At(0, 1)!.Value.Pos, rectangle);
        Assert.DoesNotContain(grid.At(0, 2)!.Value.Pos, rectangle);
    }

    [Fact]
    public void ARectangleListsEachMergedCellOnlyOnce()
    {
        var grid = TableGrid.ForCell(SelectionSample.MergedTable, 2)!;
        var all = grid.Rectangle(grid.At(0, 0)!.Value.Pos, grid.At(1, 2)!.Value.Pos);

        Assert.Equal(all.Length, all.Distinct().Count());
        Assert.Equal(5, all.Length);
    }

    [Fact]
    public void GrowingTheRectangleCanUncoverSomethingThatGrowsItAgain()
    {
        // Widening leftwards brings a column into the rectangle that was never looked at, and
        // that column holds a cell reaching up above it. One sweep finds the first growth and
        // stops; only re-running the sweep over the enlarged rectangle finds the second.
        //
        //      col 0   col 1   col 2   col 3
        // r0     p    | q     |  r    |  s
        // r1     t    | ^q    |  u    |  v
        // r2     w    | x-------------|  y
        var doc = new DocumentNode(
        [
            new TableNode(
            [
                new TableRowNode(
                [
                    TableCellNode.FromText("p"),
                    new TableCellNode([ParagraphNode.FromText("q")], rowSpan: 2),
                    TableCellNode.FromText("r"),
                    TableCellNode.FromText("s"),
                ]),
                new TableRowNode(
                [
                    TableCellNode.FromText("t"),
                    TableCellNode.FromText("u"),
                    TableCellNode.FromText("v"),
                ]),
                new TableRowNode(
                [
                    TableCellNode.FromText("w"),
                    new TableCellNode([ParagraphNode.FromText("x")], columnSpan: 2),
                    TableCellNode.FromText("y"),
                ]),
            ]),
        ]);

        Assert.Empty(DocumentSchema.Validate(doc));

        var grid = TableGrid.ForCell(doc, 2)!;
        var rectangle = grid.Rectangle(grid.At(1, 2)!.Value.Pos, grid.At(2, 3)!.Value.Pos);

        // Columns 1 to 3 of every row: q, r, s, u, v, x, y. Column 0 stays out.
        Assert.Equal(7, rectangle.Length);
        Assert.Contains(grid.At(0, 1)!.Value.Pos, rectangle);
        Assert.Contains(grid.At(0, 3)!.Value.Pos, rectangle);
        Assert.DoesNotContain(grid.At(0, 0)!.Value.Pos, rectangle);
    }
}

/// <summary>Selecting a rectangle of cells.</summary>
public class CellSelectionTests
{
    [Fact]
    public void TwoCellsOfOneTableMakeASelection()
    {
        var selection = CellSelection.Create(SelectionSample.Table, 2, 19);

        Assert.NotNull(selection);
        Assert.Equal(new[] { 2, 7, 14, 19 }, selection.Cells(SelectionSample.Table).ToArray());
    }

    [Fact]
    public void APositionThatIsNotACellMakesNoSelection()
    {
        Assert.Null(CellSelection.Create(SelectionSample.Table, 0, 19));
        Assert.Null(CellSelection.Create(SelectionSample.Table, 2, 4));
    }

    [Fact]
    public void CellsOfDifferentTablesMakeNoSelection()
    {
        var doc = new DocumentNode(
        [
            (TableNode)SelectionSample.Table.Children[0],
            (TableNode)SelectionSample.Table.Children[0],
        ]);

        // The first table occupies 0..26, so 28 is the first cell of the second.
        Assert.NotNull(TableGrid.ForCell(doc, 28));
        Assert.Null(CellSelection.Create(doc, 2, 28));
    }

    [Fact]
    public void EditingInsideACellKeepsTheSelection()
    {
        var doc = SelectionSample.Table;
        var typed = new ReplaceStep(4, 4, Slice.OfText("z")).Apply(doc);

        Assert.True(typed.IsOk, typed.Failure);

        var mapped = new CellSelection(2, 19).Map(typed.Doc!, Mapping.Of(new StepMap(4, 0, 1)));
        var cells = Assert.IsType<CellSelection>(mapped);

        Assert.Equal(new[] { 2, 8, 15, 20 }, cells.Cells(typed.Doc!).ToArray());
    }

    [Fact]
    public void DeletingOneCornerOfTheRectangleDegradesToACaretInsideTheDocument()
    {
        // The anchor's row is deleted; the head's cell survives but has moved. There is no
        // rectangle left to describe, so the answer is a caret - and it has to be a caret in
        // the document that now exists, not at the head's address in the one that does not.
        var row = new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]);
        var doc = new DocumentNode([new TableNode([row, row, row])]);
        var deleted = new ReplaceStep(1, 13, Slice.Empty).Apply(doc);

        Assert.True(deleted.IsOk, deleted.Failure);

        // 26 is the first cell of the third row, which the deletion moves down to 14.
        var mapped = new CellSelection(2, 26)
            .Map(deleted.Doc!, Mapping.Of(new StepMap(1, 12, 0)));

        Assert.IsNotType<CellSelection>(mapped);

        // The surviving head cell now starts at 14 and is five positions long. Landing anywhere
        // else means the caret was placed from the old document's numbering; Near clamps, so the
        // reader would find their caret thrown towards the end of the document.
        Assert.InRange(mapped.Head, 14, 18);
    }

    [Fact]
    public void DeletingTheTableDegradesToACaret()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("after")]);
        var mapped = new CellSelection(2, 19).Map(doc, Mapping.Of(new StepMap(0, 26, 7)));

        Assert.IsNotType<CellSelection>(mapped);
        Assert.InRange(mapped.Head, 0, doc.ContentSize);
    }

    [Fact]
    public void ItBoundsButDoesNotCoverWhatItSelects()
    {
        // The reason commands must ask Cells rather than read From and To: the range between
        // them runs straight through cells the selection does not include.
        var selection = CellSelection.Create(SelectionSample.MergedTable, 2, 7)!;

        Assert.Equal(2, selection.From);
        Assert.Equal(7, selection.To);
        Assert.Equal(new[] { 2, 7 }, selection.Cells(SelectionSample.MergedTable).ToArray());
    }
}

/// <summary>Promoting two arbitrary positions to a rectangle, which is what a drag needs.</summary>
public class CellSelectionAcrossTests
{
    [Fact]
    public void TwoPositionsInDifferentCellsSelectTheRectangleBetweenThem()
    {
        // 4 is the character in the top-left cell, 21 the one in the bottom-right.
        var selection = CellSelection.Across(SelectionSample.Table, 4, 21);

        Assert.NotNull(selection);
        Assert.Equal(new[] { 2, 7, 14, 19 }, selection.Cells(SelectionSample.Table).ToArray());
    }

    [Fact]
    public void TwoPositionsInOneCellAreNotARectangle()
    {
        // Dragging inside a cell is ordinary text selection. Promoting it would take the whole
        // cell and make selecting a single word inside a table impossible.
        Assert.Null(CellSelection.Across(SelectionSample.Table, 3, 5));
    }

    [Fact]
    public void APositionOutsideAnyTableIsNotARectangle()
    {
        Assert.Null(CellSelection.Across(SelectionSample.WithRule, 1, 8));
    }

    [Fact]
    public void OnlyOneEndBeingInATableIsNotARectangle()
    {
        var doc = new DocumentNode(
        [
            (TableNode)SelectionSample.Table.Children[0],
            ParagraphNode.FromText("after"),
        ]);

        // 4 is inside the first cell; 29 is in the paragraph that follows the table.
        Assert.Null(CellSelection.Across(doc, 4, 29));
    }

    [Fact]
    public void OutOfRangePositionsAreNotARectangle()
    {
        Assert.Null(CellSelection.Across(SelectionSample.Table, -1, 4));
        Assert.Null(
            CellSelection.Across(SelectionSample.Table, 4, SelectionSample.Table.ContentSize + 1));
    }

    [Fact]
    public void ADragOutOfANestedTableSelectsCellsOfTheOuterOne()
    {
        // The case the chain walk exists for. Matching at the innermost level would compare a
        // cell of the inner table against a cell of the outer one - two unrelated grids - and
        // find no rectangle at all. The only rectangle containing both ends is in the outer
        // table, so that is the level the selection has to be drawn at.
        var inner = new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("i"), TableCellNode.FromText("j")]),
        ]);

        var outer = new TableNode(
        [
            new TableRowNode(
            [
                new TableCellNode([inner]),
                TableCellNode.FromText("b"),
            ]),
        ]);

        var doc = new DocumentNode([outer]);

        Assert.Empty(DocumentSchema.Validate(doc));

        var outerCells = TableGrid.Build(0, outer);
        var firstOuter = outerCells.At(0, 0)!.Value.Pos;
        var secondOuter = outerCells.At(0, 1)!.Value.Pos;

        // A position deep inside the nested table's first cell.
        var innerGrid = TableGrid.ForCell(doc, firstOuter + 3)!;
        var innerCell = innerGrid.At(0, 0)!.Value.Pos;
        var deep = innerCell + 2;

        Assert.True(doc.Resolve(deep).IsInText, "the probe position should be inside cell text");

        var selection = CellSelection.Across(doc, deep, secondOuter + 2);

        Assert.NotNull(selection);
        Assert.Equal(new[] { firstOuter, secondOuter }, selection.Cells(doc).ToArray());
    }

    [Fact]
    public void ADragWithinOneNestedCellStaysInTheInnerTable()
    {
        // The mirror of the case above: both ends are in the inner table, so the rectangle
        // belongs to the inner table and must not be promoted to the outer one.
        var inner = new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("i"), TableCellNode.FromText("j")]),
        ]);

        var outer = new TableNode(
        [
            new TableRowNode([new TableCellNode([inner]), TableCellNode.FromText("b")]),
        ]);

        var doc = new DocumentNode([outer]);
        var outerFirst = TableGrid.Build(0, outer).At(0, 0)!.Value.Pos;
        var innerGrid = TableGrid.ForCell(doc, outerFirst + 3)!;
        var a = innerGrid.At(0, 0)!.Value.Pos;
        var b = innerGrid.At(0, 1)!.Value.Pos;

        var selection = CellSelection.Across(doc, a + 2, b + 2);

        Assert.NotNull(selection);
        Assert.Equal(new[] { a, b }, selection.Cells(doc).ToArray());
    }
}
