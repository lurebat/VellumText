using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Updating a table in place instead of rebuilding it.
/// </summary>
/// <remarks>
/// <para>
/// A table was the only block <c>Reconcile</c> could not update in place, so every keystroke
/// anywhere inside one threw away the view of every cell and built a fresh <c>TextLayout</c> for
/// each. Measured on a 50×5 table that was 18.5 ms per typed character against a 16.7 ms frame,
/// and it got worse the bigger the table was.
/// </para>
/// <para>
/// The claim is therefore about <em>what work happens</em>, not about how long it takes, so
/// these tests count block views built rather than timing anything. <see cref="TableBlockView"/>
/// takes the factory that builds them, which makes the count observable without the view having
/// to report on itself. The other half of the claim — that the reuse does not corrupt anything —
/// is checked against positions and pixels, because a view kept when it should have been rebuilt
/// shows up as a caret in the wrong cell or as stale text on screen.
/// </para>
/// </remarks>
public class TableBlockViewUpdateTests
{
    private static readonly IBrush Border = Brushes.Black;

    private int _built;

    private TableBlockView Built(TableNode table, double width = 400)
    {
        var host = new RichTextView { State = EditorState.Create(new DocumentNode([table])) };

        host.Measure(new Size(width, 800));

        var inner = (TableBlockView)host.Blocks[0];

        // Built through the real factory the presenter uses, so the children are the same views
        // the control would have, then counted from here on.
        var view = new TableBlockView(table, Build, Border);

        _built = 0;

        GC.KeepAlive(inner);
        view.Measure(width);

        return view;

        BlockView Build(BlockNode block)
        {
            _built++;

            return CreateFor(host, block);
        }
    }

    private static BlockView CreateFor(RichTextView host, BlockNode block)
    {
        var doc = new DocumentNode([block]);
        var probe = new RichTextView { State = EditorState.Create(doc), TextStyles = host.TextStyles };

        probe.Measure(new Size(400, 800));

        return probe.Blocks[0];
    }

    private static TableNode Grid(int rows, int columns, string? mark = null) =>
        new(Enumerable
            .Range(0, rows)
            .Select(r => new TableRowNode(Enumerable
                .Range(0, columns)
                .Select(c => TableCellNode.FromText($"{mark}r{r}c{c}"))
                .ToArray()))
            .ToArray());

    /// <summary>The same table with one cell's text replaced.</summary>
    private static TableNode Retyped(TableNode table, int row, int column, string text)
    {
        var rows = table.Rows.ToArray();
        var cells = rows[row].Cells.ToArray();

        cells[column] = TableCellNode.FromText(text);
        rows[row] = new TableRowNode(cells);

        return new TableNode(rows, table.ColumnWidths);
    }

    // ---- that the work is actually skipped ---------------------------------------------------

    [AvaloniaFact]
    public void EditingOneCellRebuildsOnlyThatCell()
    {
        var table = Grid(10, 5);
        var view = Built(table);

        Assert.True(view.Update(Retyped(table, 4, 2, "edited")));

        // One paragraph changed. The other 49 cells hold the very same node they did before, so
        // there is nothing to build for them.
        Assert.Equal(0, _built);
    }

    [AvaloniaFact]
    public void AnUnchangedTableIsNotRebuiltAtAll()
    {
        var table = Grid(6, 4);
        var view = Built(table);

        Assert.False(view.Update(table));
        Assert.Equal(0, _built);
    }

    /// <remarks>
    /// The reuse is worth nothing if the update path is never reached. This goes through the
    /// control rather than calling <c>Update</c>, so it fails if <c>Reconcile</c> stops routing a
    /// table to it and goes back to building a new view.
    /// </remarks>
    [AvaloniaFact]
    public void TypingInACellKeepsTheViewTheTableAlreadyHad()
    {
        var host = new RichTextView { State = EditorState.Create(new DocumentNode([Grid(8, 4)])) };

        host.Measure(new Size(400, 800));

        var before = host.Blocks[0];

        host.MoveTo(host.Slots[0].Start + 3, extend: false);

        Assert.True(host.InsertText("x"));

        host.Measure(new Size(400, 800));

        Assert.Same(before, host.Blocks[0]);
    }

    [AvaloniaFact]
    public void AddingARowBuildsOnlyTheBlocksInIt()
    {
        var table = Grid(5, 3);
        var view = Built(table);

        var rows = table.Rows.ToList();

        rows.Add(new TableRowNode([
            TableCellNode.FromText("new0"),
            TableCellNode.FromText("new1"),
            TableCellNode.FromText("new2"),
        ]));

        Assert.True(view.Update(new TableNode(rows)));

        // Appending leaves the cells before it in the same place, so only the three new ones are
        // built. A row inserted at the top would shift every cell along and rebuild more, which
        // is correct and is what the positional match is for.
        Assert.Equal(3, _built);
    }

