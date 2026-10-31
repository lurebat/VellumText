using Avalonia;
using Avalonia.Headless.XUnit;

namespace VellumText.Avalonia.Tests;

/// <summary>
/// A row or column command on a long table works the grid out once, rather than once for the
/// selection, again for the edit, and again to place the caret.
/// </summary>
/// <remarks>
/// At 10,000 × 5 an insert-row command built <see cref="TableGrid"/> — placements plus a dictionary
/// of every cell — three times and placed the cells six times. See the structural-edit profile in
/// <c>docs/plans/2026-10-28-table-edit-scaling-research.md</c>.
/// </remarks>
public sealed class TableStructureCostTests
{
    private const double Width = 800;
    private const double Height = 600;
    private const int Rows = 10_000;

    private static DocumentNode Document() =>
        new([new TableNode(Enumerable.Range(0, Rows).Select(r =>
            new TableRowNode(Enumerable.Range(0, 5).Select(c => TableCellNode.FromText($"{r}.{c}")))))]);

    /// <summary>The first text position of row <paramref name="row"/>, column 0, of a table at the start of the document.</summary>
    private static int TextIn(DocumentNode doc, int row)
    {
        var table = (TableNode)doc.Blocks[0];
        var pos = 1;

        for (var r = 0; r < row; r++)
        {
            pos += table.Rows[r].NodeSize;
        }

        // Into the row, the cell and its paragraph.
        return pos + 3;
    }

    private static void Layout(RichTextView view)
    {
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
    }

    [AvaloniaTheory]
    [InlineData("insert row below", 26)]
    [InlineData("delete row", 26)]
    [InlineData("insert column right", 37)]
    public void A_structural_command_on_a_long_table_builds_its_grid_once(string command, int limitMb)
    {
        var view = new RichTextView { State = EditorState.Create(Document()) };
        Layout(view);

        Func<bool> act = command switch
        {
            "insert row below" => view.InsertRowBelow,
            "delete row" => view.DeleteRow,
            _ => view.InsertColumnRight,
        };

        // Once to pay for anything the first edit builds, then the measured one.
        view.MoveTo(TextIn(view.State.Doc, Rows / 2), false);
        Assert.True(act());
        Layout(view);
        view.MoveTo(TextIn(view.State.Doc, Rows / 2), false);
        Layout(view);

        WorkCounters.TableGridsBuilt = 0;
        WorkCounters.TablePlacements = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();

        Assert.True(act());
        Layout(view);

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        // One grid, for the selection the command acts on. The edit reuses it, and the caret is
        // found from the new table's placements.
        Assert.Equal(1, WorkCounters.TableGridsBuilt);

        // That grid's placement of the old table; then the new table, placed once each by
        // validation, the caret and the view.
        Assert.Equal(4, WorkCounters.TablePlacements);

        // The caret lands in the cell the command names, not wherever the old position mapped to.
        var doc = view.State.Doc;
        var cell = TableGrid.CellContaining(doc, view.State.Selection.From);
        Assert.NotNull(cell);

        Assert.True(bytes < limitMb * 1024L * 1024, $"{command} allocated {bytes / (1024 * 1024)} MB");
    }
}
