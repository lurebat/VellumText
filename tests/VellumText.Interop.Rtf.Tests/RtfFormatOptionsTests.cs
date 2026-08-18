using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// Whether a host can configure the RTF importer through the object it actually registers.
/// </summary>
/// <remarks>
/// <c>RtfImporter.Import(rtf, options)</c> is not the path a control takes: the control holds an
/// <see cref="IDocumentImporter"/> and calls <c>Import(text)</c>. Without
/// <see cref="RtfFormat.With"/> every option — including
/// <see cref="RtfImportOptions.AllowRemoteImages"/>, which is a privacy control — was out of reach
/// of an application that pastes RTF into the editor, which is what the package is for.
/// </remarks>
public class RtfFormatOptionsTests
{
    /// <summary>A hyperlink field with a relative target: the shortest thing a base URI changes.</summary>
    private const string RelativeLink =
        @"{\rtf1\ansi{\field{\*\fldinst{HYPERLINK ""/relative""}}{\fldrslt{click}}}}";

    [Fact]
    public void The_shared_instance_imports_with_the_paranoid_defaults()
    {
        Assert.Same(RtfImportOptions.Default, RtfFormat.Instance.ImportOptions);
    }

    [Fact]
    public void With_leaves_the_shared_instance_alone()
    {
        var configured = RtfFormat.Instance.With(new RtfImportOptions { AllowRemoteImages = true });

        Assert.NotSame(RtfFormat.Instance, configured);
        Assert.True(configured.ImportOptions.AllowRemoteImages);
        Assert.False(RtfFormat.Instance.ImportOptions.AllowRemoteImages);
    }

    [Fact]
    public void With_rejects_null_rather_than_silently_meaning_the_defaults()
    {
        Assert.Throws<ArgumentNullException>(() => RtfFormat.Instance.With(null!));
    }

    /// <summary>
    /// The options reach the importer through an interface-typed call, which is all the control
    /// ever makes.
    /// </summary>
    [Fact]
    public void A_relative_url_resolves_against_the_base_the_registered_format_carries()
    {
        IDocumentImporter configured = RtfFormat.Instance.With(
            new RtfImportOptions { BaseUri = new Uri("https://example.com/dir/page") });

        Assert.True(AnyLink(configured.Import(RelativeLink), "https://example.com/relative"));
    }

    /// <summary>
    /// The companion that stops the test above passing for the wrong reason: the default drops it.
    /// </summary>
    [Fact]
    public void A_relative_url_is_still_dropped_by_the_shared_instance()
    {
        IDocumentImporter shared = RtfFormat.Instance;

        Assert.False(AnyLink(shared.Import(RelativeLink), "https://example.com/relative"));
    }

    [Fact]
    public void The_stream_overload_carries_the_options_as_well()
    {
        IDocumentImporter configured = RtfFormat.Instance.With(
            new RtfImportOptions { BaseUri = new Uri("https://example.com/dir/page") });

        using var source = new MemoryStream(System.Text.Encoding.Latin1.GetBytes(RelativeLink));

        Assert.True(AnyLink(configured.Import(source), "https://example.com/relative"));
    }

    private static bool AnyLink(ImportResult result, string href) =>
        result.Doc.Blocks
            .OfType<ParagraphNode>()
            .Any(p => Enumerable
                .Range(0, p.Content.Length)
                .Any(i => p.Content.MarkAt(i).Link?.Href == href));
}
