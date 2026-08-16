using Vellum;

namespace Vellum.Tests.Core;

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
