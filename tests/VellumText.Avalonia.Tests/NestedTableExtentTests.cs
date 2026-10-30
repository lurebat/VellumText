using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The scroll extent of a document with tables nested in cells stays within a small multiple of
/// what a fully laid-out copy measures, through jumps and resizes, at every depth.
/// </summary>
/// <remarks>
/// A row holding a table in a cell is only partly laid out; recorded at a height that includes
/// that table's estimate of its rows nobody measured, it taught its own table's estimate to price
/// every other row like it, and the table holding that one did the same, so the estimate grew as
/// the product of the row counts along the path: 3.46e10 DIP against 5.8e6 for a random document
/// after a resize (rubber-duck follow-up to finding 10.4). The ledger the view keeps — the extent
/// is the sum of the heights it records — is self-consistent either way, which is why this is
/// compared with the copy.
/// </remarks>
public class NestedTableExtentTests
{
    private const double Width = 500;
    private const double Height = 400;

    /// <summary>
    /// Long tables nested one, two and three deep, each in a row of a short table of plain rows:
    /// after a jump to the innermost one and resizes, the extent is within a small multiple of the
    /// truth at every depth.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TheExtentOfNestedLongTablesStaysNearTheTruthThroughAJumpAndResizes(int depth)
    {
        BlockNode content = NestedTableVirtualizationTests.Irregular(120, "in");

        for (var d = 0; d < depth; d++)
        {
            content = new TableNode(Enumerable.Range(0, 8).Select(r => new TableRowNode(r == 4
                ? [new TableCellNode([content]), TableCellNode.FromText($"side {d} {r}")]
                : [TableCellNode.FromText($"outer {d} {r}"), TableCellNode.FromText($"side {d} {r}")])));
        }

        var doc = new DocumentNode([content, ParagraphNode.FromText("After the tables.")]);
        var (window, view) = Shown(doc);
        var target = NestedTableVirtualizationTests.TextAt(doc, "inr60c0");

        for (var attempt = 0; attempt < 4; attempt++)
        {
            view.Offset = new Vector(0, view.Offset.Y + GeometryTransactionTests.ScreenTop(view, target) - 2);
            Layout(window);
        }

        AssertNearTheTruth(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc, window.Width), Bound, "after the jump");

        foreach (var width in new[] { 360.0, 620, Width })
        {
            window.Width = width;
            Layout(window);

            AssertNearTheTruth(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc, window.Width), Bound, $"at width {width}");
        }
    }

    /// <summary>
    /// Twenty rows, each holding a 300-row table: the rows nobody has measured are priced from what
    /// is known of the rows that have been — at first one table's top screenful and, for each of
    /// its rows not yet measured, at least the padding a row has; once that table has been scrolled
    /// through, all of it — so the table's height is a fair share of the truth, and grows to more
    /// of it as more is known.
    /// </summary>
    /// <remarks>
    /// Compared table to table: the paragraph after the table is priced by the presenter from the
    /// one block it has measured, the table, which doubles the extent here at any estimate.
    /// </remarks>
    [AvaloniaFact]
    public void RowsEachHoldingALongTableArePricedFromWhatIsKnownOfThem()
    {
        var doc = new DocumentNode([
            new TableNode(Enumerable.Range(0, 20).Select(r => new TableRowNode([
                new TableCellNode([NestedTableVirtualizationTests.Uniform(300, tag: $"t{r}")]),
                TableCellNode.FromText($"side {r}"),
            ]))),
            ParagraphNode.FromText("After the tables."),
        ]);
        var (window, view) = Shown(doc);
        var truth = GeometryTransactionTests.FullyLaidOut(doc, Width).Blocks[0].Size.Height;
        var table = view.Blocks[0];

        // A row of the inner table is about forty pixels; the padding alone is twelve.
        var first = table.Size.Height / truth;

        Assert.True(first >= 0.25, $"laid out first, the table is {first:F2} of its true height");

        var last = NestedTableVirtualizationTests.TextAt(doc, "t0 r299c0");

        while (GeometryTransactionTests.ScreenTop(view, last) > Height - 40)
        {
            view.Offset += new Vector(0, Height);
            Layout(window);
        }

        var after = table.Size.Height / truth;

        Assert.True(after >= 0.5 && after <= 1.5, $"with the first row's table scrolled through, the table is {after:F2} of its true height");
    }
    /// <summary>
    /// The random document of fuzz seed 10056, and the jumps and the resize that made its extent
    /// 6,000 times the truth.
    /// </summary>
    [AvaloniaFact]
    public void TheExtentOfARandomDocumentOfNestedTablesStaysNearTheTruthThroughJumpsAndAResize()
    {
        var doc = new NestedTableFuzzTests.Generator(new Random(10_056)).Document();
        var (window, view) = Shown(doc);

        foreach (var y in new[] { 80_795.0, 125_628 })
        {
            view.Offset = new Vector(0, y);
            Layout(window);

            AssertNearTheTruth(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc, window.Width), Bound, $"after a jump to {y}");
        }

        window.Width = 360;
        Layout(window);

        AssertNearTheTruth(view, GeometryTransactionTests.FullyLaidOut(view.State.Doc, window.Width), Bound, "after a resize to 360");
    }

    /// <summary>
    /// The extent is at most <paramref name="over"/> times the height of a fully laid-out copy at
    /// the same width, and at least a <see cref="Under"/>th of it.
    /// </summary>
    internal static void AssertNearTheTruth(RichTextView view, RichTextView twin, double over, string what)
    {
        var truth = twin.DesiredSize.Height;
        var ratio = view.Extent.Height / truth;

        Assert.True(
            ratio <= over && ratio >= 1 / Under,
            $"{what}: the extent is {view.Extent.Height:F1}, {ratio:F4} times the {truth:F1} a fully laid-out copy measures");
    }

    /// <summary>
    /// How many times the truth the extent may be in these tests: a row on screen holding a long
    /// table in a cell is the one row its table has measured after a resize, and every row not
    /// measured is priced like it — once, at one level, as at the top of the document.
    /// </summary>
    internal const double Bound = 4;

    /// <summary>
    /// How many times the truth the extent may be in the fuzz, whose documents have a few rows
    /// holding most of the content: the most seen over its recorded seeds is 14.
    /// </summary>
    internal const double Over = 32;

    /// <summary>
    /// The least share of the truth the extent may be: content nobody has laid out is priced from
    /// what has been, and in the fuzz's documents the rows not yet seen hold most of the content,
    /// at every depth — a sixth of a percent is the least seen over its recorded seeds.
    /// </summary>
    internal const double Under = 1_000;

    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        Layout(window);

        return (window, view);
    }

    private static void Layout(Window window) => GeometryTransactionTests.Layout(window);
}