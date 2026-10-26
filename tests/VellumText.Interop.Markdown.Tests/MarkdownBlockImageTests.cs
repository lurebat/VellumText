using VellumText.Interop.Markdown;

namespace VellumText.Tests.Interop.Markdown;

/// <summary>
/// Block images through Markdown, where an image alone in its paragraph is the block form
/// (F-51, audit of 0282015).
/// </summary>
public class MarkdownBlockImageTests
{
    private const string Png = RoundTripGenerator.Png;

    [Fact]
    public void A_block_image_comes_back_a_block_image()
    {
        var image = new ImageEmbed(Png, altText: "pic");
        var doc = new DocumentNode([
            ParagraphNode.FromText("x"),
            new BlockImageNode(image),
            ParagraphNode.FromText("y"),
        ]);

        var exported = MarkdownExporter.Instance.ExportWithDiagnostics(doc);

        Assert.Equal($"x\n\n![pic]({Png})\n\ny\n", exported.Text);
        Assert.Empty(exported.Diagnostics);

        var back = MarkdownImporter.Import(exported.Text).Doc;

        Assert.Equal(3, back.Blocks.Length);
        Assert.Equal(image, Assert.IsType<BlockImageNode>(back.Blocks[1]).Image);
    }

    [Fact]
    public void A_block_image_in_a_list_item_comes_back_a_block_image()
    {
        var image = new ImageEmbed(Png);
        var doc = new DocumentNode([
            new ListNode(
                [new ListItemNode([ParagraphNode.FromText("a"), new BlockImageNode(image)])],
                ListKind.Unordered),
        ]);

        var back = MarkdownImporter.Import(MarkdownExporter.Instance.Export(doc)).Doc;
        var item = Assert.Single(Assert.IsType<ListNode>(Assert.Single(back.Blocks)).Items);

        Assert.Equal(image, Assert.IsType<BlockImageNode>(item.Blocks[1]).Image);
    }

    /// <summary>
    /// The price of the notation: an inline image that is the whole of its paragraph is written
    /// the same way, and comes back a block image. That is reported rather than left silent.
    /// </summary>
    [Fact]
    public void An_inline_image_alone_in_a_paragraph_is_reported()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromEmbed(new ImageEmbed(Png))),
        ]);

        var exported = MarkdownExporter.Instance.ExportWithDiagnostics(doc);

        var diagnostic = Assert.Single(exported.Diagnostics);

        Assert.Equal(DiagnosticSeverity.Downgraded, diagnostic.Severity);
        Assert.Equal("image-paragraph", diagnostic.Context);
        Assert.IsType<BlockImageNode>(Assert.Single(MarkdownImporter.Import(exported.Text).Doc.Blocks));
    }

    /// <summary>A badge — an image inside a link — is inline, and stays a paragraph.</summary>
    [Fact]
    public void A_linked_image_alone_in_a_paragraph_stays_inline()
    {
        var back = MarkdownImporter.Import($"[![b]({Png})](https://e.test/)").Doc;
        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(back.Blocks));

        Assert.Equal("https://e.test/", paragraph.Content.MarkAt(0).Link?.Href);
    }

    [Fact]
    public void An_image_beside_text_stays_inline()
    {
        Assert.IsType<ParagraphNode>(
            Assert.Single(MarkdownImporter.Import($"see ![b]({Png})").Doc.Blocks));
    }

    [Fact]
    public void Generated_documents_with_block_images_survive_when_the_export_reports_no_loss()
    {
        var failures = MarkdownRoundTripPropertyTests.Run(
            10_000, 1500, new RoundTripGenerator.Options { BlockImages = true }, out var clean);

        Assert.True(failures.Count == 0, string.Join("\n\n", failures));
        Assert.True(clean > 300, $"only {clean} of 1500 documents exported without a reported loss");
    }
}