    /// <remarks>
    /// <para>
    /// The one that catches a table quietly refusing to update. Hit-testing the table's own caret
    /// rectangle is not enough: both answers come from the same local starts, so a view holding
    /// stale positions agrees with itself perfectly. This asks the <em>control</em> where a
    /// position is, and the control computes that from the document rather than from the view.
    /// </para>
    /// <para>
    /// The edit is in the first cell and the caret is checked in the last, because a stale start
    /// is off by exactly the number of characters inserted before it.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void ACellAfterAnEditIsStillDrawnWhereTheDocumentSaysItIs()
    {
        var host = new RichTextView { State = EditorState.Create(new DocumentNode([Grid(3, 3)])) };

        host.Measure(new Size(400, 800));

        // Absolute positions, not BlockStart-relative ones: BlockStart follows the caret, so
        // reading it after a move points at whichever cell the caret has just landed in.
        var table = host.Slots[0].Start;

        End(host);

        var before = host.CaretRect();

        // Two boundaries into the table and one into the paragraph: the first cell, a long way
        // before the last one.
        host.MoveTo(table + 3, extend: false);
        host.InsertText("a good deal of inserted text");
        host.Measure(new Size(400, 800));

        End(host);

        // The first cell got taller, so everything after it moved down. Nothing moved sideways:
        // the columns are evenly divided and there are still three of them.
        Assert.Equal(before.X, host.CaretRect().X, 1);
        Assert.True(
            host.CaretRect().Y > before.Y,
            "the row below the edited cell did not move down, so nothing relaid out");

        static void End(RichTextView host)
        {
            host.MoveTo(host.State.Doc.ContentSize - 4, extend: false);
            host.Measure(new Size(400, 800));
        }
    }

    /// <remarks>
    /// Kills the case where the view is kept but never told anything: the update is skipped, the
    /// same view stays on screen, and every position still agrees with itself — just with the
    /// text as it was before the edit.
    /// </remarks>
    [AvaloniaFact]
    public void TypingInACellChangesWhatTheTableDraws()
    {
        var host = new RichTextView { State = EditorState.Create(new DocumentNode([Grid(2, 2)])) };
        var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300, Content = host };

        window.Show();

        var before = Ink(window);

        host.MoveTo(host.Slots[0].Start + 3, extend: false);
        host.InsertText("WWWWWWWWWWWWWWWW");

