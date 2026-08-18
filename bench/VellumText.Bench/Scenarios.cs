using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using VellumText.Avalonia;
using VellumText.Interop.Rtf;

namespace VellumText.Bench;

/// <summary>The measured scenarios.</summary>
/// <remarks>
/// Each one exists because a review finding claimed something about it. The scenario is the
/// claim's oracle: if the finding is real the shape shows it, and if a fix works the shape
/// changes. Scenarios are deliberately small and literal rather than shared behind helpers,
/// because a benchmark that quietly measures a different thing than its name says is worse than
/// no benchmark — this project has already been caught once comparing against a baseline that
/// never entered the code under test.
/// </remarks>
internal static class Scenarios
{
    private const double Width = 800;
    private const double Height = 600;

    /// <summary>Sizes for whole-document scenarios.</summary>
    private static readonly int[] DocumentSizes = [1_000, 2_000, 4_000, 8_000, 16_000];

    /// <summary>Sizes for the per-paragraph and per-export scenarios.</summary>
    private static readonly int[] UnitSizes = [250, 500, 1_000, 2_000, 4_000];

    /// <summary>
    /// AC12's keystroke budget: inserting one character into a document of N paragraphs.
    /// </summary>
    /// <remarks>
    /// The headline scenario. <c>architecture.md</c> claims keystroke-to-frame under 16 ms at
    /// 50,000 paragraphs, which is a claim that the cost does not depend on N — so the allocation
    /// ratio here should be about 1.0. Anything near 2.0 means the edit path walks the document
    /// no matter how well the *render* path is virtualized.
    /// </remarks>
    internal static void Keystroke()
    {
        var points = DocumentSizes
            .Select(n => Measure.Run(
                () => Shown(Document(n)),
                view =>
                {
                    view.MoveTo(1, false);
                    view.InsertText("x");
                    view.Measure(new Size(Width, Height));
                    view.Arrange(new Rect(0, 0, Width, Height));
                },
                n))
            .ToList();

        Measure.Report(
            "Keystroke — insert one character",
            "paragraphs",
            points,
            "AC12 claims this is independent of document size, so the ratio should be ~1.0.");
    }

    /// <summary>A caret move that changes nothing about the document.</summary>
    /// <remarks>
    /// A selection change should cost a render pass, not a measure pass. Held-down arrow keys
    /// repeat about thirty times a second, so this is the cheapest operation an editor has and
    /// the one most likely to be noticed when it is not.
    /// </remarks>
    internal static void CaretMove()
    {
        var points = DocumentSizes
            .Select(n => Measure.Run(
                () =>
                {
                    var view = Shown(Document(n));
                    view.MoveTo(1, false);
                    return view;
                },
                view =>
                {
                    view.RaiseEvent(new KeyEventArgs
                    {
                        RoutedEvent = InputElement.KeyDownEvent,
                        Key = Key.Right,
                    });
                },
                n))
            .ToList();

        Measure.Report("Caret move — one Right arrow", "paragraphs", points);
    }

    /// <summary>First layout of a document that has never been measured.</summary>
    internal static void FirstRender()
    {
        var points = DocumentSizes
            .Select(n =>
            {
                var doc = Document(n);

                return Measure.Run(
                    () => new RichTextView { State = EditorState.Create(doc) },
                    view =>
                    {
                        var window = new Window { Content = view, Width = Width, Height = Height };

                        window.Show();
                        window.Measure(new Size(Width, Height));
                        window.Arrange(new Rect(0, 0, Width, Height));
                    },
                    n,
                    rounds: 5,
                    warmup: 2);
            })
            .ToList();

        Measure.Report(
            "First render",
            "paragraphs",
            points,
            "AC12 budget is 500 ms at 50,000 paragraphs.");
    }

