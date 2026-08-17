namespace VellumText.Tests.Primitives;

public class RgbaTests
{
    [Fact]
    public void Default_is_fully_transparent()
    {
        Assert.Equal(default, Rgba.Transparent);
        Assert.True(Rgba.Transparent.IsTransparent);
        Assert.Equal(0u, Rgba.Transparent.ToArgb());
    }

    [Theory]
    [InlineData(0x00000000u)]
    [InlineData(0xFF000000u)]
    [InlineData(0xFFFFFFFFu)]
    [InlineData(0x80123456u)]
    [InlineData(0x01FEDCBAu)]
    public void Argb_packing_round_trips(uint argb)
    {
        Assert.Equal(argb, Rgba.FromArgb(argb).ToArgb());
    }

    [Fact]
    public void FromArgb_assigns_channels_in_the_documented_order()
    {
        var c = Rgba.FromArgb(0x11223344);

        Assert.Equal(0x11, c.A);
        Assert.Equal(0x22, c.R);
        Assert.Equal(0x33, c.G);
        Assert.Equal(0x44, c.B);
    }

    [Fact]
    public void FromRgb_is_opaque()
    {
        Assert.Equal(new Rgba(1, 2, 3, 255), Rgba.FromRgb(1, 2, 3));
    }

    [Fact]
    public void Equality_is_structural()
    {
        Assert.Equal(new Rgba(1, 2, 3, 4), new Rgba(1, 2, 3, 4));
        Assert.NotEqual(new Rgba(1, 2, 3, 4), new Rgba(1, 2, 3, 5));
        Assert.Equal(new Rgba(1, 2, 3, 4).GetHashCode(), new Rgba(1, 2, 3, 4).GetHashCode());
    }

    [Fact]
    public void A_transparent_colour_keeps_its_rgb_channels()
    {
        // Transparent black and transparent white are distinct values. Mark normalization
        // merges on equality, so collapsing them would silently lose a formatting change.
        var transparentWhite = Rgba.White.WithAlpha(0);

        Assert.True(transparentWhite.IsTransparent);
        Assert.NotEqual(Rgba.Transparent, transparentWhite);
    }

    [Fact]
    public void ToString_is_the_packed_hex_form()
    {
        Assert.Equal("#FF0000FF", Rgba.FromRgb(0, 0, 255).ToString());
        Assert.Equal("#00000000", Rgba.Transparent.ToString());
    }

    [Fact]
    public void Named_colours_are_what_they_claim()
    {
        Assert.Equal(new Rgba(0, 0, 0, 255), Rgba.Black);
        Assert.Equal(new Rgba(255, 255, 255, 255), Rgba.White);
    }
}
