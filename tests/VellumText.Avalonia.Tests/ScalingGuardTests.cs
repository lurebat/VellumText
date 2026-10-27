using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The CI half of the performance scenarios in <c>bench/VellumText.Bench</c>: the shape of each
/// one, pinned without a clock.
/// </summary>
/// <remarks>
/// <para>
/// The audit's recurring performance defect (RP-5) was a scan over the whole document, or over
/// every match, every span or every character, hiding inside an operation that should have
/// touched a handful of them. Each was found with a benchmark run at doubling sizes, and each
/// would have been caught by the same run in CI — except that a timing in CI is noise.
/// </para>
/// <para>
/// So every row here measures something deterministic: bytes allocated on the test's own thread,
/// or a work counter the code under test keeps. Two kinds of verdict. A growth row runs the
/// operation at doubling sizes and fails when the cost grows by more than
/// <see cref="Growth.Superlinear"/> per doubling, which is the quadratic shape every finding had.
/// A budget row runs it once at AC12's 50,000 paragraphs and fails when it allocates more than the
/// stated budget, which is how a linear constant factor that breaks the frame budget shows up
/// without timing the frame. The timing itself is the bench's job, on a quiet machine.
/// </para>
/// </remarks>
public class ScalingGuardTests
{
    private const double Width = 800;
    private const double Height = 600;

    /// <summary>The document size AC12 states its budgets at.</summary>
    private const int Ac12Paragraphs = 50_000;

    /// <summary>
    /// The most a keystroke, an Enter or a join may allocate at <see cref="Ac12Paragraphs"/>.
    /// </summary>
    private const long Ac12Budget = 1_000_000;

    /// <summary>
    /// The control row: a keystroke at the end of the document is allowed to be linear in the
    /// document's size (architecture §8 item 8), and must not be worse.
    /// </summary>
    /// <remarks>
    /// Kept as a check on the harness as much as on the editor. A growth row that passed because
    /// it measured nothing would look exactly like a fixed scan, and this one has to see the
    /// document-sized array a keystroke copies to report the ratio it does.
    /// </remarks>
    [AvaloniaFact]
    public void AKeystrokeAtTheEndAllocatesAtMostLinearlyInTheDocument()
    {
        var bytes = Growth.Measure(
            [4_000, 8_000, 16_000],
            n =>
            {
                var doc = Growth.Paragraphs(n);
                var view = Shown(doc);

                view.MoveTo(doc.ContentSize - 2, false);
                Layout(view);

                return Growth.Allocated(() =>
                {
                    view.InsertText("x");
                    Layout(view);
                });
            });

        Growth.AssertAtMostLinear(bytes, "bytes per keystroke at the end of the document");
        Assert.True(bytes[^1] > bytes[0], "the keystroke allocated nothing that grows with the document");
    }

    /// <summary>
    /// F-11: undoing a format applied across the document examines each recorded span once, not
    /// once per paragraph.
    /// </summary>
    /// <remarks>
    /// The inverse of "make everything italic" records every stretch of formatting the document
    /// had, and putting it back looked at all of them for every paragraph: 3.5× per doubling,
    /// seconds at a few thousand paragraphs. The bench scenario is <c>a-undofmt</c>.
    /// </remarks>
    [Fact]
    public void UndoingAFormatAcrossTheDocumentVisitsEachRecordedSpanOnce()
    {
        int[] sizes = [500, 1_000, 2_000];
        var bound = new long[sizes.Length];
        var visits = Growth.Measure(
            sizes,
            n =>
            {
                var doc = Growth.MarkedParagraphs(n);
                var italic = new AddMarkStep(
                    0, doc.ContentSize, new MarkSet { Style = TextStyle.Italic }, MarkFields.Italic);
                var undo = Assert.IsType<RestoreMarksStep>(italic.Invert(doc));
                var formatted = italic.Apply(doc).Doc!;

                WorkCounters.RestoredSpanVisits = 0;

                var restored = undo.Apply(formatted);

                Assert.Equal(doc, restored.Doc);
                bound[Array.IndexOf(sizes, n)] = undo.Spans.Length + n;

                return WorkCounters.RestoredSpanVisits;
            });

        Growth.AssertAtMostLinear(visits, "recorded spans examined by one undo");

        // Each paragraph may look at one span past its end to know it is done, and no more.
        for (var i = 0; i < visits.Length; i++)
        {
            Assert.True(
                visits[i] <= bound[i],
                $"examined {visits[i]} spans where {bound[i]} recorded spans and paragraphs exist");
        }
    }

