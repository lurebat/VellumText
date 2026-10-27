using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// Tables written from the grid rather than from each row's cell array (F-23, audit of 0282015).
/// </summary>
/// <remarks>
/// <para>
/// Each test reads the export back through the real import path — RtfPipe, then the HTML importer —
/// because what matters about interchange is what a recipient makes of it. Where RtfPipe cannot
/// read a construct at all (a vertical merge comes back as two cells, the second empty), the test
/// pins the control words instead and says so; the replacement reader of Phase 8 is re-verified
/// against the same documents.
/// </para>
/// </remarks>
public class RtfTableExportTests
{
    private static async Task<DocumentNode> Trip(DocumentNode doc) =>
        (await RtfImporter.ImportAsync(RtfExporter.Instance.Export(doc))).Doc;

    private static TableCellNode Cell(params BlockNode[] blocks) => new(blocks);

    private static TableCellNode Cell(string text) => new([ParagraphNode.FromText(text)]);

    private static string TextOf(TableCellNode cell) =>
        string.Join("|", cell.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));

    /// <summary>
    /// A cell spanning two rows leaves the row below one cell short. Written from the row array,
    /// "C" went into the first column, under "A"; from the grid it is under "B", where it belongs.
    /// </summary>
    [Fact]
    public async Task A_row_shortened_by_a_row_span_keeps_its_cells_in_their_columns()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("A")], rowSpan: 2), Cell("B")]),
                new TableRowNode([Cell("C")]),
            ]),
        ]);

        var rtf = RtfExporter.Instance.Export(doc);

        Assert.Contains(@"\clvmgf", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\clvmrg", rtf, StringComparison.Ordinal);

        var table = Assert.IsType<TableNode>(Assert.Single((await Trip(doc)).Blocks));

        Assert.Equal(2, table.Rows.Length);
        Assert.Equal("A", TextOf(table.Rows[0].Cells[0]));
        Assert.Equal("B", TextOf(table.Rows[0].Cells[1]));

        // RtfPipe does not read the merge, so the covered slot comes back as a cell of its own;
        // what this pins is that "C" is the second cell of its row, not the first.
        Assert.Equal(2, table.Rows[1].Cells.Length);
        Assert.DoesNotContain("A", TextOf(table.Rows[1].Cells[0]), StringComparison.Ordinal);
        Assert.Equal("C", TextOf(table.Rows[1].Cells[1]));
    }

    /// <summary>
    /// A cell spanning two columns is two columns wide. RTF says so only through the right edge
    /// its <c>\cellx</c> gives, which readers line up against the edges of the rows around it.
    /// </summary>
    [Fact]
    public async Task A_cell_spanning_two_columns_is_as_wide_as_two()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2)]),
                new TableRowNode([Cell("left"), Cell("right")]),
            ]),
        ]);

        var rtf = RtfExporter.Instance.Export(doc);
        var firstRow = rtf[rtf.IndexOf(@"\trowd", StringComparison.Ordinal)..rtf.IndexOf(@"\row", StringComparison.Ordinal)];

        Assert.Contains(@"\cellx2880", firstRow, StringComparison.Ordinal);
        Assert.DoesNotContain(@"\cellx1440", firstRow, StringComparison.Ordinal);

        var table = Assert.IsType<TableNode>(Assert.Single((await Trip(doc)).Blocks));

        Assert.Equal(2, Assert.Single(table.Rows[0].Cells).ColumnSpan);
    }

    [Fact]
    public async Task A_cells_paragraphs_stay_separate_paragraphs()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([Cell(ParagraphNode.FromText("Hello"), ParagraphNode.FromText("World"))]),
            ]),
        ]);

        var cell = Assert.Single(Assert.Single(Assert.IsType<TableNode>(Assert.Single((await Trip(doc)).Blocks)).Rows).Cells);

        Assert.Equal("Hello|World", TextOf(cell));
    }

    [Fact]
    public async Task A_list_inside_a_cell_stays_inside_the_cell()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    Cell(new ListNode(
                        [ListItemNode.FromText("one"), ListItemNode.FromText("two")],
                        ListKind.Unordered)),
                ]),
            ]),
            ParagraphNode.FromText("after"),
        ]);

        var back = (await Trip(doc));
        var table = Assert.IsType<TableNode>(back.Blocks[0]);
        var list = Assert.IsType<ListNode>(Assert.Single(Assert.Single(Assert.Single(table.Rows).Cells).Blocks));

        Assert.Equal(2, list.Items.Length);
        Assert.Equal("after", Assert.IsType<ParagraphNode>(Assert.Single(back.Blocks.Skip(1))).Content.Text);
    }

    /// <summary>
    /// A cell's paragraphs used to be written as bare content with no paragraph properties, so
    /// their alignment was never in the RTF at all. It is now. RtfPipe puts it on the row and the
    /// HTML stage, by a documented decision, does not carry a container's alignment into the
    /// paragraphs inside it — so this pins the control words, and the Phase 8 reader is
    /// re-verified for the round trip.
    /// </summary>
    [Fact]
    public void A_cell_paragraph_keeps_its_alignment()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    Cell(new ParagraphNode(InlineContent.FromText("a"), ParagraphKind.Body, TextAlign.Center)),
                    Cell(new ParagraphNode(InlineContent.FromText("b"), ParagraphKind.Body, TextAlign.Right)),
                ]),
            ]),
        ]);

        var rtf = RtfExporter.Instance.Export(doc);

        Assert.Contains(@"\pard\intbl\qc{a}\cell", rtf, StringComparison.Ordinal);
        Assert.Contains(@"\pard\intbl\qr{b}\cell", rtf, StringComparison.Ordinal);
    }

    /// <summary>
    /// RTF 1.x tables do not nest in the form every reader understands, so a table inside a cell
    /// is flattened into the cell's paragraphs, row by row — the loss is stated in the exporter's
    /// remarks. Before, its rows were written inline and broke the outer table apart.
    /// </summary>
    [Fact]
    public async Task A_table_inside_a_cell_is_flattened_into_the_cell()
    {
        var inner = new TableNode([
            new TableRowNode([Cell("x"), Cell("y")]),
            new TableRowNode([Cell("z")]),
        ]);
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([Cell(ParagraphNode.FromText("before"), inner), Cell("next")])]),
        ]);

        var table = Assert.IsType<TableNode>(Assert.Single((await Trip(doc)).Blocks));
        var row = Assert.Single(table.Rows);

        Assert.Equal(2, row.Cells.Length);
        Assert.Equal("before|x|y|z", TextOf(row.Cells[0]));
        Assert.Equal("next", TextOf(row.Cells[1]));
    }

    /// <summary>
    /// F-56: two tables written back to back are one table to a reader — rows run on. A paragraph
    /// between them is the separator every RTF writer uses, Word included.
    /// </summary>
    [Fact]
    public async Task Two_adjacent_tables_stay_two_tables()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([Cell("a")]), new TableRowNode([Cell("b")])]),
            new TableNode([new TableRowNode([Cell("c")])]),
        ]);

        var tables = (await Trip(doc)).Blocks.OfType<TableNode>().ToArray();

        Assert.Equal(2, tables.Length);
        Assert.Equal(2, tables[0].Rows.Length);
        Assert.Equal("c", TextOf(Assert.Single(Assert.Single(tables[1].Rows).Cells)));
    }

    [Fact]
    public async Task A_header_cell_paragraph_is_still_written_bold()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([new TableCellNode([ParagraphNode.FromText("H")], isHeader: true)]),
                new TableRowNode([Cell("b")]),
            ]),
        ]);

        var table = Assert.IsType<TableNode>(Assert.Single((await Trip(doc)).Blocks));
        var header = Assert.IsType<ParagraphNode>(Assert.Single(table.Rows[0].Cells).Blocks[0]);

        Assert.True(header.Content.MarkAt(0).Has(TextStyle.Bold));
        Assert.False(Assert.IsType<ParagraphNode>(Assert.Single(table.Rows[1].Cells).Blocks[0]).Content.MarkAt(0).Has(TextStyle.Bold));
    }
}
