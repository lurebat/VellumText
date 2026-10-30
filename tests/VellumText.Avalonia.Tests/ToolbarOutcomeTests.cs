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

    /// <summary>
    /// In a list, Decrease indent lifts the item out of the list it is nested in. It used to
    /// lower the paragraph's own indent level, which inside a list was already zero, so the click
    /// did nothing visible and the item stayed nested (found live: "two" nested under "one").
    /// </summary>
    [AvaloniaFact]
    public void DecreaseIndentInANestedItemLiftsIt()
    {
        var bar = Templated(out var editor, new DocumentNode([
            new ListNode(
                [new ListItemNode([ParagraphNode.FromText("one"), new ListNode([ListItemNode.FromText("two")], ListKind.Unordered)])],
                ListKind.Unordered),
            ParagraphNode.FromText("after"),
        ]));
        var view = editor.View!;
        var nestedAt = Bullet(view, 1);

        view.MoveTo(view.Slots[1].Start + 1, extend: false);

        UserClick(bar, "Decrease indent");

        // One flat list: "one" and "two" side by side, nothing nested, no stray indent.
        var list = Assert.IsType<ListNode>(editor.State.Doc.Blocks[0]);

        Assert.Equal(["one", "two"], list.Items.Select(item => Assert.IsType<ParagraphNode>(Assert.Single(item.Blocks)).Content.Text));
        Assert.All(Paragraphs(editor.State.Doc), p => Assert.Equal(0, p.IndentLevel));
        Assert.Equal(ListKind.Unordered, bar.ListKind);

        // On screen: the bullet moved left, to where the first item's is.
        Assert.True(Bullet(view, 1) < nestedAt, $"the bullet stayed at {Bullet(view, 1)}, nested at {nestedAt}");
        Assert.Equal(Bullet(view, 0), Bullet(view, 1), 3);
        AssertNoOverlappingLines(view);
    }

    /// <summary>
    /// In a list, Increase indent nests the item under the one before it. It used to raise the
    /// paragraph's own indent level: the bullet moved right without the item becoming a sub-item —
    /// the misleading bullet Tab's remarks rule out.
    /// </summary>
    [AvaloniaFact]
    public void IncreaseIndentOnASecondItemNestsItUnderTheFirst()
    {
        var bar = Templated(out var editor, new DocumentNode([
            new ListNode([ListItemNode.FromText("one"), ListItemNode.FromText("two")], ListKind.Unordered),
            ParagraphNode.FromText("after"),
        ]));
        var view = editor.View!;
        var flatAt = Bullet(view, 1);

        view.MoveTo(view.Slots[1].Start + 1, extend: false);

        UserClick(bar, "Increase indent");

        // "two" is now the one item of a list inside "one"'s item.
        var item = Assert.Single(Assert.IsType<ListNode>(editor.State.Doc.Blocks[0]).Items);

        Assert.Equal("one", Assert.IsType<ParagraphNode>(item.Blocks[0]).Content.Text);
        Assert.Equal("two", Assert.IsType<ParagraphNode>(Assert.Single(Assert.IsType<ListNode>(item.Blocks[1]).Items).Blocks[0]).Content.Text);
        Assert.All(Paragraphs(editor.State.Doc), p => Assert.Equal(0, p.IndentLevel));
        Assert.IsType<ParagraphNode>(editor.State.Doc.Blocks[1]);

        // On screen: the bullet moved right by one nesting step.
        Assert.Equal(flatAt + ParagraphView.DefaultIndentStep, Bullet(view, 1), 3);
        Assert.Equal("\u2022", ((ParagraphView)view.Blocks[1]).MarkerText);
        AssertNoOverlappingLines(view);

        // And back: Decrease indent lifts it again rather than leaving it nested.
        UserClick(bar, "Decrease indent");

        Assert.Equal(2, Assert.IsType<ListNode>(editor.State.Doc.Blocks[0]).Items.Length);
        Assert.Equal(flatAt, Bullet(view, 1), 3);
    }

    /// <summary>
    /// Where a list item's text starts, which its bullet is drawn a fixed distance in front of:
    /// when this moves, the bullet moves with it.
    /// </summary>
    private static double Bullet(RichTextView view, int block)
    {
        var paragraph = Assert.IsType<ParagraphView>(view.Blocks[block]);

        Assert.NotNull(paragraph.MarkerText);

        return paragraph.GetCaretRect(0).X;
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
