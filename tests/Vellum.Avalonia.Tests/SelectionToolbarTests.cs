using global::Avalonia;
using global::Avalonia.Automation.Peers;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.VisualTree;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// The formatting bar that floats beside a selection, per architecture 10.3 P4.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion here is about geometry or about when the bar is offered, because those are the
/// two things that cannot be checked by reading the markup. A bar that appears at the right moment
/// in the wrong place is as broken as one that never appears, and neither shows up in a test that
/// only asks whether the control exists.
/// </para>
/// <para>
/// Visibility is real <c>IsVisible</c>, not transparency: opacity leaves the bar visible to
/// automation, which announces nine buttons that are not on screen. The cost is that a hidden
/// control is never measured, which the editor handles by measuring it as it shows it.
/// </para>
/// </remarks>
public class SelectionToolbarTests
{
    private const double Width = 620;
    private const double Height = 460;

    [AvaloniaFact]
    public void NothingFloatsUntilSomethingIsSelected()
    {
        var editor = Shown(out _);

        Assert.NotNull(editor.SelectionToolbar);
        Assert.False(editor.SelectionToolbar!.IsVisible);
    }

    /// <remarks>
    /// The reason the bar is hidden rather than made transparent. A screen reader asks the peer
    /// whether an element is offscreen; Avalonia answers from <c>IsEffectivelyVisible</c>, which
    /// opacity does not touch. This asserts through the automation peer rather than through
    /// <c>IsVisible</c> so that it keeps biting if the mechanism is ever changed back.
    /// </remarks>
    [AvaloniaFact]
    public void AScreenReaderIsNotToldAboutTheHiddenBar()
    {
        var editor = Shown(out var window);
        var bar = editor.SelectionToolbar!;

        Assert.True(ControlAutomationPeer.CreatePeerForElement(bar).IsOffscreen());

        Select(editor, 0);
        Layout(window);

        Assert.False(ControlAutomationPeer.CreatePeerForElement(bar).IsOffscreen());
    }