    /// <summary>Laying out one paragraph carrying many alternating mark spans.</summary>
    /// <remarks>
    /// <para>
    /// Targets <c>InlineTextSource.RunEnd</c>, which linear-scans a sorted span list once per
    /// run request while there are O(spans) runs. The binary search that would fix it already
    /// exists in the same file, for the embed placeholders.
    /// </para>
    /// <para>
    /// The whole layout happens inside the measured region, and that is not incidental. An
    /// earlier version of this scenario laid the view out during setup and then re-measured it,
    /// which Avalonia satisfies from cache: it reported 464 bytes flat at every size and would
    /// have declared the defect absent. The setup must therefore stop short of any layout.
    /// </para>
    /// </remarks>
    internal static void MarkSpans()
    {
        var points = UnitSizes
            .Select(n =>
            {
                var doc = new DocumentNode([AlternatingMarks(n)]);

                return Measure.Run(
                    () => new Window
                    {
                        Content = new RichTextView { State = EditorState.Create(doc) },
                        Width = Width,
                        Height = Height,
                    },
                    window =>
                    {
                        window.Show();
                        window.Measure(new Size(Width, Height));
                        window.Arrange(new Rect(0, 0, Width, Height));
                    },
                    n,
                    rounds: 7,
                    warmup: 2);
            })
            .ToList();

        Measure.Report(
            "Layout — one paragraph, N alternating mark spans",
            "spans",
            points,
            "RunEnd scans the span list per run request; expect worse than linear until fixed.");
    }

    /// <summary>Exporting a document whose runs each carry a distinct colour.</summary>
    /// <remarks>
    /// Targets <c>RtfExporter</c>'s font and colour tables, which are <c>List&lt;T&gt;</c>s
    /// searched with <c>IndexOf</c>. Both performance reviewers found this independently. The
    /// two-colour control is the point of comparison: it does the same export work with a table
    /// that never grows, so a gap between them is the table lookup and nothing else.
    /// </remarks>
    internal static void RtfColours()
    {
        var many = UnitSizes
            .Select(n => Measure.Run(
                () => DistinctColours(n),
                doc => RtfFormat.Instance.Export(doc),
                n))
            .ToList();

        Measure.Report(
            "RTF export — N distinct colours",
            "colours",
            many,
            "Compare against the two-colour control below: same runs, table that never grows.");

        var control = UnitSizes
            .Select(n => Measure.Run(
                () => TwoColours(n),
                doc => RtfFormat.Instance.Export(doc),
                n))
            .ToList();

        Measure.Report(
            "RTF export — same run count, two colours (control)",
            "runs",
            control);
    }

    /// <summary>Reporting a measured height into the block height index.</summary>
    /// <remarks>
    /// Targets <c>BlockHeightIndex.SetHeight</c>. Called for every realized block on every
    /// layout pass, so a per-call cost that grows with the document multiplies.
    /// </remarks>
    internal static void HeightIndex()
    {
        var points = DocumentSizes
            .Select(n => Measure.Run(
                () =>
                {
                    var index = new BlockHeightIndex();

                    index.Reset(n);

                    return index;
                },
                index => index.SetHeight(index.Count / 2, 37),
                n,
                rounds: 15,
                warmup: 5))
            .ToList();

        Measure.Report("BlockHeightIndex.SetHeight — one call", "blocks", points);
    }

