using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// Tables written from the grid rather than from each row's cell array (F-23, audit of 0282015).
/// </summary>
/// <remarks>
/// <para>
/// Each test reads the export back through the real import path — the RTF reader, then the HTML
/// importer — because what matters about interchange is what a recipient makes of it. Where the
/// reader cannot read a construct at all, the test pins the control words instead and says so.
/// (Written against RtfPipe; re-verified when OfficeIMO replaced it on 2026-10-27, which reads both
/// merges.)
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

        // The merge comes back as a merge (F-58: RtfPipe read it as two cells), so the row below
        // holds only "C", and the grid places it under "B".
        Assert.Equal(2, table.Rows[0].Cells[0].RowSpan);
        var below = Assert.Single(table.Rows[1].Cells);
        Assert.Equal("C", TextOf(below));
        Assert.Equal(1, TableGeometry.Place(table, out _)[^1].Left);
    }

    /// <summary>
    /// A cell spanning two columns is written as Word writes a merge: two cell definitions, the
    /// first <c>\clmgf</c> and the second <c>\clmrg</c>, together exactly as wide as two.
    /// </summary>
    /// <remarks>
    /// Until 2026-10-27 the span was only the width of one wide <c>\cellx</c>, because RtfPipe
    /// misread <c>\clmgf</c>; the reader that replaced it reads the merge and ignores the width.
    /// </remarks>
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

        Assert.Contains(@"\clmgf", firstRow, StringComparison.Ordinal);
        Assert.Contains(@"\clmrg", firstRow, StringComparison.Ordinal);
        Assert.Contains(@"\cellx1440", firstRow, StringComparison.Ordinal);
        Assert.Contains(@"\cellx2880", firstRow, StringComparison.Ordinal);

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
    /// their alignment was never in the RTF at all. It is now. RtfPipe put it on the row, where the
    /// HTML stage by a documented decision does not carry it into the paragraphs, so this pins the
    /// control words; the reader that replaced it brings it back
    /// (<c>RtfReaderRoundTripTests.A_cells_alignment_comes_back</c>).
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