    /// <summary>
    /// F-12: Ctrl+Z after a burst of typing costs the same however long the burst was.
    /// </summary>
    /// <remarks>
    /// Every keystroke's inverse used to be replayed, each one a rebuild of the document's spine,
    /// so the undo grew with the number of characters typed: 151 ms for 25 characters and 2.3 s
    /// for 400 at 50,000 paragraphs, bytes exactly doubling with the burst. The bench scenario is
    /// <c>a-undo</c>.
    /// </remarks>
    [Fact]
    public void UndoingATypingBurstCostsTheSameWhateverItsLength()
    {
        var bytes = Growth.Measure(
            [25, 50, 100, 200],
            k =>
            {
                var doc = Growth.Paragraphs(2_000);
                var state = EditorState.Create(doc, TextSelection.Cursor(doc.ContentSize - 2));
                var history = History.Empty;

                for (var i = 0; i < k; i++)
                {
                    var at = state.Selection.Head;
                    var tr = state.Transaction()
                        .As(TransactionKind.Typing)
                        .At(DateTimeOffset.UnixEpoch.AddMilliseconds(100 * i))
                        .InsertText(at, "x")
                        .SetSelection(TextSelection.Cursor(at + 1));

                    history = history.Record(tr);
                    state = state.Apply(tr);
                }

                Assert.Equal(1, history.UndoDepth);

                return Growth.Allocated(() => history.Undo(state));
            });

        Growth.AssertFlat(bytes, "bytes allocated by one undo of a typing burst, by burst length");
    }

    /// <summary>
    /// F-13: with a find query open, painting a frame looks only at the matches on screen.
    /// </summary>
    /// <remarks>
    /// Every match in the document was walked on every frame — caret blink, scroll, keystroke —
    /// and each walked the realized blocks: 81 ms per frame at 50,000 paragraphs for a query of
    /// "e". The bench scenario is <c>a-findrender</c>.
    /// </remarks>
    [AvaloniaFact]
    public void PaintingFindHighlightsLooksOnlyAtTheMatchesOnScreen()
    {
        var examined = Growth.Measure(
            [2_000, 4_000, 8_000],
            n =>
            {
                var view = new RichTextView { State = EditorState.Create(Growth.Paragraphs(n)) };
                var window = new Window { Content = view, Width = Width, Height = Height };

                window.Show();
                view.Find("e");
                view.Offset = new Vector(0, view.Extent.Height / 2);
                window.Measure(new Size(Width, Height));
                window.Arrange(new Rect(0, 0, Width, Height));

                view.FindMatchesExamined = 0;

                using var frame = window.CaptureRenderedFrame();

                Assert.NotNull(frame);
                Assert.True(view.FindMatchesExamined > 0, "no match was painted");

                return view.FindMatchesExamined;
            });

        Growth.AssertFlat(examined, "matches examined to paint one frame");
    }

    /// <summary>
    /// The AC12 rows, in the one currency CI can measure: bytes allocated by a keystroke, an Enter
    /// and a Backspace that joins two paragraphs, at the end of a 50,000-paragraph document.
    /// </summary>
    /// <remarks>
    /// <para>
    /// F-14 and F-15. The view re-flattened the whole document into a fresh slot array on every
    /// change (3.2 MB), the step behind Enter and a join cut and spliced the whole block array
    /// (2.4 MB), the join looked its neighbour up in a second whole-document list (3.2 MB), and
    /// the height index was reallocated for every block added or removed (1 MB): 4.2 MB for a
    /// keystroke, 6.9 MB for Enter and 10.1 MB for a join, enough to put a gen-2 collection
    /// every few keys. The bench scenarios are <c>a-stages</c>, <c>a-sustained</c> and
    /// <c>a-structure</c>; the 16 ms frame budget itself is timed there, on a quiet machine.
    /// </para>
    /// <para>
    /// What is left is the copy of the document's block array that an immutable edit must make
    /// — linear in the document by decision (architecture §8 item 8) — and the size index the new
    /// document builds the first time a position in it is resolved.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void Ac12AKeystrokeAtTheEndStaysWithinItsAllocationBudget()
    {
        var doc = Growth.Paragraphs(Ac12Paragraphs);
        var view = Shown(doc);

        view.MoveTo(doc.ContentSize - 2, false);
        Layout(view);

        var bytes = Growth.Allocated(() =>
        {
            view.InsertText("x");
            Layout(view);
        });

        Assert.True(bytes <= Ac12Budget, $"a keystroke allocated {bytes:N0} bytes (budget {Ac12Budget:N0})");
    }

