using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The default paragraph style resolver, and specifically its agreement with the shared heading
/// ramp.
/// </summary>
/// <remarks>
/// The editor and the RTF exporter each carried their own copy of the heading sizes and had drifted
/// apart at every level below the first, so a heading pasted into Word did not match the heading on
/// screen. Both now read one shared ramp; this pins the editor's half of that.
/// </remarks>
public class ParagraphStyleResolverTests
{
    /// <remarks>
    /// The expected numbers are written out rather than read back from the shared ramp the
    /// resolver itself consults: an assertion sourced from the code under test holds however the
    /// ramp is edited, and would not have caught the drift that made this test necessary.
    /// </remarks>
    [Theory]
    [InlineData(ParagraphKind.Heading1, 2.0)]
    [InlineData(ParagraphKind.Heading2, 1.6)]
    [InlineData(ParagraphKind.Heading3, 1.35)]
    [InlineData(ParagraphKind.Heading4, 1.2)]
    [InlineData(ParagraphKind.Heading5, 1.1)]
    [InlineData(ParagraphKind.Heading6, 1.0)]
    public void A_heading_is_drawn_at_the_shared_scale(ParagraphKind kind, double expected) =>
        Assert.Equal(expected, new ParagraphStyleResolver().Resolve(kind).FontScale);

    [Theory]
    [InlineData(ParagraphKind.Body)]
    [InlineData(ParagraphKind.Quote)]
    public void Anything_that_is_not_a_heading_is_drawn_at_body_size(ParagraphKind kind) =>
        Assert.Equal(1.0, new ParagraphStyleResolver().Resolve(kind).FontScale);
}
