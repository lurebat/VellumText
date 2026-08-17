namespace VellumText.Tests.Model;

public class MarkSetTests
{
    [Fact]
    public void Default_overrides_nothing()
    {
        Assert.True(MarkSet.Empty.IsEmpty);
        Assert.Equal(default, MarkSet.Empty);
        Assert.Equal(TextStyle.None, MarkSet.Empty.Style);
        Assert.Null(MarkSet.Empty.FontFamily);
        Assert.Null(MarkSet.Empty.FontSize);
        Assert.Null(MarkSet.Empty.Foreground);
        Assert.Null(MarkSet.Empty.Highlight);
        Assert.Null(MarkSet.Empty.Link);
    }

    [Fact]
    public void Blank_font_family_normalizes_to_null()
    {
        // Two marks that mean the same thing must compare equal, or span merging leaves
        // a seam in the document that nothing can see but everything compares against.
        Assert.Null(new MarkSet(fontFamily: "").FontFamily);
        Assert.Null(new MarkSet(fontFamily: "   ").FontFamily);
        Assert.Equal(MarkSet.Empty, new MarkSet(fontFamily: "  "));
    }

    [Fact]
    public void Font_family_is_trimmed()
    {
        Assert.Equal("Segoe UI", new MarkSet(fontFamily: "  Segoe UI  ").FontFamily);
        Assert.Equal(new MarkSet(fontFamily: "Segoe UI"), new MarkSet(fontFamily: " Segoe UI "));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rejects_a_nonsensical_font_size(double size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarkSet(fontSize: size));
    }