    /// <inheritdoc cref="Ac12AKeystrokeAtTheEndStaysWithinItsAllocationBudget"/>
    /// <remarks>
    /// At the start as well as the end, because the two ends of the block list are matched
    /// separately when a document changes: an edit at the end is found by the scan from the
    /// front, and one at the start only by the scan from the back.
    /// </remarks>
    [AvaloniaFact]
    public void Ac12AKeystrokeAtTheStartStaysWithinItsAllocationBudget()
    {
        var view = Shown(Growth.Paragraphs(Ac12Paragraphs));

        view.MoveTo(1, false);
        Layout(view);

        var bytes = Growth.Allocated(() =>
        {
            view.InsertText("x");
            Layout(view);
        });

        Assert.True(bytes <= Ac12Budget, $"a keystroke allocated {bytes:N0} bytes (budget {Ac12Budget:N0})");
    }

    /// <inheritdoc cref="Ac12AKeystrokeAtTheEndStaysWithinItsAllocationBudget"/>
    [AvaloniaFact]
    public void Ac12EnterNearTheEndStaysWithinItsAllocationBudget()
    {
        var doc = Growth.Paragraphs(Ac12Paragraphs);
        var view = Shown(doc);

        view.MoveTo(doc.ContentSize - 10, false);
        Layout(view);

        var bytes = Growth.Allocated(() =>
        {
            view.Split();
            Layout(view);
        });

        Assert.True(bytes <= Ac12Budget, $"Enter allocated {bytes:N0} bytes (budget {Ac12Budget:N0})");
    }

    /// <inheritdoc cref="Ac12AKeystrokeAtTheEndStaysWithinItsAllocationBudget"/>
    [AvaloniaFact]
    public void Ac12BackspaceJoiningTheLastParagraphStaysWithinItsAllocationBudget()
    {
        var doc = Growth.Paragraphs(Ac12Paragraphs);
        var view = Shown(doc);

        void JoinTheLastParagraph()
        {
            var state = view.State.Doc;

            view.MoveTo(state.Resolve(state.ContentSize - 2).Start(1), false);
            Layout(view);
            Assert.True(view.Backspace(), "the join was refused");
            Layout(view);
        }

        // Once to pay for anything the first join builds, then the measured one.
        JoinTheLastParagraph();

        var before = GC.GetAllocatedBytesForCurrentThread();

        JoinTheLastParagraph();

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(Ac12Paragraphs - 2, view.State.Doc.Blocks.Length);
        Assert.True(bytes <= Ac12Budget, $"a join allocated {bytes:N0} bytes (budget {Ac12Budget:N0})");
    }

    /// <summary>
    /// F-32: working out what formatting a selection shares looks at stretches of formatting, not
    /// characters, and stops once nothing is shared.
    /// </summary>
    /// <remarks>
    /// The toolbar reads this on every state change, and with everything selected it looked up
    /// the formatting of every selected character: 143 ms at 50,000 paragraphs, and Ctrl+B on a
    /// select-all paid it again. The bench scenario is <c>a-selectall</c>.
    /// </remarks>
    [AvaloniaFact]
    public void TheSelectionsFormattingIsReadByStretchNotByCharacter()
    {
        int[] sizes = [500, 1_000, 2_000];

        foreach (var shape in new[] { "plain", "every other word bold", "all bold", "the middle paragraph of a bold document" })
        {
            var lookups = new long[sizes.Length];
            var examined = Growth.Measure(
                sizes,
                n =>
                {
                    var doc = shape switch
                    {
                        "plain" => Growth.Paragraphs(n),
                        "every other word bold" => Growth.MarkedParagraphs(n),
                        _ => Bold(Growth.Paragraphs(n)),
                    };
                    var view = Shown(doc);

                    if (shape == "the middle paragraph of a bold document")
                    {
                        var middle = doc.Resolve(DocumentBlocks.Flatten(doc)[n / 2].Start);

                        view.MoveTo(middle.Start(1), false);
                        view.MoveTo(middle.End(1), true);
                    }
                    else
                    {
                        view.SelectAll();
                    }

                    view.MarkRunsExamined = 0;
                    WorkCounters.MarkLookups = 0;

                    var marks = view.SelectionMarks;

                    Assert.Equal(shape is "all bold" or "the middle paragraph of a bold document", marks.Has(TextStyle.Bold));
                    lookups[Array.IndexOf(sizes, n)] = WorkCounters.MarkLookups;

                    return view.MarkRunsExamined;
                });

            // Whole-document bold is the one shape that cannot stop early: every paragraph is a
            // single bold stretch, so the walk is one look at each paragraph and its stretch. The
            // others give up as soon as two stretches disagree, or never leave the paragraph.
            if (shape == "all bold")
            {
                Growth.AssertAtMostLinear(examined, $"leaves and stretches examined, {shape}");
                Assert.True(examined[^1] <= 2 * sizes[^1], $"{shape}: examined {examined[^1]}");
            }
            else
            {
                Growth.AssertFlat(examined, $"leaves and stretches examined, {shape}");
            }

            Assert.True(lookups.All(l => l <= 1), $"{shape}: {string.Join(", ", lookups)} per-character lookups");
        }

        static DocumentNode Bold(DocumentNode doc) =>
            new AddMarkStep(0, doc.ContentSize, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold)
                .Apply(doc).Doc!;
    }

