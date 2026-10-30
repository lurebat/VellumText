using System.Diagnostics;

using VellumText;

namespace VellumText.Tests.Core;

/// <summary>
/// Shortening a document to a preview, and the two ways a naive cut goes wrong.
/// </summary>
public class DocumentPreviewTests
{
    [Fact]
    public void ADocumentShorterThanTheBudgetComesBackUntouched()
    {
        var doc = DocumentNode.FromParagraphs("short");

        Assert.Same(doc, DocumentPreview.Truncate(doc, 100));
    }

    [Fact]
    public void ALongParagraphIsCutToTheBudget()
    {
        var doc = DocumentNode.FromParagraphs(new string('a', 500));

        var preview = DocumentPreview.Truncate(doc, 80);

        Assert.Equal(80, DocumentText.Of(preview).Length);
    }

    [Fact]
    public void WholeBlocksAreKeptUntilTheBudgetRunsOut()
    {
        var doc = DocumentNode.FromParagraphs("one", "two", "three", "four");

        var preview = DocumentPreview.Truncate(doc, 7);

        // "one" and "two" fit in seven; "three" does not, so one character of it is kept.
        Assert.Equal(3, preview.Blocks.Length);
        Assert.Equal("one", ((ParagraphNode)preview.Blocks[0]).Content.Text);
        Assert.Equal("two", ((ParagraphNode)preview.Blocks[1]).Content.Text);
        Assert.Equal("t", ((ParagraphNode)preview.Blocks[2]).Content.Text);
    }

    [Fact]
    public void FormattingSurvivesTheCut()
    {
        // A preview that dropped formatting would misrepresent the row it stands for.
        var doc = new DocumentNode([
            new ParagraphNode(
                InlineContent.Create(
                    "bold and plain",
                    [new ValueSpan<MarkSet>(0, 4, new MarkSet { Style = TextStyle.Bold })],
                    []),
                ParagraphKind.Heading2),
        ]);

        var preview = DocumentPreview.Truncate(doc, 8);
        var paragraph = Assert.IsType<ParagraphNode>(preview.Blocks[0]);

        Assert.Equal(ParagraphKind.Heading2, paragraph.Kind);
        Assert.Equal(TextStyle.Bold, paragraph.Content.MarkAt(0).Style);
    }

    [Fact]
    public void ASurrogatePairIsNeverHalved()
    {
        // Cutting at 1 would leave a lone high surrogate, which renders as a replacement glyph.
        var doc = DocumentNode.FromParagraphs("😀tail");

        var preview = DocumentPreview.Truncate(doc, 1);

        Assert.Equal(string.Empty, DocumentText.Of(preview).Trim());
    }

    [Fact]
    public void AZwjSequenceIsKeptWholeOrDroppedWhole()
    {
        // The family emoji is one grapheme built of seven code units. Cutting inside it yields
        // several perfectly valid characters that render as the wrong thing entirely.
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";

        var doc = DocumentNode.FromParagraphs(family + "tail");

        for (var budget = 0; budget < family.Length; budget++)
        {
            Assert.Equal(string.Empty, DocumentText.Of(DocumentPreview.Truncate(doc, budget)).Trim());
        }

        Assert.Equal(family, DocumentText.Of(DocumentPreview.Truncate(doc, family.Length)).Trim());
    }

    [Fact]
    public void ACombiningMarkStaysWithTheLetterItModifies()
    {
        var doc = DocumentNode.FromParagraphs("e\u0301tail");

        Assert.Equal(string.Empty, DocumentText.Of(DocumentPreview.Truncate(doc, 1)).Trim());
        Assert.Equal("e\u0301", DocumentText.Of(DocumentPreview.Truncate(doc, 2)).Trim());
    }

