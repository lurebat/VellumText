using System.Diagnostics;

using AngleSharp.Html.Parser;

namespace VellumText.Interop.Html.Tests;

/// <summary>
/// The shapes of HTML whose parse cost grows faster than their size, and the limits that bound
/// them before the parser runs.
/// </summary>
public sealed class ParseCostGuardTests
{
    private static string NestedDivs(int count) => string.Concat(Enumerable.Repeat("<div>", count)) + "x";

    private static string ManyAttributes(int count) =>
        "<p " + string.Join(" ", Enumerable.Range(0, count).Select(i => $"a{i}=1")) + ">x</p>";

    private static long BestOfThreeMs(Func<ImportResult> import)
    {
        import();

        var best = long.MaxValue;

        for (var i = 0; i < 3; i++)
        {
            var watch = Stopwatch.StartNew();
            import();
            best = Math.Min(best, watch.ElapsedMilliseconds);
        }

        return best;
    }

    private static string TextOf(DocumentNode doc) => DocumentText.Of(doc);

    /// <summary>
    /// Nesting is quadratic in AngleSharp's tree builder: 50 KB of nested <c>div</c> took 0.3 s
    /// and 200 KB took 7 s, a factor of 18 for four times the input.
    /// </summary>
    /// <remarks>
    /// The bound is a ratio rather than a time so that a slow machine does not fail it: linear
    /// growth is a factor of about 4, quadratic about 16, and 8 sits between them with room for
    /// noise on both sides.
    /// </remarks>
    [Fact]
    public void Nesting_cost_grows_linearly_with_the_input()
    {
        var small = NestedDivs(10_000);
        var large = NestedDivs(40_000);

        var smallMs = Math.Max(1, BestOfThreeMs(() => HtmlImporter.Import(small)));
        var largeMs = BestOfThreeMs(() => HtmlImporter.Import(large));

        Assert.True(
            largeMs < 8 * smallMs + 50,
            $"50 KB took {smallMs} ms and 200 KB took {largeMs} ms, a factor of {largeMs / (double)smallMs:F1}");
    }

    /// <summary>One element with 40,000 attributes (349 KB) took twelve seconds to parse.</summary>
    [Fact]
    public void Many_attributes_on_one_element_import_quickly()
    {
        var html = ManyAttributes(40_000);

        var ms = BestOfThreeMs(() => HtmlImporter.Import(html));

        Assert.True(ms < 2_000, $"349 KB of attributes took {ms} ms");
        Assert.Equal("x", TextOf(HtmlImporter.Import(html).Doc));
    }

    [Fact]
    public void Text_below_the_nesting_limit_is_kept_in_order()
    {
        var html = string.Concat(Enumerable.Range(0, 700).Select(i => $"<div>{i % 10}"));

        var result = HtmlImporter.Import(html);

        Assert.Equal(
            string.Concat(Enumerable.Range(0, 700).Select(i => (i % 10).ToString())),
            TextOf(result.Doc).Replace("\n", string.Empty, StringComparison.Ordinal));
        Assert.Contains(result.Diagnostics, d => d.Context == "element-depth");
    }

    [Fact]
    public void Element_depth_limit_is_honoured()
    {
        var options = HtmlImportOptions.Default with { MaxElementDepth = 3 };
        var html = "<div><div><div><div><div><b>deep</b></div></div></div></div></div><p>after</p>";

        var guarded = HtmlInputGuard.Bound(html, options);

        Assert.True(guarded.DroppedDeepTags);
        Assert.Equal(3, MaxDepthOf(guarded.Html));

        var result = HtmlImporter.Import(html, options);

        Assert.Equal("deep\nafter", TextOf(result.Doc));
        Assert.Contains(result.Diagnostics, d => d.Context == "element-depth" && d.Severity == DiagnosticSeverity.Downgraded);
    }

    [Fact]
    public void The_default_depth_limit_bounds_what_the_parser_sees()
    {
        var guarded = HtmlInputGuard.Bound(NestedDivs(5_000), HtmlImportOptions.Default);

        Assert.Equal(512, MaxDepthOf(guarded.Html));
    }