    /// <summary>
    /// AC12 as written: 50,000 paragraphs, first render under 500 ms, keystroke under 16 ms.
    /// </summary>
    /// <remarks>
    /// One size, because this is not asking about shape — the other scenarios do that. It is
    /// asking the single question <c>architecture.md</c> §8 actually commits to, at the size it
    /// commits to, so the answer is yes or no rather than an extrapolation.
    /// </remarks>
    internal static void Ac12()
    {
        const int Paragraphs = 50_000;

        var doc = Document(Paragraphs);

        var render = Measure.Run(
            () => new RichTextView { State = EditorState.Create(doc) },
            view =>
            {
                var window = new Window { Content = view, Width = Width, Height = Height };

                window.Show();
                window.Measure(new Size(Width, Height));
                window.Arrange(new Rect(0, 0, Width, Height));
            },
            Paragraphs,
            rounds: 5,
            warmup: 2);

        var keystroke = Measure.Run(
            () => Shown(doc),
            view =>
            {
                view.MoveTo(1, false);
                view.InsertText("x");
                view.Measure(new Size(Width, Height));
                view.Arrange(new Rect(0, 0, Width, Height));
            },
            Paragraphs,
            rounds: 7,
            warmup: 2);

        // The same keystroke at the far end of the document, which is where people actually
        // type. Resolving a position walks the children to find it, so the start of a document
        // is the cheapest place in it and a benchmark that only types there flatters itself.
        var atEnd = Measure.Run(
            () => Shown(doc),
            view =>
            {
                view.MoveTo(doc.ContentSize - 2, false);
                view.InsertText("x");
                view.Measure(new Size(Width, Height));
                view.Arrange(new Rect(0, 0, Width, Height));
            },
            Paragraphs,
            rounds: 7,
            warmup: 2);

        var caret = Measure.Run(
            () =>
            {
                var view = Shown(doc);

                view.MoveTo(1, false);

                return view;
            },
            view => view.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Right,
            }),
            Paragraphs,
            rounds: 7,
            warmup: 2);

        Console.WriteLine();
        Console.WriteLine("## AC12 — architecture.md §8, at 50,000 paragraphs");
        Console.WriteLine();
        Console.WriteLine($"| {"criterion",-26} | {"budget",8} | {"measured",10} | {"result",7} |");
        Console.WriteLine($"|{new string('-', 28)}|{new string('-', 10)}|{new string('-', 12)}|{new string('-', 9)}|");

        Verdict("first render", 500, render.Milliseconds);
        Verdict("keystroke to frame", 16, keystroke.Milliseconds);
        Verdict("keystroke at document end", 16, atEnd.Milliseconds);
        Verdict("caret move", 16, caret.Milliseconds);

        Console.WriteLine();
        Console.WriteLine($"   allocated: render {render.Bytes:N0} B, keystroke {keystroke.Bytes:N0} B, at end {atEnd.Bytes:N0} B, caret {caret.Bytes:N0} B");

        static void Verdict(string name, double budget, double measured) =>
            Console.WriteLine(
                $"| {name,-26} | {budget,6:F0} ms | {measured,7:F2} ms | {(measured <= budget ? "PASS" : "FAIL"),7} |");
    }

    /// <summary>A one-character model edit, with no view involved at all.</summary>
    /// <remarks>
    /// <para>
    /// Separated from the keystroke scenario because measuring them together hid where the cost
    /// was. With the block list cached, laying out after an edit allocates 32 bytes; the edit
    /// itself allocates megabytes. The remaining linearity in a keystroke is here, in Core, and
    /// not in the view.
    /// </para>
    /// <para>
    /// Measured at 16 bytes per block, which is two arrays: the one <c>SetItem</c> produces and
    /// the one the node's constructor keeps. A one-character insertion now rebuilds the
    /// document's child array once, where it used to rebuild it three times — twice to cut the
    /// document in half and once to splice it back together, all to arrive at an array differing
    /// from the original in a single place. Getting below this means a node knowing how to
    /// replace one of its children without going through a builder.
    /// </para>
    /// <para>
    /// It is measured at both ends of the document, and that is the point of the second table.
    /// The spine rebuild touches the same number of nodes wherever the edit lands, so the two
    /// tables should agree; a gap between them is position-dependent work, which is what
    /// resolving a position used to be, and which the cut-and-splice used to add on top —
    /// inserting at the end cost half again as much as inserting at the start, because the tail
    /// it had to carry across was the whole document.
    /// </para>
    /// </remarks>
    internal static void ModelEdit()
    {
        Measure.Report(
            "Model edit — insert one character at the start, no view",
            "paragraphs",
            DocumentSizes.Select(n => ModelInsert(n, atEnd: false)).ToList(),
            "About 16 bytes per block: the document's child array rebuilt once, not three times.");

        Measure.Report(
            "Model edit — insert one character at the end, no view",
            "paragraphs",
            DocumentSizes.Select(n => ModelInsert(n, atEnd: true)).ToList(),
            "Same work as the table above, in the place people actually type. Any gap is "
            + "position-dependent cost.");
    }

    /// <summary>Resolving a position, which every edit and every caret move begins with.</summary>
    /// <remarks>
    /// <para>
    /// The one scenario that isolates the lookup rather than an operation containing it. Three
    /// tables, because the trade this makes is only visible across all three: a lookup near the
    /// start of a document was always cheap and must stay cheap; a lookup near the end was linear
    /// in the document; and the index that fixes the second is paid for once per document and
    /// then amortized over every lookup after it.
    /// </para>
    /// <para>
    /// The cold table is deliberately the unflattering one — one lookup into a document that
    /// nothing has touched, which is the case where an index can only cost and never save.
    /// </para>
    /// </remarks>
    internal static void ResolvePosition()
    {
        var start = DocumentSizes
            .Select(n => Measure.Run(
                () => Document(n),
                doc =>
                {
                    for (var i = 0; i < Lookups; i++)
                    {
                        doc.Resolve(1);
                    }
                },
                n,
                rounds: 9,
                warmup: 3))
            .ToList();

        Measure.Report(
            $"Resolve — {Lookups} lookups at the start of the document",
            "paragraphs",
            start,
            "The cheapest position there is: the walk stops at the first block. Must stay flat.");

        var end = DocumentSizes
            .Select(n => Measure.Run(
                () => Document(n),
                doc =>
                {
                    for (var i = 0; i < Lookups; i++)
                    {
                        doc.Resolve(doc.ContentSize - 2);
                    }
                },
                n,
                rounds: 9,
                warmup: 3))
            .ToList();

        Measure.Report(
            $"Resolve — {Lookups} lookups at the end of the document",
            "paragraphs",
            end,
            "Where writing happens. This is the line AC12's keystroke budget failed on.");

        var cold = DocumentSizes
            .Select(n => Measure.Run(
                () => Document(n),
                doc => doc.Resolve(doc.ContentSize - 2),
                n,
                rounds: 9,
                warmup: 3))
            .ToList();

        Measure.Report(
            "Resolve — a single lookup at the end of an untouched document",
            "paragraphs",
            cold,
            "One lookup per document version, which no index can improve. Linear here is "
            + "expected; the question is whether it got worse.");
    }

    /// <summary>How many lookups the resolve scenario makes per measured round.</summary>
    /// <remarks>
    /// One is too few to time reliably at small sizes, and a keystroke resolves a position more
    /// than a dozen times anyway — between the caret snap, the step, and the selection mapping.
    /// </remarks>
    private const int Lookups = 64;

    private static Point ModelInsert(int paragraphs, bool atEnd)
    {
        var doc = Document(paragraphs);
        var state = EditorState.Create(doc);
        var at = atEnd ? doc.ContentSize - 2 : 1;

        return Measure.Run(
            () => state,
            s =>
            {
                var transaction = s.Transaction();

                transaction.InsertText(at, "x");
                s.Apply(transaction);
            },
            paragraphs,
            rounds: 7,
            warmup: 2);
    }

    private static DocumentNode Document(int paragraphs) =>
        new(Enumerable
            .Range(0, paragraphs)
            .Select(i => (BlockNode)ParagraphNode.FromText(
                $"Paragraph {i} with enough words in it to need shaping and to wrap once."))
            .ToArray());

    private static ParagraphNode AlternatingMarks(int spans)
    {
        var content = InlineContent.FromText(string.Concat(Enumerable.Repeat("word ", spans)));

        for (var i = 0; i < spans; i += 2)
        {
            var start = i * 5;

            if (start + 4 <= content.Length)
            {
                content = content.ApplyMarks(start, 4, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold);
            }
        }

        return new ParagraphNode(content);
    }

    private static DocumentNode DistinctColours(int count) =>
        new(Enumerable
            .Range(0, count)
            .Select(i =>
            {
                var content = InlineContent.FromText($"run {i} ");

                return (BlockNode)new ParagraphNode(content.ApplyMarks(
                    0,
                    content.Length,
                    new MarkSet { Foreground = new Rgba((byte)(i % 251), (byte)(i / 251 % 251), (byte)(i % 97), 255) },
                    MarkFields.Foreground));
            })
            .ToArray());

    private static DocumentNode TwoColours(int count) =>
        new(Enumerable
            .Range(0, count)
            .Select(i =>
            {
                var content = InlineContent.FromText($"run {i} ");

                return (BlockNode)new ParagraphNode(content.ApplyMarks(
                    0,
                    content.Length,
                    new MarkSet { Foreground = i % 2 == 0 ? new Rgba(255, 0, 0, 255) : new Rgba(0, 0, 255, 255) },
                    MarkFields.Foreground));
            })
            .ToArray());

    private static RichTextView Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        return view;
    }
}
