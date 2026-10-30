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
}
