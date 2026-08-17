namespace VellumText.Tests;

/// <summary>
/// The one plain-text projection of a document.
/// </summary>
/// <remarks>
/// It has three callers who must agree — the clipboard's plain-text flavour, the accessibility
/// peer, and anything that searches — so the cases that matter are the ones where a plausible
/// second implementation would differ: what an embed contributes, and where the newlines fall.
/// </remarks>
public class DocumentTextTests
{
    [Fact]
    public void BlocksAreSeparatedByOneNewline()
    {
        var doc = DocumentNode.FromParagraphs("first", "second", "third");

        Assert.Equal("first\nsecond\nthird", DocumentText.Of(doc));
    }

    [Fact]
    public void AnEmptyDocumentIsEmptyText()
    {
        Assert.Equal(string.Empty, DocumentText.Of(DocumentNode.Empty));
    }

    /// <remarks>
    /// An empty paragraph is a blank line, not nothing. Skipping it would silently join the
    /// paragraphs on either side of it.
    /// </remarks>
    [Fact]
    public void AnEmptyParagraphIsStillALine()
    {
        var doc = DocumentNode.FromParagraphs("first", string.Empty, "third");

        Assert.Equal("first\n\nthird", DocumentText.Of(doc));
    }

    /// <remarks>
    /// The reason this type exists. An embed occupies one position, held in the text by
    /// <see cref="InlineContent.Placeholder"/>; reading that character out to a screen reader, or
    /// pasting it into a text editor, is noise. The alt text is the whole point of alt text.
    /// </remarks>
    [Fact]
    public void AnEmbedContributesItsFallbackNotItsPlaceholder()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                $"see {InlineContent.Placeholder} here",
                [],
                [new ImageEmbed("cat.png", altText: "a cat")])),
        ]);

        Assert.Equal("see a cat here", DocumentText.Of(doc));
    }

    [Fact]
    public void AnEmbedWithNoFallbackContributesNothing()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                $"see {InlineContent.Placeholder} here",
                [],
                [new ImageEmbed("cat.png")])),
        ]);

        Assert.Equal("see  here", DocumentText.Of(doc));
    }

    /// <remarks>
    /// Nesting is what separates this from a loop over the top-level blocks. A table's paragraphs
    /// are two levels down, and a list's are one.
    /// </remarks>
    [Fact]
    public void ParagraphsInsideOtherBlocksAreReached()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromText("above")),
            new TableNode([
                new TableRowNode([
                    new TableCellNode([new ParagraphNode(InlineContent.FromText("a"))]),
                    new TableCellNode([new ParagraphNode(InlineContent.FromText("b"))]),
                ]),
            ]),
            new ParagraphNode(InlineContent.FromText("below")),
        ]);

        Assert.Equal("above\na\nb\nbelow", DocumentText.Of(doc));
    }

    /// <remarks>
    /// A marker is drawn by the view and is not in the model. Inventing one here would put a
    /// bullet glyph on the clipboard.
    /// </remarks>
    [Fact]
    public void AListItemContributesNoMarker()
    {
        var doc = new DocumentNode([
            new ListNode([
                new ListItemNode([new ParagraphNode(InlineContent.FromText("one"))]),
                new ListItemNode([new ParagraphNode(InlineContent.FromText("two"))]),
            ]),
        ]);

        Assert.Equal("one\ntwo", DocumentText.Of(doc));
    }

    [Fact]
    public void ItRefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentText.Of((DocumentNode)null!));
        Assert.Throws<ArgumentNullException>(() => DocumentText.Of((IEnumerable<Node>)null!));
    }
}