    /// <summary>
    /// F-33: laying out one long paragraph does work in proportion to its length, not to its length
    /// squared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Avalonia's line wrapping copies the run list that remains once per line, so one layout of a
    /// formatted paragraph costs lines × runs, both of which grow with the paragraph: 326 ms a
    /// keystroke at 100,000 characters, 2.76× per doubling (docs/increment-9-0-profile.md). The
    /// paragraph is now laid out in chunks of bounded size, so the product is bounded per chunk.
    /// The bench scenario is <c>a-longpara</c>.
    /// </para>
    /// <para>
    /// Unformatted text was always linear, being one run; it is here so that a chunk which stopped
    /// cutting its window off — and so laid out the rest of the paragraph each time — would show.
    /// </para>
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void LayingOutALongParagraphIsLinearInItsLength(bool formatted)
    {
        var work = Growth.Measure(
            [25_000, 50_000, 100_000],
            n =>
            {
                var text = Growth.Words(n);
                var doc = new DocumentNode([new ParagraphNode(
                    formatted ? Growth.EveryOtherWordBold(text) : InlineContent.FromText(text))]);

                WorkCounters.WrappingWork = 0;

                Shown(doc);

                return WorkCounters.WrappingWork;
            });

        Growth.AssertAtMostLinear(work, $"lines × runs wrapped to lay out one long paragraph (formatted: {formatted})");
    }

    /// <summary>
    /// F-33: a keystroke in one long formatted paragraph of ordinary prose lays out a stretch of it
    /// whose size does not depend on the paragraph's length.
    /// </summary>
    /// <remarks>
    /// An edit lays out again from the chunk it lands in and stops where the lines meet the old
    /// ones, which in prose is within a line or two. Text so regular that every line holds the
    /// same number of words can shift every later line by a word, and then the whole tail is laid
    /// out again — exactly, and in linear time, which the row above pins.
    /// </remarks>
    [AvaloniaFact]
    public void AKeystrokeInALongFormattedParagraphLaysOutABoundedStretchOfIt()
    {
        var laidOut = Growth.Measure(
            [25_000, 50_000, 100_000],
            n =>
            {
                var doc = new DocumentNode([new ParagraphNode(Growth.EveryOtherWordBold(Growth.Prose(n)))]);
                var view = Shown(doc);

                view.MoveTo(1 + (n / 2), false);
                Layout(view);
                view.InsertText("x");
                Layout(view);

                WorkCounters.CharactersLaidOut = 0;

                view.InsertText("x");
                Layout(view);

                return WorkCounters.CharactersLaidOut;
            });

        // A bound rather than a ratio: what is laid out comes in whole chunks, so one keystroke
        // landing on a chunk's first line lays out two where its neighbour lays out one.
        Assert.True(
            laidOut.All(count => count <= MostLaidOutPerKeystroke),
            $"a keystroke laid out {string.Join(", ", laidOut)} characters of 25,000, 50,000 and 100,000");
    }

