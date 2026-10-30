using System.Globalization;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media;
using global::Avalonia.Media.TextFormatting;
using global::Avalonia.VisualTree;
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

    /// <summary>
    /// An alignment button aligns exactly the paragraphs the selection touches, and stays lit when
    /// it is clicked again. A ToggleButton flips itself before it runs its command; when the
    /// command then changed nothing — the paragraphs already had that alignment — no state
    /// property changed, the binding never put the button back, and it showed unlit over centred
    /// text.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Align left", TextAlign.Left)]
    [InlineData("Centre", TextAlign.Center)]
    [InlineData("Align right", TextAlign.Right)]
    [InlineData("Justify", TextAlign.Justify)]
    public void AnAlignmentButtonAlignsTheSelectedParagraphsAndStaysLit(string tip, TextAlign align)
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;

        SelectAcross(view);

        var button = Toggle(bar, tip);

        UserClick(button);
        UserClick(button);

        var paragraphs = Paragraphs(editor.State.Doc);

        Assert.Equal([align, align, TextAlign.Default], paragraphs.Select(p => p.Align));
        Assert.Equal(align, bar.Align);
        Assert.True(button.IsChecked, $"{tip} went dark on a second click; the paragraphs are still {align}");
        Assert.All(
            new[] { "Align left", "Centre", "Align right", "Justify" }.Where(other => other != tip),
            other => Assert.False(Toggle(bar, other).IsChecked, $"{other} is lit too"));

        // On screen: where the selected paragraphs' lines sit, and the third's left alone.
        for (var block = 0; block < 2; block++)
        {
            var layout = ((ParagraphView)view.Blocks[block]).Layout;
            var last = layout.TextLines[^1];

            switch (align)
            {
                case TextAlign.Left:
                    Assert.Equal(0, last.Start, 3);
                    break;
                case TextAlign.Center:
                    Assert.Equal((layout.MaxWidth - last.Width) / 2, last.Start, 1);
                    break;
                case TextAlign.Right:
                    Assert.Equal(layout.MaxWidth, last.Start + last.Width, 1);
                    break;
            }
        }

        if (align == TextAlign.Justify)
        {
            // Every line but the last fills the width; the last is left as it is, as in print.
            var lines = ((ParagraphView)view.Blocks[0]).Layout.TextLines;

            Assert.True(lines.Count > 1, "the first paragraph was meant to wrap");
            Assert.All(lines.Take(lines.Count - 1), line => Assert.True(
                line.WidthIncludingTrailingWhitespace >= ((ParagraphView)view.Blocks[0]).Layout.MaxWidth - 1,
                $"a justified line is {line.WidthIncludingTrailingWhitespace} wide"));
        }

        Assert.Equal(0, ((ParagraphView)view.Blocks[2]).Layout.TextLines[0].Start, 3);
        AssertNoOverlappingLines(view);
    }

    /// <summary>Body is a state, not a toggle: clicking it on body text leaves it body, and lit.</summary>
    [AvaloniaFact]
    public void ClickingBodyOnBodyTextLeavesItLit()
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var before = editor.State.Doc;
        var body = Toggle(bar, "Body text");

        SelectAcross(editor.View!);

        Assert.True(body.IsChecked);

        UserClick(body);

        Assert.Same(before, editor.State.Doc);
        Assert.True(body.IsChecked, "Body went dark over body text");
    }

    /// <summary>
    /// The Link button asks the host for the address, as Ctrl+K does, and the link the host
    /// applies lands on exactly the selection; clicked again, it removes it. The default template
    /// gives the button no address, and with none the command did nothing at all — while the
    /// button lit up as though it had made a link.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLinkButtonAsksForTheAddressAndLinksExactlyTheSelection(bool across)
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;
        var link = new LinkMark("https://example.com/");
        var asked = 0;

        view.LinkRequested += (_, e) =>
        {
            asked++;
            Assert.Null(e.Existing);
            view.ApplyLink(link);
        };

        var selected = Select(view, across);
        var button = Toggle(bar, "Link");

        UserClick(button);

        Assert.Equal(1, asked);
        AssertMarkedExactly(editor.State.Doc, selected, marks => marks.Link == link, "the link");
        Assert.True(bar.IsLink);
        Assert.True(button.IsChecked);

        // On screen: a link is drawn underlined, and the text beside it is not.
        Assert.True(Drawn(RunAt(view, selected[0].Block, selected[0].From), TextStyle.Underline), "the link is not drawn as one");
        Assert.All(Outside(across), at => Assert.False(Drawn(RunAt(view, at.Block, at.Offset), TextStyle.Underline)));

        UserClick(button);

        Assert.Equal(1, asked);
        AssertMarkedExactly(editor.State.Doc, [], marks => marks.Link is not null, "a link");
        Assert.False(bar.IsLink);
        Assert.False(button.IsChecked);
    }

    /// <summary>A host that asks and is told no leaves the text as it was and the button dark.</summary>
    [AvaloniaFact]
    public void ALinkTheHostDoesNotMakeLeavesTheButtonDark()
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;
        var asked = 0;

        view.LinkRequested += (_, _) => asked++;
        SelectPart(view);

        var before = editor.State.Doc;
        var button = Toggle(bar, "Link");

        UserClick(button);

        Assert.Equal(1, asked);
        Assert.Same(before, editor.State.Doc);
        Assert.False(button.IsChecked, "the Link button lit with no link made");
    }

    /// <summary>
    /// A size picked from the list, or typed and committed with Enter, lands on exactly the
    /// selection, in points; the box says so afterwards; and the laid-out lines make room for it.
    /// Found live: a large size overlapped the lines below it while every test passed, because
    /// they read the document and never the layout.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(48.0, false)]
    [InlineData(72.0, false)]
    [InlineData(48.0, true)]
    [InlineData(9.5, true)]
    public void TheSizeBoxSizesExactlyTheSelectionAndTheLinesMakeRoomForIt(double points, bool typed)
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;
        var box = Box(bar, "PART_SizeBox");
        var text = points.ToString("0.#", CultureInfo.InvariantCulture);
        var pixels = points * 96 / 72;

        SelectAcross(view);
        Commit(box, typed ? text : points);

        AssertMarkedExactly(editor.State.Doc, Across, marks => marks.FontSize is { } size && Math.Abs(size - pixels) < 1e-6, $"{points}pt");
        Assert.Equal(points, bar.SelectionFontSize);
        Assert.Equal(text, box.Text);

        // On screen: drawn at that size, the text beside it at its own, and no line over another.
        Assert.Equal(pixels, RunAt(view, 0, 6).FontRenderingEmSize, 3);
        Assert.Equal(pixels, RunAt(view, 1, 0).FontRenderingEmSize, 3);
        Assert.NotEqual(pixels, RunAt(view, 0, 0).FontRenderingEmSize, 3);
        Assert.NotEqual(pixels, RunAt(view, 1, 7).FontRenderingEmSize, 3);
        Assert.True(((ParagraphView)view.Blocks[0]).Layout.TextLines.Count > 1, "the first paragraph was meant to wrap");
        AssertNoOverlappingLines(view);
    }

    /// <summary>
    /// Picks an item from a box's open list the way a click on it does, or types into the box and
    /// presses Enter, which is the box's own key binding. A string is typed; a number or a
    /// <see cref="PickedItem"/> is picked.
    /// </summary>
    private static void Commit(ComboBox box, object value)
    {
        if (value is string typed)
        {
            box.GetVisualDescendants().OfType<TextBox>().First().Focus();
            TypeAndEnter(box, typed);

            return;
        }

        box.IsDropDownOpen = true;
        box.SelectedItem = value is PickedItem picked ? picked.Value : value;
        box.IsDropDownOpen = false;
        Layout(WindowOf(box));
    }

    /// <summary>
    /// A family picked from the list, or typed and committed with Enter, lands on exactly the
    /// selection, the box shows it, and the selection is drawn in it.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFontBoxSetsExactlyTheSelectionsFamily(bool typed)
    {
        const string family = "Courier New";
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;
        var box = Box(bar, "PART_FontBox");

        // A headless platform may report no installed fonts; the pick is what is under test.
        if (!typed && !box.Items.Contains(family))
        {
            box.ItemsSource = new[] { "Arial", family };
        }

        SelectAcross(view);
        Commit(box, typed ? family : (object)new PickedItem(family));

        AssertMarkedExactly(editor.State.Doc, Across, marks => marks.FontFamily == family, family);
        Assert.Equal(family, bar.SelectionFontFamily);
        Assert.Equal(family, box.Text);

        Assert.Equal(family, RunAt(view, 0, 6).Typeface.FontFamily.Name);
        Assert.Equal(family, RunAt(view, 1, 0).Typeface.FontFamily.Name);
        Assert.NotEqual(family, RunAt(view, 0, 0).Typeface.FontFamily.Name);
        Assert.NotEqual(family, RunAt(view, 1, 7).Typeface.FontFamily.Name);
        AssertNoOverlappingLines(view);
    }

    /// <summary>A string to pick from a box's list rather than to type into it.</summary>
    private sealed record PickedItem(string Value);

    /// <summary>
    /// A colour or highlight picked with a real click across two paragraphs lands on exactly the
    /// selection and is drawn there, the button's swatch shows it, and "No colour" / "No
    /// highlight" clicked the same way takes it off again, on screen too.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Text colour")]
    [InlineData("Highlight")]
    public void AColourAcrossTwoParagraphsColoursExactlyThemAndTheClearTakesItOff(string tip)
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;
        var foreground = tip == "Text colour";
        Func<MarkSet, Rgba?> colourOf = marks => foreground ? marks.Foreground : marks.Highlight;

        SelectAcross(view);

        var swatch = Swatches(bar, tip)[8];
        var colour = Color.Parse((string)swatch.CommandParameter!);

        UserClick(swatch);

        AssertMarkedExactly(editor.State.Doc, Across, marks => colourOf(marks) == colour.ToVellumText(), tip);
        Assert.Equal(colour.ToVellumText(), foreground ? bar.SelectionForeground : bar.SelectionHighlight);
        Assert.Equal(colour, Assert.IsAssignableFrom<ISolidColorBrush>(ButtonSwatch(bar, tip).Background).Color);

        Assert.Equal(colour, DrawnColour(RunAt(view, 0, 6), foreground));
        Assert.Equal(colour, DrawnColour(RunAt(view, 1, 0), foreground));
        Assert.NotEqual(colour, DrawnColour(RunAt(view, 0, 0), foreground));
        Assert.NotEqual(colour, DrawnColour(RunAt(view, 1, 7), foreground));

        UserClick(ClearColour(bar, tip));

        AssertMarkedExactly(editor.State.Doc, [], marks => colourOf(marks) is not null, tip);
        Assert.Null(foreground ? bar.SelectionForeground : bar.SelectionHighlight);
        Assert.NotEqual(colour, DrawnColour(RunAt(view, 0, 6), foreground));
        Assert.NotEqual(colour, DrawnColour(RunAt(view, 1, 0), foreground));
        Assert.False(((Flyout)Find(bar, tip)!.Flyout!).IsOpen);
    }

    /// <summary>The colour a laid-out run is drawn in, or highlighted with; null for none.</summary>
    private static Color? DrawnColour(TextRunProperties run, bool foreground) =>
        (foreground ? run.ForegroundBrush : run.BackgroundBrush) is ISolidColorBrush brush ? brush.Color : null;

    /// <summary>The little swatch on a colour button that says what the selection is.</summary>
    private static Border ButtonSwatch(RichTextToolbar bar, string tip) =>
        Find(bar, tip)!.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("VellumText-swatch"));

    /// <summary>
    /// A list button makes exactly the selected paragraphs a list — drawn with their bullets or
    /// numbers, and set in from the paragraph after them — and pressed again, takes them out.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Bulleted list (Ctrl+Shift+8)", ListKind.Unordered, "\u2022", "\u2022")]
    [InlineData("Numbered list (Ctrl+Shift+7)", ListKind.Ordered, "1.", "2.")]
    public void AListButtonListsExactlyTheSelectedParagraphsAndPressedAgainUnlistsThem(
        string tip, ListKind kind, string firstMarker, string secondMarker)
    {
        var bar = Templated(out var editor, ThreeParagraphs());
        var view = editor.View!;
        var button = Toggle(bar, tip);
        var other = Toggle(bar, kind == ListKind.Ordered ? "Bulleted list (Ctrl+Shift+8)" : "Numbered list (Ctrl+Shift+7)");
        var left = TextLeft(view, 2);

        SelectAcross(view);
        UserClick(button);

        var list = Assert.IsType<ListNode>(editor.State.Doc.Blocks[0]);

        Assert.Equal(kind, list.Kind);
        Assert.Equal([First, Second], list.Items.Select(item => Assert.IsType<ParagraphNode>(Assert.Single(item.Blocks)).Content.Text));
        Assert.Equal(Third, Assert.IsType<ParagraphNode>(editor.State.Doc.Blocks[1]).Content.Text);
        Assert.Equal(kind, bar.ListKind);
        Assert.True(button.IsChecked);
        Assert.False(other.IsChecked);

        // On screen: a marker in front of each item and none in front of the paragraph after;
        // the items' text set in by one step, the paragraph's where it was.
        Assert.Equal([firstMarker, secondMarker, null], view.Blocks.Select(b => ((ParagraphView)b).MarkerText));
        Assert.Equal(left + ParagraphView.DefaultIndentStep, TextLeft(view, 0), 3);
        Assert.Equal(left + ParagraphView.DefaultIndentStep, TextLeft(view, 1), 3);
        Assert.Equal(left, TextLeft(view, 2), 3);
        AssertNoOverlappingLines(view);

        UserClick(button);

        Assert.Equal([First, Second, Third], editor.State.Doc.Blocks.Select(b => Assert.IsType<ParagraphNode>(b).Content.Text));
        Assert.Null(bar.ListKind);
        Assert.False(button.IsChecked);
        Assert.All(view.Blocks, b => Assert.Null(((ParagraphView)b).MarkerText));
        Assert.Equal(left, TextLeft(view, 0), 3);
    }

    /// <summary>Where a block's text starts, in the view's coordinates.</summary>
    private static double TextLeft(RichTextView view, int block) =>
        view.BlockOrigin(block).X + Assert.IsType<ParagraphView>(view.Blocks[block]).GetCaretRect(0).X;

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
