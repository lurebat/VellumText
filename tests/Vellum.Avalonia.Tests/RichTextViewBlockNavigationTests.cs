using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Caret movement that leaves the block it started in.
/// </summary>
public class RichTextViewBlockNavigationTests
{
    [AvaloniaFact]
    public void RightAtTheEndOfAParagraphEntersTheNextOne()
    {
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[0].End, extend: false);
        Press(view, Key.Right);

        Assert.Equal(1, view.CaretBlockIndex);
        Assert.Equal(view.Slots[1].Start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void LeftAtTheStartOfAParagraphEntersTheEndOfThePreviousOne()
    {
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[1].Start, extend: false);
        Press(view, Key.Left);

        Assert.Equal(0, view.CaretBlockIndex);
        Assert.Equal(view.Slots[0].End, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void RightAtTheEndOfTheDocumentStaysPut()
    {
        var view = Shown("first", "second");
        var end = view.Slots[1].End;

        view.MoveTo(end, extend: false);
        Press(view, Key.Right);

        Assert.Equal(end, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void LeftAtTheStartOfTheDocumentStaysPut()
    {
        var view = Shown("first", "second");
        var start = view.Slots[0].Start;

        view.MoveTo(start, extend: false);
        Press(view, Key.Left);

        Assert.Equal(start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void SteppingOutOfABlockWithShiftKeepsTheAnchor()
    {
        // The regression that the block refactor introduced and this pins: a head that is not a
        // text position went through Selection.Near, which answers with a cursor.
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[0].Start + 2, extend: false);
        Press(view, Key.Right, KeyModifiers.Shift);
        Press(view, Key.End, KeyModifiers.Shift);
        Press(view, Key.Right, KeyModifiers.Shift);

        Assert.Equal(view.Slots[0].Start + 2, view.State.Selection.Anchor);
        Assert.Equal(view.Slots[1].Start, view.State.Selection.Head);
        Assert.False(view.State.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void ARuleIsSteppedOverRatherThanStoppedAt()
    {
        var view = Shown(new DocumentNode([
            ParagraphNode.FromText("above"),
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]));

        view.MoveTo(view.Slots[0].End, extend: false);
        Press(view, Key.Right);

        Assert.Equal(2, view.CaretBlockIndex);
        Assert.Equal(view.Slots[2].Start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void DownFromTheLastLineEntersTheNextParagraph()
    {
        // Identical text in both, because a vertical move keeps an x offset rather than a
        // column: with different glyphs the same offset is legitimately a different index.
        var view = Shown("mirror", "mirror");

        view.MoveTo(view.Slots[0].Start + 3, extend: false);
        Press(view, Key.Down);

        Assert.Equal(1, view.CaretBlockIndex);
        Assert.Equal(view.Slots[1].Start + 3, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void UpFromTheFirstLineEntersThePreviousParagraph()
    {
        var view = Shown("mirror", "mirror");

        view.MoveTo(view.Slots[1].Start + 3, extend: false);
        Press(view, Key.Up);

        Assert.Equal(0, view.CaretBlockIndex);
        Assert.Equal(view.Slots[0].Start + 3, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void UpEntersTheLastLineOfThePreviousParagraphNotItsFirst()
    {
        // A wrapping paragraph above: Up must land on the line nearest the caret, which is the
        // bottom one. Landing on the first line is the easy mistake and is very visible.
        var view = Shown(WrappingText, "below");

        Assert.True(((ParagraphView)view.Blocks[0]).LineCount > 1);

        var lastLine = ((ParagraphView)view.Blocks[0]).LineStart(
            ((ParagraphView)view.Blocks[0]).LineCount - 1);

        view.MoveTo(view.Slots[1].Start + 1, extend: false);
        Press(view, Key.Up);

        Assert.Equal(0, view.CaretBlockIndex);
        Assert.True(
            view.State.Selection.Head - view.Slots[0].Start >= lastLine,
            "Up landed above the last line of the paragraph it entered");
    }

    [AvaloniaFact]
    public void DownAcrossAParagraphKeepsTheGoalColumn()
    {
        // Down through a short paragraph and out the other side returns to the column the caret
        // started in, rather than being permanently pulled left by the short one.
        var view = Shown("a long enough paragraph here", "hi", "a long enough paragraph here");

        view.MoveTo(view.Slots[0].Start + 20, extend: false);

        Press(view, Key.Down);
        Assert.Equal(1, view.CaretBlockIndex);

        Press(view, Key.Down);

        Assert.Equal(2, view.CaretBlockIndex);
        Assert.Equal(view.Slots[2].Start + 20, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void DownAcrossARuleReachesTheParagraphBelowIt()
    {
        var view = Shown(new DocumentNode([
            ParagraphNode.FromText("mirror"),
            RuleNode.Instance,
            ParagraphNode.FromText("mirror"),
        ]));

        view.MoveTo(view.Slots[0].Start + 2, extend: false);
        Press(view, Key.Down);

        Assert.Equal(2, view.CaretBlockIndex);
        Assert.Equal(view.Slots[2].Start + 2, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void DownFromTheLastParagraphGoesToTheEndOfTheDocument()
    {
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[1].Start + 1, extend: false);
        Press(view, Key.Down);

        Assert.Equal(view.Slots[1].End, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void UpFromTheFirstParagraphGoesToTheStartOfTheDocument()
    {
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[0].Start + 3, extend: false);
        Press(view, Key.Up);

        Assert.Equal(view.Slots[0].Start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void CtrlHomeAndCtrlEndReachEitherEndOfTheDocument()
    {
        var view = Shown("first", "second", "third");

        view.MoveTo(view.Slots[1].Start + 1, extend: false);

        Press(view, Key.End, KeyModifiers.Control);
        Assert.Equal(view.Slots[2].End, view.State.Selection.Head);

        Press(view, Key.Home, KeyModifiers.Control);
        Assert.Equal(view.Slots[0].Start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void HomeAndEndStayInsideTheBlock()
    {
        var view = Shown("first", "second", "third");

        view.MoveTo(view.Slots[1].Start + 3, extend: false);

        Press(view, Key.End);
        Assert.Equal(view.Slots[1].End, view.State.Selection.Head);

        Press(view, Key.Home);
        Assert.Equal(view.Slots[1].Start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void CtrlRightAtTheEndOfAParagraphEntersTheNextOne()
    {
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[0].End, extend: false);
        Press(view, Key.Right, KeyModifiers.Control);

        Assert.Equal(1, view.CaretBlockIndex);
    }

    [AvaloniaFact]
    public void SelectAllCoversEveryParagraph()
    {
        var view = Shown("first", "second", "third");

        Press(view, Key.A, KeyModifiers.Control);

        Assert.Equal(view.Slots[0].Start, view.State.Selection.From);
        Assert.Equal(view.Slots[2].End, view.State.Selection.To);
    }

    private const string WrappingText =
        "a paragraph long enough that it has to wrap onto more than a single line at this width";

    private static void Press(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });

    private static RichTextView Shown(params string[] paragraphs) =>
        Shown(DocumentNode.FromParagraphs(paragraphs));

    private static RichTextView Shown(DocumentNode doc)
    {
        var root = new Window { Width = 300, Height = 400 };
        var view = new RichTextView { State = EditorState.Create(doc) };

        root.Content = view;
        root.Show();
        view.Focus();

        view.Measure(new Size(300, 400));
        view.Arrange(new Rect(0, 0, 300, 400));

        return view;
    }
}
