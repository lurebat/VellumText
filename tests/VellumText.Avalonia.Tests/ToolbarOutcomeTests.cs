using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media;
using global::Avalonia.Media.TextFormatting;
using VellumText;
using VellumText.Avalonia;
using static VellumText.Tests.Avalonia.ToolbarHarness;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Every action on the shipped toolbar, driven the way a user drives it and judged by what the
/// user would see.
/// </summary>
/// <remarks>
/// Each test clicks the real button (or picks from, or types into, the real box) with a realistic
/// selection — part of one paragraph, or across two with a third after them — and asserts the
/// outcome: the marks or attributes on exactly the selected text and nowhere else, the bar's state
/// afterwards, and for anything that moves text, the laid-out lines and runs (owner rule: "the
/// command was invoked" is not an outcome).
/// </remarks>
public class ToolbarOutcomeTests
{
    [AvaloniaTheory]
    [InlineData("Bold (Ctrl+B)", TextStyle.Bold, false)]
    [InlineData("Bold (Ctrl+B)", TextStyle.Bold, true)]
    [InlineData("Italic (Ctrl+I)", TextStyle.Italic, false)]
    [InlineData("Italic (Ctrl+I)", TextStyle.Italic, true)]
    [InlineData("Underline (Ctrl+U)", TextStyle.Underline, false)]
    [InlineData("Underline (Ctrl+U)", TextStyle.Underline, true)]
    [InlineData("Strikethrough (Ctrl+Shift+X)", TextStyle.Strikethrough, false)]
    [InlineData("Strikethrough (Ctrl+Shift+X)", TextStyle.Strikethrough, true)]
    [InlineData("Superscript (Ctrl+.)", TextStyle.Super, false)]
    [InlineData("Superscript (Ctrl+.)", TextStyle.Super, true)]
    [InlineData("Subscript (Ctrl+,)", TextStyle.Sub, false)]
    [InlineData("Subscript (Ctrl+,)", TextStyle.Sub, true)]
    public void AStyleButtonStylesExactlyTheSelectionAndPressedAgainTakesItOff(string tip, TextStyle style, bool across)
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;
        var selected = Select(view, across);
        var button = Toggle(bar, tip);

        Assert.False(button.IsChecked);

        UserClick(button);

        AssertMarkedExactly(editor.State.Doc, selected, marks => marks.Has(style), style.ToString());
        Assert.True(button.IsChecked);

        // On screen: the run drawn for the selection has the style, the text beside it does not.
        Assert.True(Drawn(RunAt(view, selected[0].Block, selected[0].From), style), $"the selection is not drawn {style}");
        Assert.All(Outside(across), at => Assert.False(Drawn(RunAt(view, at.Block, at.Offset), style), $"text outside the selection is drawn {style}"));
        AssertNoOverlappingLines(view);

        UserClick(button);

        AssertMarkedExactly(editor.State.Doc, [], marks => marks.Has(style), style.ToString());
        Assert.False(button.IsChecked);
        Assert.False(Drawn(RunAt(view, selected[0].Block, selected[0].From), style), $"the selection is still drawn {style}");
    }

    /// <summary>Selects part of a paragraph, or across two, and returns what was selected.</summary>
    private static (int Block, int From, int To)[] Select(RichTextView view, bool across)
    {
        if (across)
        {
            SelectAcross(view);

            return Across;
        }

        SelectPart(view);

        return Part;
    }

    /// <summary>Characters next to the selection, which an edit to the selection must not reach.</summary>
    private static (int Block, int Offset)[] Outside(bool across) =>
        across ? [(0, 0), (1, 7), (2, 0)] : [(0, 6), (1, 0)];

    /// <summary>Whether a laid-out run is drawn with a style.</summary>
    private static bool Drawn(TextRunProperties run, TextStyle style) => style switch
    {
        TextStyle.Bold => run.Typeface.Weight == FontWeight.Bold,
        TextStyle.Italic => run.Typeface.Style == FontStyle.Italic,
        TextStyle.Underline => Decorated(run, TextDecorationLocation.Underline),
        TextStyle.Strikethrough => Decorated(run, TextDecorationLocation.Strikethrough),
        TextStyle.Super => run.BaselineAlignment == BaselineAlignment.Superscript,
        TextStyle.Sub => run.BaselineAlignment == BaselineAlignment.Subscript,
        _ => throw new ArgumentOutOfRangeException(nameof(style), style, null),
    };

    private static bool Decorated(TextRunProperties run, TextDecorationLocation where) =>
        run.TextDecorations?.Any(d => d.Location == where) == true;
}