    /// <summary>
    /// The most of a long paragraph a keystroke in it may lay out: the chunk it lands in, the one
    /// before it when the edit is on a chunk's first line, and two more for the reflow to settle.
    /// </summary>
    private const long MostLaidOutPerKeystroke = 4 * 2_048;

    /// <summary>
    /// F-35: painting a frame of a long table draws the rows on screen, not every row.
    /// </summary>
    /// <remarks>
    /// <c>TableBlockView.Render</c> drew every cell's paragraph whatever the viewport showed — 93%
    /// of a frame, 13.3 ms at 1,000 × 5 and linear in rows (docs/increment-9-0-profile.md). Every
    /// caret blink is a frame. The bench scenario is <c>a-table</c>.
    /// </remarks>
    [AvaloniaFact]
    public void PaintingAFrameOfALongTableDrawsOnlyTheRowsOnScreen()
    {
        var drawn = Growth.Measure(
            [250, 500, 1_000],
            n =>
            {
                var view = new RichTextView { State = EditorState.Create(Growth.Table(n)) };
                var window = new Window { Content = view, Width = Width, Height = Height };

                window.Show();
                window.Measure(new Size(Width, Height));
                window.Arrange(new Rect(0, 0, Width, Height));

                WorkCounters.TableCellsRendered = 0;
                WorkCounters.TableCellsExamined = 0;

                using var frame = window.CaptureRenderedFrame();

                Assert.NotNull(frame);
                Assert.True(WorkCounters.TableCellsRendered > 0, "no cell was painted");

                return WorkCounters.TableCellsExamined;
            });

        Growth.AssertFlat(drawn, "table cells looked at to paint one frame showing the top of the table");
    }

    /// <summary>
    /// F-35: a keystroke in a table cell rebuilds the row it is in, not every row.
    /// </summary>
    /// <remarks>
    /// <c>TableBlockView.Update</c> walked every cell of the table — flattening its blocks and
    /// matching its views — on every keystroke anywhere in it: 5.35 ms at 1,000 × 5 and linear in
    /// rows. The bench scenario is <c>a-table</c>.
    /// </remarks>
    [AvaloniaFact]
    public void AKeystrokeInATableRebuildsOnlyTheRowItIsIn()
    {
        var built = Growth.Measure(
            [250, 500, 1_000],
            n =>
            {
                var view = Shown(Growth.Table(n));

                view.MoveTo(Growth.FirstCellText, false);
                Layout(view);
                view.InsertText("x");
                Layout(view);

                WorkCounters.TableCellsBuilt = 0;

                view.InsertText("x");
                Layout(view);

                return WorkCounters.TableCellsBuilt;
            });

        Growth.AssertFlat(built, "table cells rebuilt by a keystroke in the first cell");
        Assert.True(built.All(count => count <= 5), $"cells rebuilt: {string.Join(", ", built)}");
    }
    /// <summary>
    /// F-35: laying out a long table measures and builds the rows on screen, not every row.
    /// </summary>
    /// <remarks>
    /// The table view built a view for every block of every cell when it was created and measured
    /// all of them whenever it was measured: 3.4 s of a 1,000-row profile in <c>Measure</c> alone,
    /// and linear in rows (docs/increment-9-0-profile.md). Rows are now realized in bands as the
    /// viewport reaches them, with the rest priced at an estimate.
    /// </remarks>
    [AvaloniaFact]
    public void LayingOutALongTableRealizesOnlyTheRowsOnScreen()
    {
        var built = new long[3];
        var measured = Growth.Measure(
            [2_500, 5_000, 10_000],
            n =>
            {
                var doc = Growth.Table(n);

                WorkCounters.TableCellsMeasured = 0;
                WorkCounters.TableCellsBuilt = 0;

                Shown(doc);

                built[Array.IndexOf([2_500, 5_000, 10_000], n)] = WorkCounters.TableCellsBuilt;

                return WorkCounters.TableCellsMeasured;
            });

        Growth.AssertFlat(measured, "table cells measured to lay out the top of a long table");
        Growth.AssertFlat(built, "table cells built to lay out the top of a long table");
    }

