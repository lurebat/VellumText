using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media;
using global::Avalonia.VisualTree;
using VellumText;
using VellumText.Avalonia;
using static VellumText.Tests.Avalonia.ToolbarHarness;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The default toolbar template that ships in <c>Themes/VellumText.axaml</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RichTextToolbarTests"/> deliberately never applies a template, because the control's
/// state and commands must work without one. That leaves the shipped markup itself untested, and
/// a theme that fails to load is a control that draws nothing — the exact failure a consumer sees
/// first. These tests load the real resource by URI and drive the real buttons.
/// </para>
/// <para>
/// The buttons are clicked through their own click handling (<see cref="ToolbarHarness.UserClick(Button)"/>),
/// never by running their command: a flyout that closed before its command ran passed every test
/// that ran the command directly.
/// </para>
/// </remarks>
public class ToolbarThemeTests
{
    [AvaloniaFact]
    public void TheShippedThemeGivesTheToolbarItsButtons()
    {
        var bar = Templated(out _);

        var buttons = Buttons(bar);

        // The count is a floor, not a fingerprint: the point is that the bar was populated from
        // markup rather than left empty, and a template edit should not have to touch this.
        Assert.True(buttons.Count >= 24, $"expected a populated bar, found {buttons.Count} buttons");
    }

    /// <summary>
    /// No button in the bar may take the keyboard. Clicking Bold and then finding that the next
    /// keystroke went to a button rather than into the document is the single most irritating
    /// thing a formatting bar can do, and nothing about it is visible until you try to type.
    /// </summary>
    [AvaloniaFact]
    public void NoButtonInTheBarTakesTheKeyboard()
    {
        var bar = Templated(out _);

        Assert.All(Buttons(bar), button => Assert.False(button.Focusable));
    }

    [AvaloniaFact]
    public void TheBoldButtonBoldsTheSelection()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        UserClick(bar, "Bold (Ctrl+B)");