    [Fact]
    public void Rejects_unknown_style_bits()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MarkSet((TextStyle)(1 << 20)));
    }

    [Fact]
    public void Rejects_sub_and_super_together()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new MarkSet(TextStyle.Sub | TextStyle.Super));
    }

    [Fact]
    public void With_expression_validates_too()
    {
        // `with` bypasses the constructor, so validation living only there would leave a
        // hole wide enough to drive the whole model through.
        var mark = new MarkSet(TextStyle.Sub);

        Assert.Throws<ArgumentOutOfRangeException>(() => mark with { FontSize = -3 });
        Assert.Throws<ArgumentOutOfRangeException>(
            () => mark with { Style = TextStyle.Sub | TextStyle.Super });
    }

    [Fact]
    public void Has_requires_every_requested_bit()
    {
        var mark = new MarkSet(TextStyle.Bold | TextStyle.Italic);

        Assert.True(mark.Has(TextStyle.Bold));
        Assert.True(mark.Has(TextStyle.Bold | TextStyle.Italic));
        Assert.False(mark.Has(TextStyle.Bold | TextStyle.Code));
        Assert.True(mark.Has(TextStyle.None));
    }

    [Fact]
    public void Apply_with_no_fields_changes_nothing()
    {
        var mark = new MarkSet(TextStyle.Bold, "Arial", 12);

        Assert.Equal(mark, mark.Apply(MarkSet.Empty, MarkFields.None));
    }

    [Fact]
    public void Apply_turns_a_style_on()
    {
        var result = MarkSet.Empty.Apply(new MarkSet(TextStyle.Bold), MarkFields.Bold);

        Assert.Equal(TextStyle.Bold, result.Style);
    }

    [Fact]
    public void Apply_turns_a_style_off_using_the_same_path()
    {
        var bold = new MarkSet(TextStyle.Bold | TextStyle.Italic);

        var result = bold.Apply(MarkSet.Empty, MarkFields.Bold);

        Assert.Equal(TextStyle.Italic, result.Style);
    }

    [Fact]
    public void Apply_leaves_unselected_styles_alone()
    {
        var mark = new MarkSet(TextStyle.Italic | TextStyle.Code);

        var result = mark.Apply(new MarkSet(TextStyle.Bold), MarkFields.Bold);

        Assert.Equal(TextStyle.Bold | TextStyle.Italic | TextStyle.Code, result.Style);
    }

    [Fact]
    public void Applying_super_over_sub_clears_sub()
    {
        // The selection mentions Super only, so a naive bitwise merge would produce the
        // Sub|Super combination the constructor refuses to accept.
        var sub = new MarkSet(TextStyle.Sub);

        var result = sub.Apply(new MarkSet(TextStyle.Super), MarkFields.Super);

        Assert.Equal(TextStyle.Super, result.Style);
    }

    [Fact]
    public void Applying_sub_over_super_clears_super()
    {
        var super = new MarkSet(TextStyle.Super);

        var result = super.Apply(new MarkSet(TextStyle.Sub), MarkFields.Sub);

        Assert.Equal(TextStyle.Sub, result.Style);
    }

    [Fact]
    public void Apply_replaces_only_the_selected_scalar()
    {
        var mark = new MarkSet(fontFamily: "Arial", fontSize: 12, foreground: Rgba.Black);
        var value = new MarkSet(fontFamily: "Georgia", fontSize: 20, foreground: Rgba.White);

        var result = mark.Apply(value, MarkFields.FontSize);

        Assert.Equal("Arial", result.FontFamily);
        Assert.Equal(20, result.FontSize);
        Assert.Equal(Rgba.Black, result.Foreground);
    }

    [Fact]
    public void A_selected_null_clears_where_an_unselected_field_inherits()
    {
        // The whole point of MarkFields: absent means "leave it", present-and-null means
        // "clear it". Collapsing the two would make removing a mark inexpressible.
        var mark = new MarkSet(fontFamily: "Arial", fontSize: 12);

        var cleared = mark.Apply(MarkSet.Empty, MarkFields.FontFamily);
        Assert.Null(cleared.FontFamily);
        Assert.Equal(12, cleared.FontSize);

        var untouched = mark.Apply(MarkSet.Empty, MarkFields.None);
        Assert.Equal("Arial", untouched.FontFamily);
    }

    [Fact]
    public void Remove_clears_the_selected_fields()
    {
        var mark = new MarkSet(
            TextStyle.Bold | TextStyle.Italic,
            "Arial",
            12,
            Rgba.Black,
            Rgba.White,
            new LinkMark("https://example.com"));

        var result = mark.Remove(MarkFields.Bold | MarkFields.FontSize | MarkFields.Link);

        Assert.Equal(TextStyle.Italic, result.Style);
        Assert.Equal("Arial", result.FontFamily);
        Assert.Null(result.FontSize);
        Assert.Null(result.Link);
        Assert.Equal(Rgba.Black, result.Foreground);
    }

    [Fact]
    public void Remove_everything_yields_the_empty_mark()
    {
        var mark = new MarkSet(
            TextStyle.All & ~TextStyle.Super,
            "Arial",
            12,
            Rgba.Black,
            Rgba.White,
            new LinkMark("https://example.com"));

        Assert.True(mark.Remove(MarkFields.All).IsEmpty);
    }

    [Fact]
    public void Apply_of_a_whole_mark_over_everything_replaces_it_wholesale()
    {
        var target = new MarkSet(TextStyle.Code, "Consolas", 11);
        var source = new MarkSet(TextStyle.Bold, "Georgia", 20, Rgba.White);

        Assert.Equal(source, target.Apply(source, MarkFields.All));
    }

    [Fact]
    public void DifferenceFrom_reports_exactly_the_fields_that_differ()
    {
        var a = new MarkSet(TextStyle.Bold, "Arial", 12);
        var b = new MarkSet(TextStyle.Bold | TextStyle.Italic, "Arial", 14);

        Assert.Equal(MarkFields.Italic | MarkFields.FontSize, a.DifferenceFrom(b));
        Assert.Equal(MarkFields.None, a.DifferenceFrom(a));
    }

    [Fact]
    public void DifferenceFrom_is_exactly_what_Apply_needs_to_transform_one_into_the_other()
    {
        var a = new MarkSet(TextStyle.Bold | TextStyle.Code, "Arial", 12, Rgba.Black);
        var b = new MarkSet(TextStyle.Italic, "Georgia", null, null, Rgba.White);

        Assert.Equal(b, a.Apply(b, a.DifferenceFrom(b)));
    }

    [Fact]
    public void Equality_is_structural_including_the_link()
    {
        var a = new MarkSet(link: new LinkMark("https://example.com", "Home"));
        var b = new MarkSet(link: new LinkMark("https://example.com", "Home"));
        var c = new MarkSet(link: new LinkMark("https://example.com", "Away"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void ToString_lists_only_what_is_overridden()
    {
        Assert.Equal("(empty)", MarkSet.Empty.ToString());
        Assert.Equal("Bold size=12", new MarkSet(TextStyle.Bold, fontSize: 12).ToString());
    }
}

public class MarkFieldsTests
{
    [Fact]
    public void Style_bits_line_up_with_TextStyle()
    {
        // The cast in ToStyle/ToFields is only sound while these agree, and the values are
        // written out separately in each enum.
        Assert.Equal((int)TextStyle.Bold, (int)MarkFields.Bold);
        Assert.Equal((int)TextStyle.Italic, (int)MarkFields.Italic);
        Assert.Equal((int)TextStyle.Underline, (int)MarkFields.Underline);
        Assert.Equal((int)TextStyle.Strikethrough, (int)MarkFields.Strikethrough);
        Assert.Equal((int)TextStyle.Sub, (int)MarkFields.Sub);
        Assert.Equal((int)TextStyle.Super, (int)MarkFields.Super);
        Assert.Equal((int)TextStyle.Code, (int)MarkFields.Code);
        Assert.Equal((int)TextStyle.All, (int)MarkFields.AllStyles);
    }

    [Fact]
    public void Scalar_selectors_do_not_collide_with_style_bits()
    {
        Assert.Equal(MarkFields.None, MarkFields.AllScalars & MarkFields.AllStyles);
        Assert.Equal(MarkFields.All, MarkFields.AllStyles | MarkFields.AllScalars);
    }

    [Fact]
    public void ToStyle_discards_scalar_selectors()
    {
        var fields = MarkFields.Bold | MarkFields.FontSize | MarkFields.Link;

        Assert.Equal(TextStyle.Bold, fields.ToStyle());
    }

    [Fact]
    public void ToFields_round_trips_through_ToStyle()
    {
        var style = TextStyle.Bold | TextStyle.Code;

        Assert.Equal(style, style.ToFields().ToStyle());
    }

    [Fact]
    public void Includes_requires_every_bit()
    {
        var fields = MarkFields.Bold | MarkFields.Italic;

        Assert.True(fields.Includes(MarkFields.Bold));
        Assert.True(fields.Includes(MarkFields.Bold | MarkFields.Italic));
        Assert.False(fields.Includes(MarkFields.Bold | MarkFields.FontSize));
    }
}

public class LinkMarkTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_href(string href)
    {
        Assert.Throws<ArgumentException>(() => new LinkMark(href));
    }

    [Fact]
    public void Rejects_a_null_href()
    {
        Assert.Throws<ArgumentNullException>(() => new LinkMark(null!));
    }

    [Fact]
    public void Blank_title_normalizes_to_null()
    {
        Assert.Null(new LinkMark("https://example.com", "  ").Title);
        Assert.Equal(new LinkMark("https://example.com"), new LinkMark("https://example.com", ""));
    }

    [Fact]
    public void Keeps_the_href_verbatim()
    {
        // Sanitizing here would mean a hostile document could not round-trip through
        // VellumText's own serializer. Refusal belongs in the importer.
        const string Href = "javascript:alert(1)";

        Assert.Equal(Href, new LinkMark(Href).Href);
    }

    [Fact]
    public void Equality_is_structural()
    {
        Assert.Equal(new LinkMark("https://a", "t"), new LinkMark("https://a", "t"));
        Assert.NotEqual(new LinkMark("https://a"), new LinkMark("https://b"));
        Assert.NotEqual(new LinkMark("https://a"), new LinkMark("https://a", "t"));
    }
}
