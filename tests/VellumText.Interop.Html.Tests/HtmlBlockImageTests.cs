using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// A block image written so that it can be told apart from an inline one, and read back as one
/// (F-51, audit of 0282015).
/// </summary>
/// <remarks>
/// It used to be written as <c>&lt;p&gt;&lt;img&gt;&lt;/p&gt;</c>, which is also how an inline
/// image alone in a paragraph is written, so every copy turned a block image into a paragraph
/// holding a picture — no block selection, no resize handles — with no diagnostic.
/// </remarks>
public class HtmlBlockImageTests
{
    private const string Png =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    [Theory]
    [InlineData(TextAlign.Default)]
    [InlineData(TextAlign.Center)]
    [InlineData(TextAlign.Right)]
    public async Task A_block_image_comes_back_a_block_image(TextAlign align)
    {
        var image = new ImageEmbed(Png, 20, 10, "pic");
        var doc = new DocumentNode([
            ParagraphNode.FromText("x"),
            new BlockImageNode(image, align),
            ParagraphNode.FromText("y"),
        ]);

        var html = HtmlExporter.Instance.Export(doc);

        Assert.Contains("<figure", html, StringComparison.Ordinal);

        var back = (await HtmlImporter.ImportAsync(html, cancellationToken: TestContext.Current.CancellationToken)).Doc;

        Assert.Equal(3, back.Blocks.Length);

        var block = Assert.IsType<BlockImageNode>(back.Blocks[1]);

        Assert.Equal(image, block.Image);
        Assert.Equal(align, block.Align);
    }

    [Fact]
    public async Task An_inline_image_alone_in_a_paragraph_stays_inline()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromEmbed(new ImageEmbed(Png, altText: "pic"))),
        ]);

        var html = HtmlExporter.Instance.Export(doc);

        Assert.DoesNotContain("<figure", html, StringComparison.Ordinal);
        Assert.IsType<ParagraphNode>(Assert.Single((await HtmlImporter.ImportAsync(html, cancellationToken: TestContext.Current.CancellationToken)).Doc.Blocks));
    }

    /// <summary>
    /// Only a figure that is nothing but its image is a block image. One with a caption is the
    /// image and the caption, as before — a block image has nowhere to keep the caption.
    /// </summary>
    [Fact]
    public async Task A_figure_with_a_caption_is_not_a_block_image()
    {
        var back = (await HtmlImporter.ImportAsync(
            $"<figure><img src=\"{Png}\"><figcaption>cap</figcaption></figure>", cancellationToken: TestContext.Current.CancellationToken)).Doc;

        Assert.DoesNotContain(back.Blocks, b => b is BlockImageNode);
        Assert.Contains("cap", DocumentText.Of(back), StringComparison.Ordinal);
    }

    /// <summary>
    /// A link around the figure belongs to the image, and a block image cannot carry one, so
    /// the image stays inline where the link survives.
    /// </summary>
    [Fact]
    public async Task A_linked_figure_keeps_its_link()
    {
        var back = (await HtmlImporter.ImportAsync(
            $"<a href=\"https://e.test/\"><figure><img src=\"{Png}\"></figure></a>", cancellationToken: TestContext.Current.CancellationToken)).Doc;

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(back.Blocks));

        Assert.Equal("https://e.test/", paragraph.Content.MarkAt(0).Link?.Href);
    }

    [Fact]
    public async Task A_figure_whose_image_was_refused_leaves_no_block_image()
    {
        var back = (await HtmlImporter.ImportAsync("<figure><img src=\"https://remote.invalid/a.png\"></figure>", cancellationToken: TestContext.Current.CancellationToken)).Doc;

        Assert.DoesNotContain(back.Blocks, b => b is BlockImageNode);
    }
}
