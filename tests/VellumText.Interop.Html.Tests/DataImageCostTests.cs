using System.Globalization;

using VellumText.ImportProbe;

namespace VellumText.Interop.Html.Tests;

/// <summary>
/// What keeping a pasted picture costs: the picture arrives as a data URL several megabytes long,
/// and every copy of it the import makes is that long again.
/// </summary>
public sealed class DataImageCostTests
{
    /// <summary>
    /// A kept 4 MiB picture costs a bounded multiple of the HTML it came in. Setting its data URL
    /// on the image element, which the sanitizer does once as it approves it, made the DOM resolve
    /// it for an image request nothing here can make: six copies, 64 MiB.
    /// </summary>
    /// <remarks>
    /// Run in a child process, which measures allocation over the import alone. What remains is
    /// the input guard's and the parser's tokenizing, each of which builds the attribute value: about
    /// 12 times the HTML as a string, against 19 before.
    /// </remarks>
    [Fact]
    public void A_kept_picture_costs_a_bounded_multiple_of_the_html()
    {
        var html = "<p>x<img src=\"data:image/png;base64," + Convert.ToBase64String(new byte[4 * 1024 * 1024]) + "\">y</p>";
        var htmlMb = html.Length * sizeof(char) / (1024 * 1024);

        var result = ProbeRunner.Run("html", html, TimeSpan.FromSeconds(60));

        Assert.True(result.ExitCode == 0, result.ToString());
        Assert.True(int.Parse(result.Value("allocatedMb")!, CultureInfo.InvariantCulture) < 15 * htmlMb, $"HTML {htmlMb} MiB; {result}");
        Assert.Equal("xy", result.Value("text"));
        Assert.Equal("1", result.Value("images"));
    }

    /// <summary>
    /// Measuring a data image's decoded size does not decode it into a buffer of that size.
    /// </summary>
    [Fact]
    public void Measuring_a_data_image_does_not_allocate_its_size()
    {
        var payload = Convert.ToBase64String(new byte[4 * 1024 * 1024]);

        SanitizingReader.MeasureDataImage(payload, int.MaxValue);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var verdict = SanitizingReader.MeasureDataImage(payload, int.MaxValue);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(SanitizingReader.DataImageVerdict.Usable, verdict);
        Assert.True(allocated < 64 * 1024, $"measuring allocated {allocated:N0} bytes");
    }

    /// <summary>
    /// The verdicts are the ones decoding the payload gave: the same whitespace, padding and size
    /// boundary.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("AAAA")]
    [InlineData("AAA=")]
    [InlineData("AA==")]
    [InlineData("A===")]
    [InlineData("AAA")]
    [InlineData("AAAAA")]
    [InlineData("AA AA")]
    [InlineData(" AAAA ")]
    [InlineData("AA\r\nAA")]
    [InlineData("AA\tAA")]
    [InlineData("AAAA====")]
    [InlineData("AA=A")]
    [InlineData("!!!!")]
    [InlineData("AAAAAAAAAAAA")]
    [InlineData("AAAAAAAAAAA=")]
    [InlineData("AAAAAAAAAA==")]
    [InlineData("    ")]
    [InlineData("=")]
    [InlineData("AA==AA==")]
    public void Measuring_agrees_with_decoding(string payload)
    {
        foreach (var limit in new[] { 0, 1, 2, 3, 6, 7, 8, 9, int.MaxValue })
        {
            Assert.True(
                Decoded(payload, limit) == SanitizingReader.MeasureDataImage(payload, limit),
                $"'{payload}' at {limit}: decoding says {Decoded(payload, limit)}");
        }
    }

    /// <summary>The verdict by decoding the payload, as it was decided before.</summary>
    private static SanitizingReader.DataImageVerdict Decoded(string payload, int maxBytes)
    {
        if ((long)(payload.Length / 4) * 3 - 2 > maxBytes)
        {
            return SanitizingReader.DataImageVerdict.TooLarge;
        }

        var buffer = new byte[(payload.Length / 4 * 3) + 3];

        if (!Convert.TryFromBase64Chars(payload, buffer, out var written) || written == 0)
        {
            return SanitizingReader.DataImageVerdict.Undecodable;
        }

        return written > maxBytes ? SanitizingReader.DataImageVerdict.TooLarge : SanitizingReader.DataImageVerdict.Usable;
    }
}