        Assert.True(bar.IsBold);
        Assert.True(Assert.IsType<ParagraphNode>(Assert.Single(editor.State.Doc.Blocks)).Content.Marks.Single().Value.Style
            .HasFlag(TextStyle.Bold));
    }

    /// <summary>
    /// The bar reports what the selection is; the buttons only display it. A button that lit
    /// because it was clicked rather than because the document changed would keep lying after
    /// undo, which is the whole reason the state is derived.
    /// </summary>
    [AvaloniaFact]
    public void AButtonLightsBecauseTheDocumentSaysSoNotBecauseItWasClicked()
    {
        var bar = Templated(out var editor);

        var bold = Find(bar, "Bold (Ctrl+B)") as ToggleButton;

        Assert.NotNull(bold);
        Assert.False(bold.IsChecked);

        var view = editor.View!;

        SelectAll(view);
        view.ToggleStyle(TextStyle.Bold);

        Assert.True(bold.IsChecked);

        view.Undo();

        Assert.False(bold.IsChecked);
    }

    [AvaloniaFact]
    public void TheHeadingButtonsLightOneAtATime()
    {
        var bar = Templated(out var editor);

        var body = (ToggleButton)Find(bar, "Body text")!;
        var h1 = (ToggleButton)Find(bar, "Heading 1")!;
        var h2 = (ToggleButton)Find(bar, "Heading 2")!;

        Assert.True(body.IsChecked);
        Assert.False(h1.IsChecked);

        UserClick(bar, "Heading 1");

        Assert.True(h1.IsChecked);
        Assert.False(body.IsChecked);
        Assert.False(h2.IsChecked);
        Assert.Equal(ParagraphKind.Heading1, Assert.IsType<ParagraphNode>(Assert.Single(editor.State.Doc.Blocks)).Kind);
    }

    [AvaloniaFact]
    public void UndoIsDisabledUntilThereIsSomethingToUndo()
    {
        var bar = Templated(out var editor);

        var undo = Find(bar, "Undo (Ctrl+Z)")!;

        Assert.False(undo.IsEnabled);

        var view = editor.View!;

        SelectAll(view);
        view.ToggleStyle(TextStyle.Bold);

        Assert.True(undo.IsEnabled);
    }

    /// <summary>
    /// The alignment buttons pass an enum through <c>CommandParameter</c>, which markup can only
    /// express as <c>x:Static</c>. Getting that wrong passes a string and the command silently
    /// does nothing, so it is worth one test that the parameter arrives as the enum.
    /// </summary>
    [AvaloniaFact]
    public void TheCentreButtonCentresTheParagraph()
    {
        var bar = Templated(out var editor);

        UserClick(bar, "Centre");

        Assert.Equal(TextAlign.Center, Assert.IsType<ParagraphNode>(Assert.Single(editor.State.Doc.Blocks)).Align);
        Assert.True(((ToggleButton)Find(bar, "Centre")!).IsChecked);
    }

    [AvaloniaFact]
    public void TheListButtonMakesTheParagraphAnItem()
    {
        var bar = Templated(out var editor);

        UserClick(bar, "Bulleted list (Ctrl+Shift+8)");

        Assert.Equal(ListKind.Unordered, bar.ListKind);
        Assert.IsType<ListNode>(Assert.Single(editor.State.Doc.Blocks));
    }

    /// <summary>
    /// A flyout's content lives in a popup, which is not a visual descendant of the button that
    /// opened it. Whether a binding written against the templated parent survives that is exactly
    /// the sort of thing that compiles, looks right, and silently does nothing.
    /// </summary>
    [AvaloniaFact]
    public void ASwatchInTheColourFlyoutColoursTheSelection()
    {
        var bar = Templated(out var editor);

        var view = editor.View!;

        SelectAll(view);

        var swatch = Swatches(bar, "Text colour")[8];

        Assert.Equal("#C82020", swatch.CommandParameter);

        UserClick(swatch);

        Assert.Equal(new Rgba(0xC8, 0x20, 0x20, 255), bar.SelectionForeground);
    }

    [AvaloniaFact]
    public void TheHighlightFlyoutReachesTheOtherCommand()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var swatch = Swatches(bar, "Highlight")[11];

        UserClick(swatch);

        Assert.Equal(new Rgba(0x18, 0x90, 0x40, 255), bar.SelectionHighlight);
        Assert.Null(bar.SelectionForeground);
    }

    /// <summary>
    /// No colour is not black. It removes the mark, so the text takes what it inherits — which is
    /// the difference between pasted content that follows a theme and content that is stuck dark.
    /// </summary>
    [AvaloniaFact]
    public void ClearingTheColourRemovesTheMarkRatherThanSettingBlack()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var swatch = Swatches(bar, "Text colour")[0];

        UserClick(swatch);

        Assert.Equal(new Rgba(0, 0, 0, 255), bar.SelectionForeground);

        UserClick(ClearColour(bar, "Text colour"));

        Assert.Null(bar.SelectionForeground);
    }

    /// <summary>
    /// Picking a size from the list applies it, in points. Only Enter used to commit, so a click
    /// on "18" in the open list changed the box and left the document alone — found live, with
    /// no test to say otherwise.
    /// </summary>
    [AvaloniaFact]
    public void PickingASizeFromTheListAppliesIt()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var box = Box(bar, "PART_SizeBox");

        box.IsDropDownOpen = true;
        box.SelectedItem = 18.0;

        Assert.Equal(24.0, FirstMark(editor).FontSize!.Value, 6);
        Assert.Equal(18.0, bar.SelectionFontSize!.Value, 6);
    }

    [AvaloniaFact]
    public void PickingAFontFromTheListAppliesIt()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var box = Box(bar, "PART_FontBox");

        // A headless platform may report no installed fonts; the pick is what is under test.
        if (box.ItemCount == 0)
        {
            box.ItemsSource = new[] { "Arial", "Courier New" };
        }

        var family = (string)box.Items[^1]!;

        box.IsDropDownOpen = true;
        box.SelectedItem = family;

        Assert.Equal(family, FirstMark(editor).FontFamily);
    }

    /// <summary>
    /// The box's text follows the selection, and an editable box may select the matching item to
    /// go with it. That must not be read as a pick: moving the caret into 20-point text would
    /// otherwise re-apply 20 points to whatever is selected — an edit nobody asked for.
    /// </summary>
    [AvaloniaFact]
    public void ASelectionChangeTheUserDidNotPickDoesNotEditTheDocument()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var before = editor.State.Doc;
        var size = Box(bar, "PART_SizeBox");
        var font = Box(bar, "PART_FontBox");

        size.SelectedItem = 18.0;
        font.SelectedItem = font.ItemCount > 0 ? font.Items[0] : null;

        Assert.Same(before, editor.State.Doc);
    }

    private static MarkSet FirstMark(RichTextEditor editor) =>
        Assert.IsType<ParagraphNode>(editor.State.Doc.Blocks[0]).Content.MarkAt(0);

    /// <summary>
    /// A colour picked with a real click lands on the selected text, in the document and on screen,
    /// and the flyout closes. The flyout used to close on the swatch's Click, which a button raises
    /// before it runs its command: hiding the flyout took the swatch out of the tree and its command
    /// binding with it, so the click closed the flyout and changed nothing (found live).
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Text colour")]
    [InlineData("Highlight")]
    public void PickingAColourAppliesItToTheSelection(string tip)
    {
        var bar = Templated(out var editor);
        var view = editor.View!;

        // "hello", and not " world".
        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[0].Start + 5, extend: true);

        var swatch = Swatches(bar, tip)[8];
        var colour = Color.Parse((string)swatch.CommandParameter!);
        var flyout = (Flyout)Find(bar, tip)!.Flyout!;

        UserClick(swatch);

        var hello = FirstMark(editor);
        var world = Assert.IsType<ParagraphNode>(editor.State.Doc.Blocks[0]).Content.MarkAt(7);

        Assert.Equal(colour.ToVellumText(), tip == "Text colour" ? hello.Foreground : hello.Highlight);
        Assert.Null(tip == "Text colour" ? world.Foreground : world.Highlight);
        Assert.False(flyout.IsOpen);

        // On screen too: the run drawn for "hello" carries the colour.
        if (tip == "Text colour")
        {
            Layout(WindowOf(bar));

            var run = ((ParagraphView)view.Blocks[0]).Layout.TextLines[0].TextRuns[0];

            Assert.Equal(colour, Assert.IsAssignableFrom<ISolidColorBrush>(run.Properties!.ForegroundBrush).Color);
        }
    }

    [AvaloniaFact]
    public void ClearingTheColourWithAClickRemovesIt()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);
        bar.SetForegroundCommand.Execute("#C82020");
        Assert.NotNull(FirstMark(editor).Foreground);

        var clear = Popup(bar, "Text colour").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("VellumText-clear-colour"));
        var flyout = (Flyout)Find(bar, "Text colour")!.Flyout!;

        UserClick(clear);

        Assert.Null(FirstMark(editor).Foreground);
        Assert.False(flyout.IsOpen);
    }

    /// <summary>
    /// Picking a colour closes the flyout it was picked from (owner decision). Found live: the
    /// colour applied and the flyout stayed open over the document until Escape or a click
    /// elsewhere — a second step for the commonest thing the flyout is for.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Text colour")]
    [InlineData("Highlight")]
    public void PickingAColourClosesTheFlyout(string tip)
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var swatch = Swatches(bar, tip)[8];
        var flyout = (Flyout)Find(bar, tip)!.Flyout!;

        Assert.True(flyout.IsOpen);

        UserClick(swatch);

        Assert.False(flyout.IsOpen);
    }

    [AvaloniaFact]
    public void ClearingTheColourClosesTheFlyoutToo()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var clear = Popup(bar, "Text colour").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("VellumText-clear-colour"));
        var flyout = (Flyout)Find(bar, "Text colour")!.Flyout!;

        UserClick(clear);

        Assert.False(flyout.IsOpen);
    }

    private static void SelectAll(RichTextView view)
    {
        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[^1].End, extend: true);
    }

}