    [Fact]
    public void APreviewNeverContainsHalfATable()
    {
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("a long cell that will not fit")]),
            ]),
        ]);

        var doc = new DocumentNode([ParagraphNode.FromText("intro"), table]);

        var preview = DocumentPreview.Truncate(doc, 10);

        Assert.Equal("intro", ((ParagraphNode)Assert.Single(preview.Blocks)).Content.Text);
    }

    [Fact]
    public void ATableThatFitsIsKeptWhole()
    {
        var table = new TableNode([
            new TableRowNode([new TableCellNode([ParagraphNode.FromText("ab")])]),
        ]);

        var doc = new DocumentNode([ParagraphNode.FromText("intro"), table]);

        var preview = DocumentPreview.Truncate(doc, 500);

        Assert.Same(doc, preview);
    }

    [Fact]
    public void AnEmptyBudgetStillYieldsAUsableDocument()
    {
        // A document with no blocks at all is not one every consumer will accept.
        var preview = DocumentPreview.Truncate(DocumentNode.FromParagraphs("text"), 0);

        Assert.Single(preview.Blocks);
        Assert.Equal(string.Empty, DocumentText.Of(preview).Trim());
    }

    [Fact]
    public void MeasuringStopsAsSoonAsTheBudgetIsBeaten()
    {
        var doc = DocumentNode.FromParagraphs(new string('a', 1_000_000));

        Assert.True(DocumentPreview.IsLongerThan(doc, 10));
        Assert.False(DocumentPreview.IsLongerThan(DocumentNode.FromParagraphs("abc"), 3));
        Assert.True(DocumentPreview.IsLongerThan(DocumentNode.FromParagraphs("abcd"), 3));
    }

    /// <remarks>
    /// F-71. A leading table or list was measured by building its whole plain text and taking the
    /// length, so asking whether a document with a 50,000-row table first was longer than eighty
    /// characters allocated 17.4 MB — against the remark's promise that it costs eighty
    /// characters. The container is now walked paragraph by paragraph and the walk stops inside
    /// it once the budget is beaten.
    /// </remarks>
    [Fact]
    public void ALeadingTableIsMeasuredOnlyUntilTheBudgetIsBeaten()
    {
        var doc = new DocumentNode(
        [
            new TableNode(Enumerable.Range(0, 50_000).Select(i =>
                new TableRowNode([TableCellNode.FromText($"Row {i}"), TableCellNode.FromText("value")]))),
            ParagraphNode.FromText("After the table."),
        ]);

        Assert.True(DocumentPreview.IsLongerThan(doc, 80));

        WorkCounters.PreviewParagraphsMeasured = 0;

        var before = GC.GetAllocatedBytesForCurrentThread();
        var longer = DocumentPreview.IsLongerThan(doc, 80);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(longer);
        Assert.True(allocated < 64 * 1024, $"allocated {allocated:N0} bytes");
        Assert.True(
            WorkCounters.PreviewParagraphsMeasured <= 20,
            $"measured {WorkCounters.PreviewParagraphsMeasured} paragraphs to beat a budget of 80");
    }

    /// <remarks>
    /// Stopping early must not change the count it stops on. A container still counts as its
    /// plain text — paragraphs joined by a newline, an image as its fallback text — and a
    /// top-level paragraph still counts its own length, so the answer is checked against that
    /// definition at every budget either side of the true length.
    /// </remarks>
    [Fact]
    public void AContainerCountsExactlyItsPlainText()
    {
        var image = new ImageEmbed("data:image/png;base64,AA==", altText: "a picture");
        var withImage = InlineContent.Create($"x{InlineContent.Placeholder}y", [], [image]);
        DocumentNode[] documents =
        [
            new([new TableNode([new TableRowNode([TableCellNode.FromText("ab"), TableCellNode.FromText("cd")])])]),
            new([
                ParagraphNode.FromText("lead"),
                new ListNode([
                    ListItemNode.FromText("one"),
                    new ListItemNode([new ParagraphNode(withImage), ParagraphNode.Empty]),
                ]),
                RuleNode.Instance,
                new ParagraphNode(withImage),
                new TableNode([new TableRowNode([new TableCellNode([ParagraphNode.Empty, ParagraphNode.FromText("z")])])]),
            ]),

            // Block images count as their alt text, at the top level and nested, as DocumentText
            // reads them (rubber-duck review 8.1); an image without alt text counts nothing.
            new([new BlockImageNode(new ImageEmbed("pic.png", altText: "a block picture"))]),
            new([
                ParagraphNode.FromText("p"),
                new BlockImageNode(new ImageEmbed("pic.png", altText: "")),
                new TableNode([new TableRowNode([new TableCellNode([
                    ParagraphNode.FromText("q"),
                    new BlockImageNode(new ImageEmbed("pic.png", altText: "nested")),
                ])])]),
                new ListNode([new ListItemNode([new BlockImageNode(new ImageEmbed("pic.png", altText: "in a list"))])]),
            ]),
        ];

        foreach (var doc in documents)
        {
            var length = doc.Blocks.Sum(block => block is ParagraphNode paragraph
                ? paragraph.Content.Length
                : DocumentText.Of([block]).Length);

            for (var budget = 0; budget <= length + 1; budget++)
            {
                Assert.True(
                    DocumentPreview.IsLongerThan(doc, budget) == length > budget,
                    $"length {length}, budget {budget}");
            }
        }
    }

    /// <summary>
    /// A document that is only a block image with long alt text is longer than a short budget,
    /// and its preview is cut. Phase 6's early-stopping count was merged over phase 7's
    /// DocumentText, which reads a block image as its alt text; the count looked only at
    /// children, and a block image has none, so it counted nothing (rubber-duck review 8.1).
    /// </summary>
    [Fact]
    public void ABlockImageCountsItsAltText()
    {
        var doc = new DocumentNode([new BlockImageNode(new ImageEmbed("pic.png", altText: new string('a', 200)))]);

        Assert.True(DocumentPreview.IsLongerThan(doc, 80));
        Assert.NotSame(doc, DocumentPreview.Truncate(doc, 80));
    }

    /// <remarks>
    /// A container counts each image as its fallback text, and images can share one alt text, so
    /// a paragraph's count is not bounded by anything held in memory. It was kept in an
    /// <see cref="int"/> and checked only once the paragraph was done: 2,049 images sharing one
    /// mebi-character alt text add up to 2³¹ + 2²⁰, which wrapped to a negative count and
    /// answered "not longer" at every budget. The count is a <see cref="long"/> now and is checked
    /// after each image, so the second question costs one image, not 2,049.
    /// </remarks>
    [Fact]
    public void ACountPastTwoBillionDoesNotWrapAround()
    {
        const int Images = 2_049;
        var image = new ImageEmbed("data:image/png;base64,AA==", altText: new string('a', 1 << 20));
        var content = InlineContent.Create(
            new string(InlineContent.Placeholder, Images),
            [],
            Enumerable.Repeat<InlineEmbed>(image, Images));
        var doc = new DocumentNode(
            [new TableNode([new TableRowNode([new TableCellNode([new ParagraphNode(content)])])])]);

        Assert.True(DocumentPreview.IsLongerThan(doc, int.MaxValue));

        WorkCounters.PreviewEmbedsMeasured = 0;

        Assert.True(DocumentPreview.IsLongerThan(doc, 80));
        Assert.Equal(1, WorkCounters.PreviewEmbedsMeasured);
    }

    /// <remarks>
    /// The same table, behind a short paragraph, cut rather than measured. Truncate decided
    /// whether a container fits by building its whole plain text and taking the length, which for
    /// this one is 2³¹ + 2²⁰ characters: four gigabytes of string builder before it gave up and
    /// threw, to drop a table that the first image had already ruled out. It uses the bounded
    /// count now, so the cut costs what the measurement does.
    /// </remarks>
    [Fact]
    public void TruncatingPastTwoBillionDoesNotBuildTheText()
    {
        const int Images = 2_049;
        var image = new ImageEmbed("data:image/png;base64,AA==", altText: new string('a', 1 << 20));
        var content = InlineContent.Create(
            new string(InlineContent.Placeholder, Images),
            [],
            Enumerable.Repeat<InlineEmbed>(image, Images));
        var doc = new DocumentNode(
        [
            ParagraphNode.FromText("intro"),
            new TableNode([new TableRowNode([new TableCellNode([new ParagraphNode(content)])])]),
        ]);

        WorkCounters.PreviewEmbedsMeasured = 0;

        var watch = Stopwatch.StartNew();
        var before = GC.GetAllocatedBytesForCurrentThread();
        var preview = DocumentPreview.Truncate(doc, 80);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        watch.Stop();

        Assert.Equal("intro", ((ParagraphNode)Assert.Single(preview.Blocks)).Content.Text);
        Assert.True(allocated < 4 * 1024 * 1024, $"allocated {allocated:N0} bytes");
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");

        // One image each for the question and the cut, not 2,049.
        Assert.True(
            WorkCounters.PreviewEmbedsMeasured <= 2,
            $"measured {WorkCounters.PreviewEmbedsMeasured} embeds to cut at 80");
    }

    /// <remarks>
    /// Measuring a container without building its text must not change which containers are
    /// kept. Every budget either side of each block's end is checked against the definition: a
    /// paragraph is cut to what is left, and a container stays, the same object, exactly when its
    /// plain text fits in what is left.
    /// </remarks>
    [Fact]
    public void AContainerIsKeptExactlyWhenItsPlainTextFits()
    {
        var image = new ImageEmbed("data:image/png;base64,AA==", altText: "a picture");
        var withImage = InlineContent.Create($"x{InlineContent.Placeholder}y", [], [image]);
        DocumentNode[] documents =
        [
            new([new TableNode([new TableRowNode([TableCellNode.FromText("ab"), TableCellNode.FromText("cd")])])]),
            new([
                ParagraphNode.FromText("lead"),
                new ListNode([
                    ListItemNode.FromText("one"),
                    new ListItemNode([new ParagraphNode(withImage), ParagraphNode.Empty]),
                ]),
                RuleNode.Instance,
                ParagraphNode.FromText("middle"),
                new TableNode([new TableRowNode([new TableCellNode([ParagraphNode.Empty, ParagraphNode.FromText("z")])])]),
                ParagraphNode.FromText("tail"),
            ]),
        ];

        foreach (var doc in documents)
        {
            var length = doc.Blocks.Sum(Measure);

            for (var budget = 0; budget <= length + 1; budget++)
            {
                var preview = DocumentPreview.Truncate(doc, budget);
                var expected = new List<BlockNode>();
                var left = budget;

                foreach (var block in doc.Blocks)
                {
                    var size = Measure(block);

                    if (left == 0 || (block is not ParagraphNode && size > left))
                    {
                        break;
                    }

                    if (block is ParagraphNode paragraph && size > left)
                    {
                        expected.Add(paragraph.WithContent(paragraph.Content.Substring(0, left)));

                        break;
                    }

                    expected.Add(block);
                    left -= size;
                }

                var context = $"budget {budget}";

                if (expected.Count == 0)
                {
                    expected.Add(ParagraphNode.Empty);
                }

                Assert.True(expected.Count == preview.Blocks.Length, context);

                for (var i = 0; i < expected.Count; i++)
                {
                    if (expected[i] is ParagraphNode paragraph)
                    {
                        Assert.True(
                            paragraph.Content.Text == ((ParagraphNode)preview.Blocks[i]).Content.Text,
                            context);
                    }
                    else
                    {
                        Assert.True(ReferenceEquals(expected[i], preview.Blocks[i]), context);
                    }
                }
            }
        }

        static int Measure(BlockNode block) => block is ParagraphNode paragraph
            ? paragraph.Content.Length
            : DocumentText.Of([block]).Length;
    }

    [Fact]
    public void TheArgumentsAreChecked()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentPreview.Truncate(null!, 10));
        Assert.Throws<ArgumentNullException>(() => DocumentPreview.IsLongerThan(null!, 10));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DocumentPreview.Truncate(DocumentNode.Empty, -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => DocumentPreview.IsLongerThan(DocumentNode.Empty, -1));
    }
}
