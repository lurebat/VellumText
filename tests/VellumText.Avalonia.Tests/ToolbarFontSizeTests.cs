using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The toolbar's font size is in points, like Word's, while the model's is in device-independent
/// pixels (owner decision for unit 3.10: convert).
/// </summary>
/// <remarks>
/// The box, its list and <see cref="RichTextToolbar.SetFontSizeCommand"/> all said points, and the
/// number went to the model unconverted. Picking 12 gave 9-point text, and a 20-point heading
/// pasted from Word — 26⅔ DIP in the model — showed as "26.7".
/// </remarks>
public class ToolbarFontSizeTests
{
    private static (RichTextEditor Editor, RichTextToolbar Toolbar) Shown(DocumentNode doc)
    {
        var editor = new RichTextEditor { State = EditorState.Create(doc) };
        var toolbar = new RichTextToolbar { Editor = editor };
        var window = new Window { Width = 600, Height = 300, Content = new StackPanel { Children = { toolbar, editor } } };

        window.Show();
        editor.EnsureView().SelectAll();

        return (editor, toolbar);
    }

    private static double? SizeOf(RichTextEditor editor) =>
        editor.Document.Blocks.OfType<ParagraphNode>().First().Content.MarkAt(0).FontSize;

    [AvaloniaFact]
    public void A_pasted_twenty_point_heading_shows_twenty()
    {
        var doc = RtfImporter.Import(@"{\rtf1\ansi {\fs40 Heading}\par}").Doc;
        var (editor, toolbar) = Shown(doc);

        Assert.Equal(80.0 / 3, SizeOf(editor)!.Value, 6);
        Assert.Equal(20, toolbar.SelectionFontSize);
        Assert.Equal("20", ToolbarConverters.Points.Convert(toolbar.SelectionFontSize, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [AvaloniaTheory]
    [InlineData(12.0, 16.0)]
    [InlineData("12", 16.0)]
    [InlineData(9, 12.0)]
    [InlineData(72.0, 96.0)]
    public void Picking_a_point_size_sets_its_size_in_pixels(object picked, double pixels)
    {
        var (editor, toolbar) = Shown(DocumentNode.FromParagraphs("text"));

        toolbar.SetFontSizeCommand.Execute(picked);

        Assert.Equal(pixels, SizeOf(editor)!.Value, 6);
        Assert.Equal(Convert.ToDouble(picked, CultureInfo.InvariantCulture), toolbar.SelectionFontSize!.Value, 6);
    }

    [AvaloniaFact]
    public void Clearing_the_size_still_clears_it()
    {
        var (editor, toolbar) = Shown(DocumentNode.FromParagraphs("text"));

        toolbar.SetFontSizeCommand.Execute(12.0);
        toolbar.SetFontSizeCommand.Execute(null);

        Assert.Null(SizeOf(editor));
        Assert.Null(toolbar.SelectionFontSize);
    }
}
