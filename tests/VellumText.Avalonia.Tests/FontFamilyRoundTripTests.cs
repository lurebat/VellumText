using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.VisualTree;
using VellumText;
using VellumText.Avalonia;
using VellumText.Interop.Html;
using static VellumText.Tests.Avalonia.ToolbarHarness;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A family name holding a comma, end to end: pasted, shown in the font box, given to more text
/// through the box, and copied out again (owner decision on review finding 5.3).
/// </summary>
/// <remarks>
/// The importer stores <c>ACME, Inc.</c> quoted so an export reads it as one family; the box shows
/// it without the quotes; what the user commits through the box must be the same stored family, or
/// text given "the same" font would export as two families, or as a different one.
/// </remarks>
public class FontFamilyRoundTripTests
{
    [AvaloniaFact]
    public async Task ACommaFamilyShowsBareIsStoredQuotedThroughTheBoxAndExportsAsOneFamily()
    {
        var pasted = (await HtmlImporter.ImportAsync(
            "<p><span style='font-family:\"ACME, Inc.\", Arial'>pasted</span> and typed</p>")).Doc;
        var bar = Templated(out var editor, pasted);
        var view = editor.View!;
        var box = Box(bar, "PART_FontBox");
        var start = view.Slots[0].Start;

        // In the pasted word the stored form is quoted and the box shows the name bare.
        view.MoveTo(start + 1, extend: false);
        view.MoveTo(start + 5, extend: true);
        Layout(WindowOf(box));
        Assert.Equal("\"ACME, Inc.\"", bar.SelectionFontFamily);
        Assert.Equal("ACME, Inc.", box.Text);

        // Given the same name through the box, the typed words get the same stored family.
        view.MoveTo(start + 7, extend: false);
        view.MoveTo(start + 16, extend: true);
        Layout(WindowOf(box));
        box.GetVisualDescendants().OfType<TextBox>().First().Focus();
        TypeAndEnter(box, "ACME, Inc.");

        (int Block, int From, int To)[] named = [(0, 0, 6), (0, 7, 16)];
        AssertMarkedExactly(editor.State.Doc, named, marks => marks.FontFamily == "\"ACME, Inc.\"", "\"ACME, Inc.\"");

        // Copied out and read back, both runs are still the one family — not ACME, then Inc.
        var html = HtmlExporter.Instance.Export(editor.State.Doc);
        var back = (await HtmlImporter.ImportAsync(html)).Doc;

        AssertMarkedExactly(back, named, marks => marks.FontFamily == "\"ACME, Inc.\"", "\"ACME, Inc.\" after a round trip");
    }
}
