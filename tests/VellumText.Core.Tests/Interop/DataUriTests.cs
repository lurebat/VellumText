namespace VellumText.Tests.Interop;

/// <summary>
/// The single reading of a <c>data:</c> URL that the sanitizer, the renderer, the RTF exporter and
/// the image store share.
/// </summary>
public class DataUriTests
{
    [Theory]
    [InlineData("data:image/png;base64,AAAA", "image/png", "AAAA")]
    [InlineData("data:image/png;name=a.png;base64,AAAA", "image/png", "AAAA")]
    [InlineData("DATA:IMAGE/PNG;BASE64,AAAA", "IMAGE/PNG", "AAAA")]
    [InlineData("data:;base64,AAAA", "", "AAAA")]
    [InlineData("data:image/png;base64,", "image/png", "")]
    [InlineData("data:image/png;base64,AA,AA", "image/png", "AA,AA")]
    public void A_base64_data_url_is_split_into_its_type_and_payload(
        string source, string mediaType, string payload)
    {
        Assert.True(DataUri.TryParse(source, out var type, out var base64));
        Assert.Equal(mediaType, type);
        Assert.Equal(payload, base64.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("data:")]
    [InlineData("data:image/png;base64")]
    [InlineData("data:image/png,AAAA")]
    [InlineData("data:image/png;base64;x=y,AAAA")]
    [InlineData("data:image/png;base64x,AAAA")]
    [InlineData(" data:image/png;base64,AAAA")]
    [InlineData("https://example.com/data:image/png;base64,AAAA")]
    [InlineData("image/png;base64,AAAA")]
    public void Anything_else_is_not_one(string? source)
    {
        // ";base64" is the last parameter of the header by RFC 2397, so one followed by another
        // parameter is not a marker, and the renderer has always read it that way. Whitespace
        // around a URL in an attribute is HTML's to strip, before a URL is stored.
        Assert.False(DataUri.TryParse(source, out _, out _));
    }

    [Fact]
    public void What_is_written_reads_back()
    {
        byte[] bytes = [0, 1, 2, 250, 251, 252, 253];

        var url = DataUri.Format("image/png", bytes);

        Assert.Equal("data:image/png;base64,AAEC+vv8/Q==", url);
        Assert.True(DataUri.TryParse(url, out var type, out var base64));
        Assert.Equal("image/png", type);
        Assert.Equal(bytes, Convert.FromBase64String(base64.ToString()));
    }
}