    /// <summary>
    /// F-35: scrolling to the middle of a long table measures the rows it brings on screen.
    /// </summary>
    [AvaloniaFact]
    public void ScrollingALongTableRealizesOnlyTheRowsItBringsOnScreen()
    {
        var measured = Growth.Measure(
            [2_500, 5_000, 10_000],
            n =>
            {
                var view = Shown(Growth.Table(n));

                WorkCounters.TableCellsMeasured = 0;

                view.Offset = new Vector(0, view.Extent.Height / 2);
                Layout(view);

                Assert.True(WorkCounters.TableCellsMeasured > 0, "the scroll measured nothing");

                return WorkCounters.TableCellsMeasured;
            });

        Growth.AssertFlat(measured, "table cells measured to scroll to the middle of a long table");
    }

    /// <summary>
    /// The AC12 row for a table, the view's share: after a keystroke in the first cell of a
    /// 10,000-row table, bringing the view up to date measures that row again and allocates within
    /// the keystroke budget.
    /// </summary>
    /// <remarks>
    /// Only the view's share, because the model's is not the view's to fix: applying the step
    /// validates the edited table, and <c>DocumentSchema.ValidateTable</c> places every cell to do
    /// it — about 6 MB at this size, measured, linear in cells and the same before and after this
    /// change. The edit is therefore applied to the state outside the measurement and handed to
    /// the view, which reconciles and lays it out exactly as it does a typed one.
    /// </remarks>
    [AvaloniaFact]
    public void Ac12AKeystrokeInATenThousandRowTableLaysOutOneRowWithinItsBudget()
    {
        var view = Shown(Growth.Table(10_000));

        view.MoveTo(Growth.FirstCellText, false);
        Layout(view);
        Typed(view);

        WorkCounters.TableCellsMeasured = 0;

        var bytes = Typed(view);

        Assert.True(bytes <= Ac12Budget, $"laying out a keystroke allocated {bytes:N0} bytes (budget {Ac12Budget:N0})");
        Assert.InRange(WorkCounters.TableCellsMeasured, 1, 5);

        static long Typed(RichTextView view)
        {
            var state = view.State;
            var head = state.Selection.Head;
            var next = state.Apply(state.Transaction().InsertText(head, "x").SetSelection(TextSelection.Cursor(head + 1)));
            var before = GC.GetAllocatedBytesForCurrentThread();

            view.State = next;
            Layout(view);

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    /// <summary>
    /// Laying out a 10,000-row table allocates for the rows on screen and a few words of
    /// bookkeeping per row, not a laid-out view of every cell.
    /// </summary>
    [AvaloniaFact]
    public void LayingOutATenThousandRowTableAllocatesBookkeepingNotViews()
    {
        var doc = Growth.Table(10_000);
        var before = GC.GetAllocatedBytesForCurrentThread();

        Shown(doc);

        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(bytes <= FirstLayoutBudget, $"laying out the table allocated {bytes:N0} bytes (budget {FirstLayoutBudget:N0})");
    }

    /// <summary>What laying out a 10,000 × 5 table for the first time may allocate.</summary>
    /// <remarks>
    /// Measured at 7.8 MB, about 780 bytes a row, most of it placing the cells
    /// (<c>TableGeometry.Place</c>'s occupancy set) and the per-row arrays. A laid-out view of every
    /// cell, which is what it was, allocated 280 MB.
    /// </remarks>
    private const long FirstLayoutBudget = 12_000_000;
    private static RichTextView Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };

        Layout(view);

        return view;
    }

    private static void Layout(RichTextView view)
    {
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
    }
}

/// <summary>Measuring an operation across doubling sizes, deterministically.</summary>
internal static class Growth
{
    /// <summary>
    /// The largest growth per doubling a scaling row accepts: comfortably above linear (2.0) and
    /// below quadratic (4.0), the shape every audit finding had.
    /// </summary>
    public const double Superlinear = 2.5;

    /// <summary>
    /// The largest growth per doubling an operation that should not depend on the size may show.
    /// The bench's own verdict calls anything under 1.35 flat.
    /// </summary>
    public const double Flat = 1.35;

    /// <summary>A plain document of <paramref name="n"/> paragraphs, the bench's shape.</summary>
    public static DocumentNode Paragraphs(int n) =>
        new(Enumerable.Range(0, n)
            .Select(i => (BlockNode)ParagraphNode.FromText(
                $"Paragraph {i} with enough words in it to need shaping and to wrap once."))
            .ToArray());

    /// <summary>
    /// The same paragraphs with every other word bold, so that each one holds several formatting
    /// spans — the shape that makes a per-span scan visible.
    /// </summary>
    public static DocumentNode MarkedParagraphs(int n) =>
        new(Enumerable.Range(0, n)
            .Select(i => (BlockNode)new ParagraphNode(EveryOtherWordBold(
                $"Paragraph {i} with enough words in it to need shaping and to wrap once.")))
            .ToArray());