        Assert.NotEqual(before, Ink(window));
    }

    // ---- that reuse does not corrupt anything -------------------------------------------------

    /// <remarks>
    /// Forgetting to record the new table is invisible until the same node comes back — an undo,
    /// or a document set to a value it held before — at which point the early-out reports nothing
    /// to do and the screen keeps showing the edit that was just undone.
    /// </remarks>
    [AvaloniaFact]
    public void UpdatingBackToTheTableItHadShowsTheOldTextAgain()
    {
        var table = Grid(2, 2);
        var view = Built(table, width: 400);

        var at = 1 + 1 + 1;
        var edited = Retyped(table, 0, 0, "an entirely different and much wider piece of text");

        view.Update(edited);
        view.Measure(400);

        var wide = view.GetCaretRect(at + 40);

        Assert.True(view.Update(table));

        view.Measure(400);

        // The original cell has no fortieth position, so a caret asking for one is clamped to the
        // end of "r0c0" — which is a long way left of where it was in the longer text.
        Assert.True(
            view.GetCaretRect(at + 40).X < wide.X,
            "the table still shows the edited text after being told to go back");
    }

    [AvaloniaFact]
    public void AddingAParagraphToACellBuildsOnlyThatParagraph()
    {
        var table = Grid(3, 2);
        var view = Built(table);

        var rows = table.Rows.ToArray();
        var cells = rows[1].Cells.ToArray();

        cells[0] = new TableCellNode([
            ParagraphNode.FromText("r1c0"),
            ParagraphNode.FromText("and a second paragraph"),
        ]);

        rows[1] = new TableRowNode(cells);

        Assert.True(view.Update(new TableNode(rows)));

        Assert.Equal(1, _built);
    }

    [AvaloniaFact]
    public void TheEditedCellShowsItsNewText()
    {
        var table = Grid(4, 3);
        var view = Built(table);
        var edited = Retyped(table, 2, 1, "quite a lot more text than was there before");

        view.Update(edited);
        view.Measure(400);

        var start = 0;

        for (var r = 0; r < 2; r++)
        {
            start += edited.Rows[r].NodeSize;
        }

        // Two boundaries into the row, one into the cell, one into the paragraph.
        var at = start + 1 + edited.Rows[2].Cells[0].NodeSize + 1 + 1;
        var caret = view.GetCaretRect(at + "quite a lot".Length);

        Assert.Equal(at + "quite a lot".Length, view.HitTest(caret.Center));
    }

    /// <remarks>
    /// The failure this defends against is the quiet one: a cell after the edited one keeps a
    /// view whose local start was computed against the old text length, so its caret lands in the
    /// neighbouring cell. It only shows up when the edit changes how many positions there are.
    /// </remarks>
    [AvaloniaFact]
    public void ACellAfterALongerEditStillHitTestsToItself()
    {
        var table = Grid(3, 3);
        var view = Built(table);
        var edited = Retyped(table, 0, 0, "a much longer piece of text than r0c0 was");

        view.Update(edited);
        view.Measure(400);

        var start = edited.Rows[0].NodeSize + 1;

        foreach (var cell in edited.Rows[1].Cells)
        {
            var inside = start + 2;
            var caret = view.GetCaretRect(inside);

            Assert.Equal(inside, view.HitTest(caret.Center));

            start += cell.NodeSize;
        }
    }

    [AvaloniaFact]
    public void TheTableRemeasuresAfterAnEdit()
    {
        var table = Grid(3, 2);
        var view = Built(table, width: 120);

        var before = view.Measure(120).Height;

        view.Update(Retyped(table, 1, 0, string.Join(' ', Enumerable.Repeat("wrap", 40))));

        Assert.True(
            view.Measure(120).Height > before,
            $"the table stayed {before} tall after a cell grew by forty words");
    }

    [AvaloniaFact]
    public void AHeaderCellKeepsItsFillThroughAnEditElsewhere()
    {
        var header = new TableCellNode([ParagraphNode.FromText("head")], isHeader: true);
        var table = new TableNode([
            new TableRowNode([header, TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]);

        var host = new RichTextView
        {
            State = EditorState.Create(new DocumentNode([table])),
            TableHeaderBrush = Brushes.Red,
        };

        var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300, Content = host };

        window.Show();

        var before = Red(window);

        Assert.True(before > 0, "the header tint was never painted, so this cannot detect losing it");

        host.MoveTo(host.Slots[0].Start + table.Rows[0].NodeSize + 4, extend: false);
        host.InsertText("x");

        Assert.Equal(before, Red(window));
    }

    /// <remarks>
    /// The update path keeps a reference to the header brush it was built with, which is a new way
    /// for the tint to go stale: a cell reused across an edit would keep the old brush. It does not
    /// happen because changing the brush drops every view — but that is a fact about another class,
    /// so it is checked here rather than assumed.
    /// </remarks>
    [AvaloniaFact]
    public void ChangingTheHeaderBrushThenTypingUsesTheNewTint()
    {
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("head")], isHeader: true),
                TableCellNode.FromText("b"),
            ]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]);

        var host = new RichTextView
        {
            State = EditorState.Create(new DocumentNode([table])),
            TableHeaderBrush = Brushes.Lime,
        };

        var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300, Content = host };

        window.Show();

        Assert.Equal(0, Red(window));

        host.TableHeaderBrush = Brushes.Red;

        var tinted = Red(window);

        Assert.True(tinted > 0, "the new header brush was never painted");

        // And it survives an edit in a different cell, which is where a reused view would bring
        // the old brush back.
        host.MoveTo(host.Slots[0].Start + table.Rows[0].NodeSize + 4, extend: false);
        host.InsertText("x");

        Assert.Equal(tinted, Red(window));
    }

    [AvaloniaFact]
    public void ANestedTableIsUpdatedRatherThanRebuilt()
    {
        var nested = Grid(2, 2, "n");
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([nested]),
                TableCellNode.FromText("beside"),
            ]),
        ]);

        var host = new RichTextView { State = EditorState.Create(new DocumentNode([table])) };
        var window = new global::Avalonia.Controls.Window { Width = 400, Height = 300, Content = host };

        window.Show();

        var drawn = Ink(window);
        var outer = host.Blocks[0];

        var view = Built(table);

        var rows = table.Rows.ToArray();
        var cells = rows[0].Cells.ToArray();

        cells[0] = new TableCellNode([Retyped(nested, 1, 1, "changed")]);
        rows[0] = new TableRowNode(cells);

        Assert.True(view.Update(new TableNode(rows)));

        // The nested table is the same view, told to update itself, and it in turn keeps the
        // three cells of its own that nothing touched.
        Assert.Equal(0, _built);

        // And it was actually told: an edit two levels down still reaches the screen. Through the
        // control, because a nested view that quietly kept its old text would satisfy the count
        // above perfectly.
        host.MoveTo(host.Slots[0].Start + 5, extend: false);
        host.InsertText("WWWWWWWWWWWWWWWW");

        Assert.Same(outer, host.Blocks[0]);
        Assert.NotEqual(drawn, Ink(window));
    }

    private static int Ink(global::Avalonia.Controls.Window window)
    {
        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!;

        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];

        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var count = 0;

        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            if (bytes[i] < 128 && bytes[i + 1] < 128 && bytes[i + 2] < 128 && bytes[i + 3] > 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int Red(global::Avalonia.Controls.Window window)
    {
        var frame = global::Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window)!;

        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];

        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var count = 0;

        // CaptureRenderedFrame returns Rgba8888, not BGRA: reading it the other way round counts
        // zero pixels of every colour and makes the assertion vacuous.
        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            if (bytes[i] > 200 && bytes[i + 1] < 80 && bytes[i + 2] < 80)
            {
                count++;
            }
        }

        return count;
    }
}
