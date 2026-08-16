using System.Text;

using Vellum.Interop.Html;

namespace Vellum.Tests.Interop.Html;

/// <summary>
/// The <c>CF_HTML</c> envelope, whose offsets are counted in bytes against a payload held as
/// characters. Everything here is arithmetic that is wrong in a way nobody notices until a
/// document contains an accent.
/// </summary>
public class ClipboardHtmlTests
{
    private static string Header(
        string startHtml, string endHtml, string startFragment, string endFragment, string? source = null) =>
        $"Version:0.9\r\nStartHTML:{startHtml}\r\nEndHTML:{endHtml}\r\n"
        + $"StartFragment:{startFragment}\r\nEndFragment:{endFragment}\r\n"
        + (source is null ? "" : $"SourceURL:{source}\r\n");

    [Fact]
    public void Plain_html_is_not_mistaken_for_a_clipboard_payload()
    {
        Assert.False(ClipboardHtml.IsClipboardHtml("<p>Version:0.9</p>"));
        Assert.False(ClipboardHtml.IsClipboardHtml(null));
        Assert.False(ClipboardHtml.IsClipboardHtml(""));
        Assert.True(ClipboardHtml.IsClipboardHtml(Header("1", "2", "3", "4") + "<html></html>"));
    }

    [Fact]
    public void Html_that_is_not_wrapped_comes_back_exactly_as_it_went_in()
    {
        const string html = "<p>hello</p>";

        Assert.Equal(html, ClipboardHtml.ExtractFragment(html, out var source));
        Assert.Null(source);
    }

    [Fact]
    public void Only_the_fragment_comes_out_not_the_page_around_it()
    {
        var payload = Header("0000000097", "0000000203", "0000000133", "0000000167")
            + "<html><body><nav>menu</nav><!--StartFragment-->copied<!--EndFragment--><footer>f</footer></body></html>";

        Assert.Equal("copied", ClipboardHtml.ExtractFragment(payload, out _));
    }

    [Fact]
    public void The_source_address_is_read_when_it_is_one_that_can_be_resolved_against()
    {
        var payload = Header("1", "2", "3", "4", "https://example.com/page")
            + "<html><body><!--StartFragment-->x<!--EndFragment--></body></html>";

        ClipboardHtml.ExtractFragment(payload, out var source);

        Assert.Equal("https://example.com/page", source?.AbsoluteUri);
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("about:blank")]
    [InlineData("not a url at all")]
    public void A_source_address_that_is_not_a_web_page_is_ignored(string source)
    {
        // The address becomes the base for every relative link in the fragment. A local path would
        // turn a paste into a set of links pointing into the machine it was pasted on.
        var payload = Header("1", "2", "3", "4", source)
            + "<html><body><!--StartFragment-->x<!--EndFragment--></body></html>";

        ClipboardHtml.ExtractFragment(payload, out var parsed);

        Assert.Null(parsed);
    }

    [Fact]
    public void The_fragment_comments_are_believed_over_the_header_offsets()
    {
        // The offsets are a claim about where the fragment is; the comments are the fragment's
        // own boundary and so cannot disagree with themselves. Several widely used applications
        // emit offsets that are out by a few bytes, which is why the order matters.
        //
        // Written deliberately so the two disagree. Every other test here builds a payload where
        // they agree, which is the natural thing to write and pins nothing about the ordering -
        // a consumer hit exactly that: ten tests, full line coverage, not one able to tell the
        // two orderings apart.
        const string body = "<html><body><nav>menu</nav><!--StartFragment-->copied<!--EndFragment--></body></html>";

        var header = Header("0000000000", "0000000000", "0000000000", "0000000000");
        var honest = Encoding.UTF8.GetByteCount(
            header + "<html><body><nav>menu</nav><!--StartFragment-->");

        // Three bytes early, which slices into the middle of the preceding tag.
        var skewed = honest - 3;
        var payload = Header(
            "0000000000",
            "0000000000",
            $"{skewed:D10}",
            $"{skewed + Encoding.UTF8.GetByteCount("copied"):D10}") + body;

        var bytes = Encoding.UTF8.GetBytes(payload);
        var byOffsets = Encoding.UTF8.GetString(bytes, skewed, Encoding.UTF8.GetByteCount("copied"));

        // Anti-vacuity: the skewed offsets really do select something else, so this cannot pass by
        // the two happening to agree.
        Assert.NotEqual("copied", byOffsets);

        Assert.Equal("copied", ClipboardHtml.ExtractFragment(payload, out _));
    }

    [Fact]
    public void The_byte_offsets_are_used_when_the_comments_are_missing()
    {
        const string body = "<html><body>MENUcopiedFOOT</body></html>";

        // The header must be measured at its final width: writing "0" while measuring and ten
        // digits while emitting moves everything the offsets point at.
        var header = Header("0000000000", "0000000000", "0000000000", "0000000000");

        var start = Encoding.UTF8.GetByteCount(header + "<html><body>MENU");
        var end = start + Encoding.UTF8.GetByteCount("copied");

        var payload = Header("0000000000", "0000000000", $"{start:D10}", $"{end:D10}") + body;

        Assert.Equal("copied", ClipboardHtml.ExtractFragment(payload, out _));
    }

    [Fact]
    public void The_byte_offsets_are_counted_in_bytes_and_not_in_characters()
    {
        // The header carries a non-ASCII source address and the body has non-ASCII text before the
        // fragment, so a reader that counts characters lands short by exactly the number of extra
        // bytes in them.
        const string source = "https://example.com/caf\u00e9\u00e9\u00e9";
        const string body = "<html><body>\u00e9\u00e9MENUcopiedFOOT</body></html>";

        var header = Header("0000000000", "0000000000", "0000000000", "0000000000", source);

        var start = Encoding.UTF8.GetByteCount(header + "<html><body>\u00e9\u00e9MENU");
        var end = start + Encoding.UTF8.GetByteCount("copied");

        var payload = Header("0000000000", "0000000000", $"{start:D10}", $"{end:D10}", source) + body;

        // The same offsets read as characters would select something else entirely, which is what
        // makes this a test of the conversion rather than of the arithmetic.
        Assert.NotEqual("copied", payload.Substring(start, end - start));
        Assert.Equal("copied", ClipboardHtml.ExtractFragment(payload, out _));
    }