    /// <summary>
    /// <paramref name="length"/> characters of short numbered words, the bench's long-paragraph
    /// text.
    /// </summary>
    public static string Words(int length)
    {
        var text = new System.Text.StringBuilder(length + 16);
        var word = 0;

        while (text.Length < length)
        {
            text.Append("word").Append(word++ % 10).Append(' ');
        }

        return text.ToString(0, length);
    }

    /// <summary>
    /// <paramref name="length"/> characters of words of irregular length, as prose has: the lines
    /// of a paragraph do not all hold the same number of words.
    /// </summary>
    public static string Prose(int length)
    {
        var random = new Random(20261027);
        var text = new System.Text.StringBuilder(length + 16);

        while (text.Length < length)
        {
            var letters = random.Next(1, 12);

            for (var i = 0; i < letters; i++)
            {
                text.Append((char)('a' + random.Next(26)));
            }

            text.Append(random.Next(9) == 0 ? ", " : " ");
        }

        return text.ToString(0, length);
    }

    /// <summary>
    /// A paragraph, a table of <paramref name="rows"/> × <paramref name="columns"/> one-line
    /// cells, and a paragraph: the bench's <c>a-table</c> document.
    /// </summary>
    public static DocumentNode Table(int rows, int columns = 5) =>
        new([
            ParagraphNode.FromText("Before the table."),
            new TableNode(Enumerable.Range(0, rows).Select(r =>
                new TableRowNode(Enumerable.Range(0, columns).Select(c =>
                    TableCellNode.FromText($"Row {r} column {c} value {r * c}"))))),
            ParagraphNode.FromText("After the table."),
        ]);

    /// <summary>
    /// The document position just inside the text of <see cref="Table"/>'s first cell: past the
    /// paragraph before it (17 characters and its two boundaries) and the table, row, cell and
    /// paragraph openings, and one character in.
    /// </summary>
    public const int FirstCellText = 24;
    /// <summary>The text with every other word bold.</summary>
    public static InlineContent EveryOtherWordBold(string text)
    {
        var spans = new List<ValueSpan<MarkSet>>();
        var bold = new MarkSet { Style = TextStyle.Bold };
        var word = 0;
        var start = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == ' ')
            {
                if (word % 2 == 1)
                {
                    spans.Add(new ValueSpan<MarkSet>(start, i - start, bold));
                }

                word++;
                start = i + 1;
            }
        }

        return InlineContent.Create(text, spans, []);
    }

    /// <summary>Bytes allocated on this thread while <paramref name="action"/> runs.</summary>
    /// <remarks>
    /// The action is run once first and discarded, so a first call's one-off allocations — a
    /// lazily built cache, a static constructor — are not charged to the size being measured.
    /// That needs an action that can run twice, which every caller's can.
    /// </remarks>
    public static long Allocated(Action action)
    {
        action();

        var before = GC.GetAllocatedBytesForCurrentThread();

        action();

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    /// <summary>Runs a measurement at each size, in order.</summary>
    public static long[] Measure(int[] sizes, Func<int, long> measure) =>
        sizes.Select(measure).ToArray();

    /// <summary>Fails if any doubling grew the measurement by more than <see cref="Superlinear"/>.</summary>
    public static void AssertAtMostLinear(long[] measured, string what) =>
        AssertGrowth(measured, Superlinear, what);

    /// <summary>
    /// Fails if any doubling grew the measurement by more than <see cref="Flat"/>: for an
    /// operation whose cost should not depend on the size at all.
    /// </summary>
    public static void AssertFlat(long[] measured, string what) =>
        AssertGrowth(measured, Flat, what);

    private static void AssertGrowth(long[] measured, double limit, string what)
    {
        for (var i = 1; i < measured.Length; i++)
        {
            // A measurement of zero at the smaller size cannot be divided by, and anything growing
            // from nothing is growth the ratio would call infinite. Treat it as one unit.
            var ratio = measured[i] / (double)Math.Max(1, measured[i - 1]);

            Assert.True(
                ratio <= limit,
                $"{what} grew {ratio:F2}x per doubling (limit {limit}x): {string.Join(", ", measured)}");
        }
    }
}
