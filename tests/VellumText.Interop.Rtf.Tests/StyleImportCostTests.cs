using System.Text.RegularExpressions;
using VellumText.Interop.Html;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Interop.Rtf;

/// <summary>
/// Importing styled HTML costs about what unstyled HTML does (F-16, audit of 0282015; plan 7.6).
/// </summary>
/// <remarks>
/// <para>
/// An RTF reader writes a <c>style</c> attribute on every element of a Word document, from a
/// handful of distinct values (RtfPipe did; the reader that replaced it in phase 8 does too).
/// Every one was parsed while the DOM was built and parsed again by the sanitizer: 10,000
/// paragraphs allocated 850 MB, against 35 MB for the same markup with its
/// styles stripped, and a 1 MB RTF took 6.7 s on the UI thread.
/// </para>
/// <para>
/// Measured in bytes allocated on this thread through the import stage itself, which a shared
/// machine cannot disturb. The time bound in the plan (within 2× of the stripped control) is a
/// quiet-machine measurement and is not asserted here.
/// </para>
/// </remarks>
public class StyleImportCostTests
{
    private static string ReaderHtml()
    {
        var blocks = Enumerable.Range(0, 10_000)
            .Select(i => (BlockNode)new ParagraphNode(InlineContent.Create(
                $"Paragraph {i} with some words in it",
                [new ValueSpan<MarkSet>(0, 9, new MarkSet { FontFamily = "Calibri", FontSize = 14.67, Foreground = Rgba.FromRgb(31, 56, 100) })],
                [])))
            .ToList();

        return RtfImporter.ReadToHtml(RtfExporter.Instance.Export(new DocumentNode(blocks)));
    }

    private static long Allocated(string html)
    {
        HtmlImporter.ImportOnCurrentStack(html, null);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var result = HtmlImporter.ImportOnCurrentStack(html, null);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(10_000, result.Doc.Blocks.Length);

        return allocated;
    }

    [Fact]
    public void A_style_on_every_element_costs_a_parse_per_distinct_value_not_per_element()
    {
        var styled = ReaderHtml();
        var stripped = Regex.Replace(styled, " style=\"[^\"]*\"", string.Empty);

        var withStyles = Allocated(styled);
        var withoutStyles = Allocated(stripped);

        Assert.True(withStyles < 200L * 1024 * 1024, $"styled import allocated {withStyles / 1048576} MB");
        Assert.True(
            withStyles < 4 * withoutStyles,
            $"styled {withStyles / 1048576} MB against {withoutStyles / 1048576} MB without styles");
    }
}