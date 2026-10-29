using VellumText.Interop.Html;

namespace VellumText.Tests.Interop.Html;

/// <summary>
/// Whether a host can configure the HTML importer through the object it actually registers.
/// </summary>
/// <remarks>
/// <c>HtmlImporter.ImportAsync(html, options)</c> is not the path a control takes: the control holds an
/// <see cref="IDocumentImporter"/> and calls <c>ImportAsync(text)</c>. So an option that only a static
/// method accepts is unreachable from an application that uses the control, which is every
/// application. These tests are about <see cref="HtmlFormat.With"/> being the way through, and in
/// particular about <see cref="HtmlImportOptions.AllowRemoteImages"/> — a privacy control is not a
/// control if the host cannot reach it.
/// </remarks>
public class HtmlFormatOptionsTests
{
    [Fact]
    public void The_shared_instance_imports_with_the_paranoid_defaults()
    {
        Assert.Same(HtmlImportOptions.Default, HtmlFormat.Instance.ImportOptions);
    }

    [Fact]
    public void With_leaves_the_shared_instance_alone()
    {
        var configured = HtmlFormat.Instance.With(new HtmlImportOptions { AllowRemoteImages = true });

        Assert.NotSame(HtmlFormat.Instance, configured);
        Assert.True(configured.ImportOptions.AllowRemoteImages);
        Assert.False(HtmlFormat.Instance.ImportOptions.AllowRemoteImages);
    }

    [Fact]
    public void With_rejects_null_rather_than_silently_meaning_the_defaults()
    {
        Assert.Throws<ArgumentNullException>(() => HtmlFormat.Instance.With(null!));
    }

    /// <summary>
    /// The point of the whole item: a remote image survives because the host asked, through an
    /// interface-typed call that is all the control ever makes.
    /// </summary>
    [Fact]
    public async Task A_remote_image_survives_when_the_registered_format_was_configured_to_allow_it()
    {
        IDocumentImporter configured =
            HtmlFormat.Instance.With(new HtmlImportOptions { AllowRemoteImages = true });

        var image = Assert.Single(Images(await configured.ImportAsync("<img src='https://cdn.test/a.png'>", cancellationToken: TestContext.Current.CancellationToken)));

        Assert.Equal("https://cdn.test/a.png", image.Source);
    }

    /// <summary>
    /// The companion that stops the test above passing for the wrong reason: the default refuses.
    /// </summary>
    [Fact]
    public async Task A_remote_image_is_still_refused_by_the_shared_instance()
    {
        IDocumentImporter shared = HtmlFormat.Instance;

        Assert.Empty(Images(await shared.ImportAsync("<img src='https://cdn.test/a.png'>", cancellationToken: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task A_relative_url_resolves_against_the_base_the_registered_format_carries()
    {
        IDocumentImporter configured = HtmlFormat.Instance.With(
            new HtmlImportOptions { BaseUri = new Uri("https://example.com/dir/page") });

        var result = await configured.ImportAsync("<a href='/somewhere'>click</a>", cancellationToken: TestContext.Current.CancellationToken);
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Equal("https://example.com/somewhere", paragraph.Content.MarkAt(0).Link?.Href);
    }

    [Fact]
    public async Task A_relative_url_is_still_dropped_by_the_shared_instance()
    {
        IDocumentImporter shared = HtmlFormat.Instance;

        var result = await shared.ImportAsync("<a href='/somewhere'>click</a>", cancellationToken: TestContext.Current.CancellationToken);
        var paragraph = Assert.IsType<ParagraphNode>(result.Doc.Blocks[0]);

        Assert.Null(paragraph.Content.MarkAt(0).Link);
    }

    /// <summary>
    /// The stream overload is the required member of the contract, so it has to carry the options
    /// too — a host importing a file through a configured format must not silently get the
    /// defaults.
    /// </summary>
    [Fact]
    public async Task The_stream_overload_carries_the_options_as_well()
    {
        IDocumentImporter configured =
            HtmlFormat.Instance.With(new HtmlImportOptions { AllowRemoteImages = true });

        using var source = new MemoryStream(
            System.Text.Encoding.UTF8.GetBytes("<img src='https://cdn.test/a.png'>"));

        Assert.Single(Images(await configured.ImportAsync(source, cancellationToken: TestContext.Current.CancellationToken)));
    }

    private static IEnumerable<ImageEmbed> Images(ImportResult result) =>
        result.Doc.Blocks
            .OfType<ParagraphNode>()
            .SelectMany(p => p.Content.Embeds)
            .OfType<ImageEmbed>();
}