    [Fact]
    public void Offsets_that_run_past_the_end_do_not_throw()
    {
        var payload = Header("0", "0", "0000000010", "0009999999")
            + "<html><body>abc</body></html>";

        var fragment = ClipboardHtml.ExtractFragment(payload, out _);

        Assert.Contains("abc", fragment, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0009999999", "0000000010")]
    [InlineData("banana", "0000000010")]
    [InlineData("-5", "0000000010")]
    public void Offsets_that_make_no_sense_fall_back_to_what_follows_the_header(string start, string end)
    {
        var payload = Header("0", "0", start, end) + "<html><body>abc</body></html>";

        var fragment = ClipboardHtml.ExtractFragment(payload, out _);

        Assert.DoesNotContain("Version:", fragment, StringComparison.Ordinal);
        Assert.Contains("abc", fragment, StringComparison.Ordinal);
    }

    [Fact]
    public void A_start_comment_with_no_end_still_yields_the_rest()
    {
        var payload = Header("1", "2", "3", "4")
            + "<html><body><!--StartFragment-->copied</body></html>";

        Assert.Contains("copied", ClipboardHtml.ExtractFragment(payload, out _), StringComparison.Ordinal);
    }

    // ---- writing ------------------------------------------------------------------------------

    [Theory]
    [InlineData("<b>hi</b>")]
    [InlineData("caf\u00e9 \u05e9\u05dc\u05d5\u05dd \ud83d\ude00")]
    [InlineData("")]
    [InlineData("a<!--StartFragment-->b")]
    public void What_is_wrapped_comes_back_out(string fragment)
    {
        var payload = ClipboardHtml.Wrap(fragment);

        Assert.True(ClipboardHtml.IsClipboardHtml(payload));
        Assert.Equal(fragment, ClipboardHtml.ExtractFragment(payload, out _));
    }

    [Fact]
    public void A_fragment_containing_the_offset_placeholder_does_not_corrupt_the_header()
    {
        // The header is written with placeholder digits and then filled in. A writer that replaces
        // over the whole payload rewrites the fragment's own text instead.
        const string fragment = "StartHTML:0000000000 and EndFragment:0000000000";

        var payload = ClipboardHtml.Wrap(fragment);

        Assert.Equal(fragment, ClipboardHtml.ExtractFragment(payload, out _));
        AssertOffsetsAreTrue(payload);
    }

    [Theory]
    [InlineData("<b>hi</b>")]
    [InlineData("caf\u00e9 \u05e9\u05dc\u05d5\u05dd \ud83d\ude00 \u4e2d\u6587")]
    public void The_offsets_written_into_the_header_point_where_they_claim_to(string fragment)
    {
        AssertOffsetsAreTrue(ClipboardHtml.Wrap(fragment, new Uri("https://example.com/\u00e9")));
    }

    [Fact]
    public void The_source_address_survives_a_round_trip()
    {
        var payload = ClipboardHtml.Wrap("x", new Uri("https://example.com/p"));

        ClipboardHtml.ExtractFragment(payload, out var source);

        Assert.Equal("https://example.com/p", source?.AbsoluteUri);
    }

    [Fact]
    public void Wrapping_nothing_is_a_mistake_worth_hearing_about()
    {
        Assert.Throws<ArgumentNullException>(() => ClipboardHtml.Wrap(null!));
    }

    /// <summary>
    /// Reads the header's own offsets and checks that the bytes they select are the fragment.
    /// </summary>
    /// <param name="payload">A payload produced by <see cref="ClipboardHtml.Wrap"/>.</param>
    private static void AssertOffsetsAreTrue(string payload)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);

        var startHtml = ReadOffset(payload, "StartHTML:");
        var endHtml = ReadOffset(payload, "EndHTML:");
        var startFragment = ReadOffset(payload, "StartFragment:");
        var endFragment = ReadOffset(payload, "EndFragment:");

        Assert.True(startHtml < startFragment, "StartHTML must precede StartFragment.");
        Assert.True(startFragment <= endFragment, "StartFragment must not follow EndFragment.");
        Assert.True(endFragment < endHtml, "EndFragment must precede EndHTML.");
        Assert.Equal(bytes.Length, endHtml);

        var html = Encoding.UTF8.GetString(bytes, startHtml, endHtml - startHtml);

        Assert.StartsWith("<html>", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>", html, StringComparison.Ordinal);

        var fragment = Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment);

        Assert.Equal(
            Encoding.UTF8.GetString(bytes, startFragment, endFragment - startFragment),
            fragment);

        // The bytes immediately before and after the fragment must be the marker comments, which
        // is what proves the offsets are not merely self-consistent but actually aimed correctly.
        var before = Encoding.UTF8.GetString(bytes, 0, startFragment);
        var after = Encoding.UTF8.GetString(bytes, endFragment, bytes.Length - endFragment);

        Assert.EndsWith("<!--StartFragment-->", before, StringComparison.Ordinal);
        Assert.StartsWith("<!--EndFragment-->", after, StringComparison.Ordinal);
    }

    private static int ReadOffset(string payload, string name)
    {
        var at = payload.IndexOf(name, StringComparison.Ordinal) + name.Length;

        return int.Parse(payload.Substring(at, 10));
    }
}