    [Fact]
    public void Attribute_limit_is_honoured()
    {
        var html = "<p><a title=\"t\" href=\"https://example.test/\">link</a></p>";

        var unlimited = HtmlImporter.Import(html);
        var limited = HtmlImporter.Import(html, HtmlImportOptions.Default with { MaxAttributesPerElement = 1 });

        Assert.NotNull(LinkOf(unlimited.Doc));
        Assert.Null(LinkOf(limited.Doc));
        Assert.Contains(limited.Diagnostics, d => d.Context == "attributes" && d.Severity == DiagnosticSeverity.Downgraded);
        Assert.DoesNotContain(unlimited.Diagnostics, d => d.Context == "attributes");
    }

    [Fact]
    public void Style_nesting_limit_is_honoured()
    {
        var html = "<p><span style=\"color:#ff0000; width:calc(((1px)))\">red</span></p>";

        var unlimited = HtmlImporter.Import(html);
        var limited = HtmlImporter.Import(html, HtmlImportOptions.Default with { MaxStyleNesting = 2 });

        Assert.Equal(new Rgba(255, 0, 0, 255), ForegroundOf(unlimited.Doc));
        Assert.Null(ForegroundOf(limited.Doc));
        Assert.Equal("red", TextOf(limited.Doc));
        Assert.Contains(limited.Diagnostics, d => d.Context == "style-nesting" && d.Severity == DiagnosticSeverity.Downgraded);
    }

    [Fact]
    public void A_style_removed_for_nesting_keeps_the_tags_other_attributes()
    {
        var html = "<p><a href=\"https://example.test/?a=1&amp;b=&quot;2&quot;\" title=\"T&lt;\" style=\"width:calc(((1px)))\">x</a></p>";
        var withoutStyle = html.Replace(" style=\"width:calc(((1px)))\"", string.Empty, StringComparison.Ordinal);

        var result = HtmlImporter.Import(html, HtmlImportOptions.Default with { MaxStyleNesting = 2 });

        Assert.Equal("https://example.test/?a=1&b=\"2\"", LinkOf(result.Doc)?.Href);
        Assert.Equal(LinkOf(HtmlImporter.Import(withoutStyle).Doc), LinkOf(result.Doc));
    }

    /// <summary>
    /// Dropping only the tags of a non-content element would release its body into the document.
    /// </summary>
    [Theory]
    [InlineData("<template><p>hidden</p></template>")]
    [InlineData("<noscript><p>hidden</p></noscript>")]
    [InlineData("<script>hidden</script>")]
    [InlineData("<style>hidden</style>")]
    [InlineData("<template><template>x</template><p>hidden</p></template>")]
    public void Non_content_past_the_depth_limit_does_not_become_text(string body)
    {
        var options = HtmlImportOptions.Default with { MaxElementDepth = 2 };
        var html = "<div><div><div>" + body + "</div></div></div><p>after</p>";

        var result = HtmlImporter.Import(html, options);

        Assert.DoesNotContain("hidden", TextOf(result.Doc), StringComparison.Ordinal);
        Assert.Contains("after", TextOf(result.Doc), StringComparison.Ordinal);
    }

    /// <summary>
    /// Inside a <c>select</c> the tree builder ignores a start tag such as <c>plaintext</c>, or
    /// makes it an ordinary element, and applies none of the implied closes it applies elsewhere.
    /// Measuring the markup as if it were outside one let the guard stop reading tags at a
    /// <c>plaintext</c> the parser never honoured, or close elements the parser kept open, while
    /// the parser went on nesting.
    /// </summary>
    [Theory]
    [InlineData("<select><plaintext></select>", "<div>")]
    [InlineData("<p><select><div><plaintext></select>", "<div>")]
    [InlineData("<select><option><b><i></b></select><plaintext>", "<div>")]
    [InlineData("<select><option><style>", "<div>")]
    [InlineData("<select><option><div><textarea>", "<div>")]
    [InlineData("<select><option>", "<p>")]
    [InlineData("<select><option>", "<br>")]
    [InlineData("<select><option>", "<h1><h2>")]
    public void Markup_inside_a_select_is_measured_as_the_parser_builds_it(string prefix, string unit)
    {
        var options = HtmlImportOptions.Default with { MaxElementDepth = 4 };
        var html = prefix + string.Concat(Enumerable.Repeat(unit, 12)) + "x";

        var guarded = HtmlInputGuard.Bound(html, options);

        Assert.InRange(MaxDepthOf(guarded.Html), 0, 4);
        Assert.True(guarded.DroppedDeepTags);
    }