    [AvaloniaFact]
    public void SelectingTextFloatsABarBesideIt()
    {
        var editor = Shown(out var window);

        Select(editor, 0);
        Layout(window);

        var bar = editor.SelectionToolbar!;

        Assert.True(bar.IsVisible);
        Assert.True(bar.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void TheBarSitsAboveTheSelectionWhenThereIsRoom()
    {
        var editor = Shown(out var window);

        // The third paragraph, so that there is a whole screenful of document above it to put
        // the bar in. Over the first there would not be, and the fallback below would pass this
        // by accident.
        Select(editor, 2);
        Layout(window);

        var bar = editor.SelectionToolbar!;
        var area = Selection(editor);

        Assert.True(
            editor.SelectionToolbarPlacement.Y + bar.DesiredSize.Height <= area.Top,
            $"bar bottom {editor.SelectionToolbarPlacement.Y + bar.DesiredSize.Height} is not above selection top {area.Top}");

        // Centred on what it formats. A bar pinned to the left edge of the selection is a
        // different design, not a detail: it points at the first word rather than the phrase.
        Assert.Equal(
            area.Center.X,
            editor.SelectionToolbarPlacement.X + (bar.DesiredSize.Width / 2),
            precision: 6);
    }

    [AvaloniaFact]
    public void TheBarDropsBelowTheSelectionWhenThereIsNoRoomAbove()
    {
        var editor = Shown(out var window);

        Select(editor, 0);
        Layout(window);

        var bar = editor.SelectionToolbar!;
        var area = Selection(editor);

        Assert.True(
            area.Top - bar.DesiredSize.Height < 0,
            "the first paragraph was expected to leave no room above it; the test's premise is gone");

        // Strictly below, not resting on it. A bar flush against the highlight reads as part of
        // the selection rather than as something offered about it.
        Assert.True(
            editor.SelectionToolbarPlacement.Y > area.Bottom,
            $"bar top {editor.SelectionToolbarPlacement.Y} is not clear of selection bottom {area.Bottom}");
    }

    /// <summary>
    /// The clamp, exercised where it actually bites. A bar centred on a selection at the start of
    /// a line wants to hang off the left edge, and every placement in the middle of a roomy editor
    /// lands inside by luck rather than by the clamp — a test that only ever made those would pass
    /// with the clamp deleted.
    /// </summary>
    [AvaloniaFact]
    public void TheBarIsPulledBackInsideTheLeftEdge()
    {
        var editor = Shown(out var window);

        SelectRange(editor, 0, 0, 3);
        Layout(window);

        var bar = editor.SelectionToolbar!;
        var area = Selection(editor);

        Assert.True(
            area.Center.X - (bar.DesiredSize.Width / 2) < 0,
            $"a bar of {bar.DesiredSize.Width} centred on {area.Center.X} was expected to overhang "
            + "the left edge; the test's premise is gone");

        Assert.Equal(0, editor.SelectionToolbarPlacement.X);
    }

    /// <summary>The same, against the bottom edge: a selection taller than the editor it is in.</summary>
    [AvaloniaFact]
    public void TheBarIsPulledBackInsideTheBottomEdge()
    {
        var editor = Shown(out var window, height: 150);

        SelectAll(editor);
        Layout(window, height: 150);

        var bar = editor.SelectionToolbar!;
        var area = Selection(editor);
        var placed = new Rect(editor.SelectionToolbarPlacement, bar.DesiredSize);

        Assert.True(
            area.Bottom + bar.DesiredSize.Height > editor.Bounds.Height,
            $"a selection ending at {area.Bottom} in an editor of {editor.Bounds.Height} was "
            + "expected to leave no room below it; the test's premise is gone");

        Assert.Equal(editor.Bounds.Height - bar.DesiredSize.Height, placed.Top, precision: 6);
    }

    /// <summary>
    /// A selection in the last visible line would put the bar off the bottom of the editor if it
    /// were placed without regard to the space left, and a bar half outside its own control is
    /// clipped to a sliver.
    /// </summary>
    [AvaloniaFact]
    public void TheBarIsAlwaysWhollyInsideTheEditor()
    {
        var editor = Shown(out var window);

        foreach (var block in new[] { 0, 1, 2, 3 })
        {
            Select(editor, block);
            Layout(window);

            var bar = editor.SelectionToolbar!;

            if (!bar.IsVisible)
            {
                continue;
            }

            var placed = new Rect(
                editor.SelectionToolbarPlacement, bar.DesiredSize);

            Assert.True(placed.Left >= 0, $"block {block}: left {placed.Left}");
            Assert.True(placed.Top >= 0, $"block {block}: top {placed.Top}");
            Assert.True(placed.Right <= editor.Bounds.Width, $"block {block}: right {placed.Right}");
            Assert.True(placed.Bottom <= editor.Bounds.Height, $"block {block}: bottom {placed.Bottom}");
        }
    }

    [AvaloniaFact]
    public void CollapsingTheSelectionTakesTheBarAway()
    {
        var editor = Shown(out var window);
        var view = editor.View!;

        Select(editor, 0);
        Layout(window);

        Assert.True(editor.SelectionToolbar!.IsVisible);

        view.MoveTo(view.Slots[0].Start, extend: false);
        Layout(window);

        Assert.False(editor.SelectionToolbar.IsVisible);
        Assert.False(editor.SelectionToolbar.IsHitTestVisible);
    }

    [AvaloniaFact]
    public void TurningTheFeatureOffTakesTheBarAwayWhileTheSelectionStands()
    {
        var editor = Shown(out var window);

        Select(editor, 0);
        Layout(window);

        Assert.True(editor.SelectionToolbar!.IsVisible);

        editor.IsSelectionToolbarEnabled = false;

        Assert.False(editor.SelectionToolbar.IsVisible);
        Assert.False(editor.View!.State.Selection.IsEmpty);
    }

    /// <summary>
    /// The bar must not appear until the drag that is making the selection has finished. Shown
    /// during one it chases the pointer across the paragraph, and being under the pointer it
    /// swallows the release that was meant to end the drag.
    /// </summary>
    [AvaloniaFact]
    public void NothingFloatsWhileTheSelectionIsStillBeingDragged()
    {
        var editor = Shown(out var window);
        var view = editor.View!;
        var start = Point(window, editor, 0, 2);
        var end = Point(window, editor, 0, 9);

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end);
        Layout(window);

        Assert.True(view.IsDragging);
        Assert.False(view.State.Selection.IsEmpty);
        Assert.False(editor.SelectionToolbar!.IsVisible);

        window.MouseUp(end, MouseButton.Left);
        Layout(window);

        Assert.False(view.IsDragging);
        Assert.True(editor.SelectionToolbar.IsVisible);
    }

    [AvaloniaFact]
    public void TheFloatingBarFormatsTheSelectionItIsFloatingBeside()
    {
        var editor = Shown(out var window);

        Select(editor, 0);
        Layout(window);

        Click(editor.SelectionToolbar!, "Bold (Ctrl+B)");

        var paragraph = Assert.IsType<ParagraphNode>(editor.State.Doc.Blocks[0]);

        Assert.All(
            paragraph.Content.Marks,
            span => Assert.True(span.Value.Style.HasFlag(TextStyle.Bold)));
        Assert.NotEmpty(paragraph.Content.Marks);
    }

    [AvaloniaFact]
    public void TheFloatingBarTracksWhatTheSelectionAlreadyIs()
    {
        var editor = Shown(out var window);

        Select(editor, 0);
        Layout(window);

        var bar = editor.SelectionToolbar!;

        Assert.False(bar.IsBold);

        Click(bar, "Bold (Ctrl+B)");

        Assert.True(bar.IsBold);
    }

    /// <summary>
    /// An application that has not added <c>Themes/Vellum.axaml</c> gets no floating bar rather
    /// than an empty box over its text: without the theme the toolbar has no template, and a
    /// control with no template has nothing to show.
    /// </summary>
    [AvaloniaFact]
    public void WithoutTheShippedThemeNothingFloats()
    {
        var editor = new RichTextEditor { State = EditorState.Create(Document()) };
        var window = new Window { Width = Width, Height = Height, Content = editor };

        window.Show();
        Layout(window);

        Select(editor, 0);
        Layout(window);

        Assert.Null(editor.SelectionToolbar!.Template);
        Assert.False(editor.SelectionToolbar.IsVisible);
    }

    /// <summary>Builds a themed editor filling a shown window, and lays it out once.</summary>
    private static RichTextEditor Shown(out Window window, double width = Width, double height = Height)
    {
        var include = new StyleInclude(new Uri("avares://Vellum.Avalonia/"))
        {
            Source = new Uri("avares://Vellum.Avalonia/Themes/Vellum.axaml"),
        };

        var editor = new RichTextEditor { State = EditorState.Create(Document()) };

        window = new Window { Width = width, Height = height };

        window.Styles.Add(include);
        window.Content = editor;
        window.Show();

        Layout(window, width, height);

        return editor;
    }

    /// <summary>
    /// Four paragraphs, long enough to wrap, so that a selection can be made well down the page
    /// as well as at the very top.
    /// </summary>
    private static DocumentNode Document() =>
        DocumentNode.FromParagraphs(
            "hello world, this first paragraph is long enough to take more than one line",
            "a second paragraph, also long enough that it wraps across the available width",
            "a third paragraph, which is the one with room above it for a bar to sit in",
            "a fourth paragraph, close enough to the bottom to test the other clamp");

    private static void Layout(Window window, double width = Width, double height = Height)
    {
        window.Measure(new Size(width, height));
        window.Arrange(new Rect(0, 0, width, height));
    }

    /// <summary>Selects the whole of one block.</summary>
    private static void Select(RichTextEditor editor, int block)
    {
        var view = editor.View!;

        view.MoveTo(view.Slots[block].Start, extend: false);
        view.MoveTo(view.Slots[block].End, extend: true);
    }

    /// <summary>Selects a range of characters inside one block.</summary>
    private static void SelectRange(RichTextEditor editor, int block, int from, int to)
    {
        var view = editor.View!;
        var start = view.Slots[block].Start;

        view.MoveTo(start + from, extend: false);
        view.MoveTo(start + to, extend: true);
    }

    private static void SelectAll(RichTextEditor editor)
    {
        var view = editor.View!;

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[^1].End, extend: true);
    }

    /// <summary>Where the selection is, in the same coordinates the bar is placed in.</summary>
    private static Rect Selection(RichTextEditor editor)
    {
        var view = editor.View!;
        var overlay = editor.GetVisualDescendants().OfType<Panel>().First();
        var local = view.SelectionBounds()!.Value;

        return new Rect(
            view.TranslatePoint(local.TopLeft, overlay)!.Value,
            view.TranslatePoint(local.BottomRight, overlay)!.Value);
    }

    /// <summary>A point inside a block, in window coordinates.</summary>
    private static Point Point(Window window, RichTextEditor editor, int block, int offset)
    {
        var view = editor.View!;
        var local = view.Blocks[block].GetCaretRect(offset);

        return view.TranslatePoint(
            new Point(local.X + 1, local.Y + 2) + view.BlockOrigin(block),
            window)!.Value;
    }

    private static void Click(RichTextToolbar bar, string tip)
    {
        var button = bar.GetVisualDescendants()
            .OfType<Button>()
            .Single(b => (string?)ToolTip.GetTip(b) == tip);

        button.Command!.Execute(button.CommandParameter);
    }
}
