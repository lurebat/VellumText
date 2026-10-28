using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using VellumText.Avalonia;

namespace VellumText.Bench;

/// <summary>
/// Edit paths at the 1.0 sizes (50k paragraphs, 10k-row tables, 100k-character paragraphs),
/// measured at true doublings for the table-edit scaling research
/// (<c>docs/plans/2026-10-28-table-edit-scaling-research.md</c>).
/// </summary>
/// <remarks>
/// Allocation is the trustworthy column on a shared machine; time is indicative.
/// </remarks>
internal static class ResearchScenarios
{
    private const double Width = 800;
    private const double Height = 600;
    private static readonly int[] Rows = [2_500, 5_000, 10_000, 20_000];
    private static readonly int[] Paragraphs = [6_250, 12_500, 25_000, 50_000];

    private static RichTextView Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        Layout(window);

        return view;
    }

    private static void Layout(Visual v)
    {
        if (TopLevel.GetTopLevel(v) is Layoutable l)
        {
            l.Measure(new Size(Width, Height));
            l.Arrange(new Rect(0, 0, Width, Height));
        }
    }

    private static string CellText(int r, int c) => $"Row {r} column {c} value {r * c}";

    private static DocumentNode Table(int rows) =>
        new([
            ParagraphNode.FromText("Before the table."),
            new TableNode(Enumerable.Range(0, rows).Select(r =>
                new TableRowNode(Enumerable.Range(0, 5).Select(c => TableCellNode.FromText(CellText(r, c)))))),
            ParagraphNode.FromText("After the table."),
        ]);

    /// <summary>The position just inside the first character of row <paramref name="row"/>, column 0.</summary>
    private static int CellStart(int row)
    {
        var position = 19 + 1;

        for (var r = 0; r < row; r++)
        {
            position += 2 + Enumerable.Range(0, 5).Sum(c => CellText(r, c).Length + 4);
        }

        return position + 3 + 1;
    }

    private static DocumentNode Plain(int paragraphs) =>
        new([.. Enumerable.Range(0, paragraphs).Select(i => (BlockNode)ParagraphNode.FromText($"Paragraph {i} with enough words in it to need shaping and to wrap once."))]);

    private static DocumentNode List(int items) =>
        new([new ListNode([.. Enumerable.Range(0, items).Select(i => ListItemNode.FromText($"Item {i} with enough words in it."))], ListKind.Unordered)]);

    // ------------------------------------------------------------------ r-table

    internal static void Table()
    {
        var model = Rows.Select(r =>
        {
            var state = EditorState.Create(Table(r));

            return Measure.Run(() => state, s =>
            {
                var tr = s.Transaction();
                tr.InsertText(24, "x");
                s.Apply(tr);
            }, r, rounds: 5, warmup: 2);
        }).ToList();

        Measure.Report("RESEARCH Cell keystroke, model only (transaction + Apply), R x 5 table", "rows", model, "Budget 16 ms.");

        var validate = Rows.Select(r =>
        {
            var before = Table(r);
            var tr = EditorState.Create(before).Transaction();
            tr.InsertText(24, "x");
            var after = tr.Doc;

            return Measure.Run(() => (after, before), p => DocumentSchema.Validate(p.after, p.before), r, rounds: 7, warmup: 2);
        }).ToList();

        Measure.Report("RESEARCH DocumentSchema.Validate(after, before) for that keystroke", "rows", validate);

        var place = Rows.Select(r =>
        {
            var table = (TableNode)Table(r).Blocks[1];

            return Measure.Run(() => table, t => TableGeometry.Place(t, out _), r, rounds: 7, warmup: 2);
        }).ToList();

        Measure.Report("RESEARCH TableGeometry.Place alone", "rows", place);

        var view = Rows.Select(r => Measure.Run(
            () =>
            {
                var v = Shown(Table(r));
                v.MoveTo(24, false);
                Layout(v);
                return v;
            },
            v =>
            {
                v.InsertText("x");
                Layout(v);
            },
            r,
            rounds: 3,
            warmup: 1)).ToList();

        Measure.Report("RESEARCH Cell keystroke through the view", "rows", view, "Budget 16 ms.");
    }

    // ------------------------------------------------------------------ r-tablestruct

    internal static void TableStructure()
    {
        foreach (var (name, op) in new (string, Func<RichTextView, bool>)[]
        {
            ("insert row below", v => v.InsertRowBelow()),
            ("delete row", v => v.DeleteRow()),
            ("insert column right", v => v.InsertColumnRight()),
        })
        {
            var apply = Rows.Select(r => Measure.Run(
                () =>
                {
                    var v = Shown(Table(r));
                    v.MoveTo(CellStart(r / 2), false);
                    Layout(v);
                    return v;
                },
                v =>
                {
                    op(v);
                    Layout(v);
                },
                r,
                rounds: 3,
                warmup: 1)).ToList();

            Measure.Report($"RESEARCH {name} in the middle of an R x 5 table, through the view", "rows", apply);

            var undo = Rows.Select(r => Measure.Run(
                () =>
                {
                    var v = Shown(Table(r));
                    v.MoveTo(CellStart(r / 2), false);
                    Layout(v);
                    op(v);
                    Layout(v);
                    return v;
                },
                v =>
                {
                    v.Undo();
                    Layout(v);
                },
                r,
                rounds: 3,
                warmup: 1)).ToList();

            Measure.Report($"RESEARCH undo of {name}", "rows", undo);
        }
    }

    // ------------------------------------------------------------------ r-paths

    internal static void Paths()
    {
        Report("keystroke at the end of an N-item list", "items", Paragraphs, n => List(n), (v, n) => v.MoveTo(v.State.Doc.ContentSize - 3, false), v => v.InsertText("x"));
        Report("Tab (indent) on the middle item of an N-item list", "items", Paragraphs, n => List(n), (v, n) => v.MoveTo(v.State.Doc.ContentSize / 2, false), v => v.Indent());
        Report("select all + bulleted list on N paragraphs", "paragraphs", Paragraphs, n => Plain(n), (v, _) => v.SelectAll(), v => v.ToggleList(ListKind.Unordered));
        Report("undo of select all + bulleted list", "paragraphs", Paragraphs, n => Plain(n), (v, _) => { v.SelectAll(); v.ToggleList(ListKind.Unordered); }, v => v.Undo());
    }

    /// <summary>Paste into large documents and edits of one long paragraph.</summary>
    internal static void PasteAndLong()
    {
        var hundred = Plain(100);

        Report("paste 100 paragraphs into the middle", "paragraphs", Paragraphs, n => Plain(n), (v, _) => v.MoveTo(v.State.Doc.ContentSize / 2, false), v => v.PasteImport(new ImportResult(hundred)));
        Report("paste a copy of the whole document into its middle", "paragraphs", Paragraphs, n => Plain(n), (v, _) => v.MoveTo(v.State.Doc.ContentSize / 2, false), v => v.PasteImport(new ImportResult(v.State.Doc)));

        int[] lengths = [12_500, 25_000, 50_000, 100_000];

        DocumentNode LongParagraph(int length)
        {
            var text = string.Concat(Enumerable.Range(0, length / 6 + 1).Select(i => "word" + (i % 10) + " "))[..length];
            return new DocumentNode([new ParagraphNode(InlineContent.FromText(text))]);
        }

        Report("select all + bold in one N-character paragraph", "chars", lengths, LongParagraph, (v, _) => v.SelectAll(), v => v.ToggleStyle(TextStyle.Bold));
        Report("replace all \"word1\" in one N-character paragraph", "chars", lengths, LongParagraph, (v, _) => v.Find("word1"), v => v.ReplaceAll("w1"));
        Report("undo of that replace all", "chars", lengths, LongParagraph, (v, _) => { v.Find("word1"); v.ReplaceAll("w1"); }, v => v.Undo());
    }

    /// <summary>
    /// Replace-all over many paragraphs, split out at smaller sizes because it is quadratic and
    /// the <see cref="Paths"/> sizes took over a quarter of an hour.
    /// </summary>
    internal static void Heavy()
    {
        int[] small = [1_000, 2_000, 4_000, 8_000];

        Report("replace all \"enough\" (1 match/paragraph)", "paragraphs", small, n => Plain(n), (v, _) => v.Find("enough"), v => v.ReplaceAll("plenty"), rounds: 1);
        Report("undo of that replace all", "paragraphs", small, n => Plain(n), (v, _) => { v.Find("enough"); v.ReplaceAll("plenty"); }, v => v.Undo(), rounds: 1);
    }

    private static void Report(
        string name,
        string unit,
        int[] sizes,
        Func<int, DocumentNode> doc,
        Action<RichTextView, int> prepare,
        Action<RichTextView> act,
        int rounds = 3)
    {
        var points = sizes.Select(n => Measure.Run(
            () =>
            {
                var v = Shown(doc(n));
                prepare(v, n);
                Layout(v);
                return v;
            },
            v =>
            {
                act(v);
                Layout(v);
            },
            n,
            rounds: rounds,
            warmup: 1)).ToList();

        Measure.Report("RESEARCH " + name, unit, points);
    }
}