    /// <summary>
    /// What a browser writes for a <c>select</c> closes every element in it, so the markup after
    /// one is measured exactly as before.
    /// </summary>
    [Fact]
    public void A_closed_select_does_not_change_how_what_follows_is_measured()
    {
        var html = "<select><option>a</option><optgroup label=\"g\"><option>b</option></optgroup></select>"
            + string.Concat(Enumerable.Range(0, 600).Select(i => $"<p>{i}"))
            + "<style>p{}</style><textarea><div></textarea>";

        var guarded = HtmlInputGuard.Bound(html, HtmlImportOptions.Default);

        Assert.False(guarded.DroppedDeepTags);
        Assert.Same(html, guarded.Html);
    }

    /// <summary>
    /// Optional end tags are closed by the next sibling, so long unclosed lists, paragraphs and
    /// rows are flat and must not be mistaken for deep nesting.
    /// </summary>
    [Fact]
    public void Unclosed_list_items_paragraphs_and_cells_are_not_counted_as_nesting()
    {
        var list = "<ul>" + string.Concat(Enumerable.Range(0, 600).Select(i => $"<li>{i}")) + "</ul>";
        var paragraphs = string.Concat(Enumerable.Range(0, 600).Select(i => $"<p>{i}"));
        var table = "<table>" + string.Concat(Enumerable.Range(0, 600).Select(i => $"<tr><td>{i}<td>x")) + "</table>";
        var definitions = "<dl>" + string.Concat(Enumerable.Range(0, 600).Select(i => $"<dt>{i}<dd>x")) + "</dl>";

        foreach (var html in new[] { list, paragraphs, table, definitions })
        {
            var guarded = HtmlInputGuard.Bound(html, HtmlImportOptions.Default);

            Assert.False(guarded.DroppedDeepTags, html[..20]);
            Assert.Same(html, guarded.Html);
        }

        var imported = HtmlImporter.Import(list).Doc;
        Assert.Equal(600, Assert.IsType<ListNode>(Assert.Single(imported.Blocks)).Items.Length);
    }

    [Fact]
    public void Markup_within_every_limit_is_passed_through_untouched()
    {
        var html = "<!DOCTYPE html><html><body><p class=\"a\" style=\"color:red\">x &amp; y<br/>z</p>"
            + "<script>if (a<b) {}</script><svg><circle/></svg><table><tr><td>1</td></tr></table></body></html>";

        var guarded = HtmlInputGuard.Bound(html, HtmlImportOptions.Default);

        Assert.Same(html, guarded.Html);
        Assert.False(guarded.DroppedDeepTags || guarded.DroppedAttributes || guarded.DroppedStyles);
    }

    [Theory]
    [InlineData("color:red", 0)]
    [InlineData("width:calc(1px + 2px)", 1)]
    [InlineData("width:calc((1px + (2px)))", 3)]
    [InlineData("a:b)))(", 1)]
    public void Bracket_depth_is_the_deepest_nesting(string css, int expected) =>
        Assert.Equal(expected, HtmlInputGuard.BracketDepth(css));

    private static int MaxDepthOf(string html)
    {
        var body = new HtmlParser().ParseDocument(html).Body!;
        var deepest = 0;
        var pending = new Stack<(AngleSharp.Dom.IElement Element, int Depth)>();

        foreach (var child in body.Children)
        {
            pending.Push((child, 1));
        }

        while (pending.Count > 0)
        {
            var (element, depth) = pending.Pop();
            deepest = Math.Max(deepest, depth);

            foreach (var child in element.Children)
            {
                pending.Push((child, depth + 1));
            }
        }

        return deepest;
    }

    private static LinkMark? LinkOf(DocumentNode doc) =>
        doc.Blocks.OfType<ParagraphNode>()
            .SelectMany(p => Enumerable.Range(0, p.Content.Length).Select(i => p.Content.MarkAt(i).Link))
            .FirstOrDefault(link => link is not null);

    private static Rgba? ForegroundOf(DocumentNode doc) =>
        doc.Blocks.OfType<ParagraphNode>()
            .SelectMany(p => Enumerable.Range(0, p.Content.Length).Select(i => p.Content.MarkAt(i).Foreground))
            .FirstOrDefault(color => color is not null);
}
