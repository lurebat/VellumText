using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Tests for pointer and keyboard selection on <see cref="RichTextView"/>.
/// </summary>
public class RichTextViewInputTests
{
    private const double Width = 400;

    [AvaloniaFact]
    public void ClickingPlacesTheCaretWhereItWasClicked()
    {
        var (window, view) = Shown("hello world");
        var target = view.Block!.GetCaretRect(6);

        window.MouseDown(new Point(target.X + 1, target.Y + 2), MouseButton.Left);

        Assert.True(view.State.Selection.IsEmpty);
        Assert.Equal(view.BlockStart + 6, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ClickingAnywhereInTheControlReachesIt()
    {
        // Avalonia hit-tests a plain Control against what it drew, so a view that painted only
        // glyphs is not clickable in the leading above a line, the gap below one, or the space
        // to the right of the last character. Measured before the transparent background was
        // added: a click 2px from the top reported the parent ContentPresenter as its source.
        var (window, view) = Shown("hello world");

        window.MouseDown(new Point(300, 0), MouseButton.Left);

        Assert.True(view.IsFocused);
        Assert.Equal(view.BlockStart + view.Block!.ContentSize, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ClearingTheBackgroundIsWhatMakesTheControlUnclickable()
    {
        // The characterization of the above: this records that the background is the mechanism,
        // so that anyone who defaults it to null finds out here rather than in an application.
        var (window, view) = Shown("hello world");

        view.Background = null;

        window.MouseDown(new Point(300, 0), MouseButton.Left);

        Assert.True(view.State.Selection.IsEmpty);
        Assert.Equal(view.BlockStart, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ClickingFocusesTheViewSoTypingCanFollow()
    {
        var (window, view) = Shown("hello");

        Assert.False(view.IsFocused);

        window.MouseDown(new Point(5, 5), MouseButton.Left);

        Assert.True(view.IsFocused);
    }

    [AvaloniaFact]
    public void DraggingExtendsTheSelectionFromWhereThePointerWentDown()
    {
        var (window, view) = Shown("hello world");
        var start = view.Block!.GetCaretRect(2);
        var end = view.Block.GetCaretRect(8);

        window.MouseDown(new Point(start.X + 1, start.Y + 2), MouseButton.Left);
        window.MouseMove(new Point(end.X + 1, end.Y + 2));

        Assert.True(view.IsDragging);
        Assert.Equal(view.BlockStart + 2, view.State.Selection.Anchor);
        Assert.Equal(view.BlockStart + 8, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void MovingWithoutADragDoesNotTouchTheSelection()
    {
        var (window, view) = Shown("hello world");

        window.MouseMove(new Point(200, 5));

        Assert.Equal(view.BlockStart, view.State.Selection.Anchor);
        Assert.False(view.IsDragging);
    }

    [AvaloniaFact]
    public void ReleasingEndsTheDragSoLaterMovementIsIgnored()
    {
        var (window, view) = Shown("hello world");
        var start = view.Block!.GetCaretRect(2);

        window.MouseDown(new Point(start.X + 1, start.Y + 2), MouseButton.Left);
        window.MouseUp(new Point(start.X + 1, start.Y + 2), MouseButton.Left);

        Assert.False(view.IsDragging);

        window.MouseMove(new Point(200, 5));

        Assert.True(view.State.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void LosingPointerCaptureEndsTheDrag()
    {
        var (window, view) = Shown("hello world");

        window.MouseDown(new Point(5, 5), MouseButton.Left);

        Assert.True(view.IsDragging);

        // A popup opening or the window deactivating takes capture away without a release.
        view.RaiseEvent(new PointerCaptureLostEventArgs(
            view, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true)));

        Assert.False(view.IsDragging);
    }

    [AvaloniaFact]
    public void ShiftClickingExtendsFromTheExistingAnchor()
    {
        var (window, view) = Shown("hello world");

        Key(view, global::Avalonia.Input.Key.Right);
        Key(view, global::Avalonia.Input.Key.Right);

        var target = view.Block!.GetCaretRect(9);

        window.MouseDown(
            new Point(target.X + 1, target.Y + 2), MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal(view.BlockStart + 2, view.State.Selection.Anchor);
        Assert.Equal(view.BlockStart + 9, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ArrowKeysStepOneCharacterAndShiftExtends()
    {
        var view = Focused("hello");

        Key(view, global::Avalonia.Input.Key.Right);

        Assert.Equal(view.BlockStart + 1, view.State.Selection.Head);
        Assert.True(view.State.Selection.IsEmpty);

        Key(view, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);

        Assert.Equal(view.BlockStart + 1, view.State.Selection.Anchor);
        Assert.Equal(view.BlockStart + 2, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Left, KeyModifiers.Shift);

        Assert.True(view.State.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void AnArrowKeyCollapsesASelectionToItsEdgeRatherThanSteppingFromTheHead()
    {
        // Selecting "ell" then pressing Left must land on 1, not on 0. Stepping from the head
        // would swallow a character, which is the classic version of this bug.
        var view = Focused("hello");

        Select(view, 1, 4);
        Key(view, global::Avalonia.Input.Key.Left);

        Assert.Equal(view.BlockStart + 1, view.State.Selection.Head);
        Assert.True(view.State.Selection.IsEmpty);

        Select(view, 1, 4);
        Key(view, global::Avalonia.Input.Key.Right);

        Assert.Equal(view.BlockStart + 4, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ArrowKeysNeverLandInsideAGraphemeCluster()
    {
        // A family emoji is one cluster made of seven code units.
        var view = Focused("a\U0001F468\u200D\U0001F469\u200D\U0001F467b");

        Key(view, global::Avalonia.Input.Key.Right);

        Assert.Equal(view.BlockStart + 1, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Right);

        Assert.Equal(view.BlockStart + 9, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Left);

        Assert.Equal(view.BlockStart + 1, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void HomeAndEndGoToTheEndsOfTheVisualLine()
    {
        var view = Focused(Wrapping);

        Assert.True(view.Block!.LineCount > 1);

        var second = view.Block.LineStart(1);
        var lineTop = view.Block.GetCaretRect(second).Y;

        Select(view, second + 2, second + 2);
        Key(view, global::Avalonia.Input.Key.Home);

        Assert.Equal(view.BlockStart + second, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.End);

        // Asserted against geometry, not against LineEnd: comparing with the same method the
        // control used could not fail. End landing on the wrap position would put the caret on
        // the *following* line, which is the bug this is watching for.
        var end = view.State.Selection.Head - view.BlockStart;

        Assert.Equal(lineTop, view.Block.GetCaretRect(end).Y);
        Assert.True(end > second);
        Assert.Equal(lineTop, view.Block.GetCaretRect(end - 1).Y);
        Assert.NotEqual(lineTop, view.Block.GetCaretRect(view.Block.LineStart(2)).Y);
    }

    [AvaloniaFact]
    public void EndOnTheFirstLineStaysOnTheFirstLine()
    {
        var view = Focused(Wrapping);

        Key(view, global::Avalonia.Input.Key.End);

        var head = view.State.Selection.Head - view.BlockStart;

        Assert.Equal(0, view.Block!.GetCaretRect(head).Y);
        Assert.True(head < view.Block.LineStart(1));
    }

    [AvaloniaFact]
    public void AtAWrapBoundaryTheCaretBelongsToTheLineItIsDrawnOn()
    {
        // A wrap boundary is one position on two lines. The caret for it is drawn at the start
        // of the second, so Home must go there and Up must reach the *first* line rather than
        // leaving for the top of the document.
        var view = Focused(Wrapping);

        Assert.True(view.Block!.LineCount >= 3);

        // The third line's start, not the second's: at the first boundary the caret sits at
        // x = 0, so moving up from it lands at position 0 whichever line is chosen, and the two
        // behaviours are indistinguishable. From the second boundary they are not.
        var boundary = view.Block.LineStart(2);

        Assert.Equal(view.Block.GetCaretRect(boundary).Y, view.Block.GetCaretRect(boundary + 1).Y);

        Select(view, boundary, boundary);
        Key(view, global::Avalonia.Input.Key.Home);

        Assert.Equal(view.BlockStart + boundary, view.State.Selection.Head);

        Select(view, boundary, boundary);
        Key(view, global::Avalonia.Input.Key.Up);

        Assert.Equal(view.BlockStart + view.Block.LineStart(1), view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ControlHomeAndControlEndGoToTheEndsOfTheDocument()
    {
        var view = Focused(Wrapping);

        Key(view, global::Avalonia.Input.Key.End, KeyModifiers.Control);

        Assert.Equal(view.BlockStart + view.Block!.ContentSize, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Home, KeyModifiers.Control);

        Assert.Equal(view.BlockStart, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void UpAndDownMoveByVisualLine()
    {
        var view = Focused(Wrapping);
        var start = view.Block!.LineStart(1) + 3;

        Select(view, start, start);
        Key(view, global::Avalonia.Input.Key.Down);

        Assert.Equal(2, view.Block.LineIndexAt(view.State.Selection.Head - view.BlockStart, forward: true));

        Key(view, global::Avalonia.Input.Key.Up);

        Assert.Equal(start, view.State.Selection.Head - view.BlockStart);
    }

    [AvaloniaFact]
    public void AVerticalRunKeepsTheColumnItStartedFrom()
    {
        // Passing through a short line must not permanently pull the caret left. The paragraph
        // ends with a short line, so going down to it and back up is the exact case.
        var view = Focused(Wrapping);
        var start = view.Block!.LineStart(0) + 20;

        Select(view, start, start);

        var column = view.Block.GetCaretRect(start).X;

        for (var i = 0; i < view.Block.LineCount - 1; i++)
        {
            Key(view, global::Avalonia.Input.Key.Down);
        }

        Assert.Equal(column, view.GoalX);

        for (var i = 0; i < view.Block.LineCount - 1; i++)
        {
            Key(view, global::Avalonia.Input.Key.Up);
        }

        Assert.Equal(start, view.State.Selection.Head - view.BlockStart);
    }

    [AvaloniaFact]
    public void AnyOtherMovementForgetsTheColumn()
    {
        var view = Focused(Wrapping);

        Key(view, global::Avalonia.Input.Key.Down);

        Assert.NotNull(view.GoalX);

        Key(view, global::Avalonia.Input.Key.Right);

        Assert.Null(view.GoalX);
    }

    [AvaloniaFact]
    public void UpFromTheFirstLineGoesToTheStartAndDownFromTheLastGoesToTheEnd()
    {
        var view = Focused(Wrapping);
        var start = view.Block!.LineStart(0) + 5;

        Select(view, start, start);
        Key(view, global::Avalonia.Input.Key.Up);

        Assert.Equal(view.BlockStart, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.End, KeyModifiers.Control);
        Key(view, global::Avalonia.Input.Key.Down);

        Assert.Equal(view.BlockStart + view.Block.ContentSize, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ControlArrowsMoveByWord()
    {
        var view = Focused("alpha beta  gamma");

        Key(view, global::Avalonia.Input.Key.Right, KeyModifiers.Control);

        Assert.Equal(view.BlockStart + 5, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Right, KeyModifiers.Control);

        Assert.Equal(view.BlockStart + 10, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Right, KeyModifiers.Control);

        Assert.Equal(view.BlockStart + 17, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Left, KeyModifiers.Control);

        Assert.Equal(view.BlockStart + 12, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Left, KeyModifiers.Control);

        Assert.Equal(view.BlockStart + 6, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void WordMovementTerminatesOnAParagraphOfSeparators()
    {
        // The loops advance by caret position, so a paragraph with no word characters must
        // still reach an end rather than spin.
        var view = Focused("--- ...");

        Key(view, global::Avalonia.Input.Key.Right, KeyModifiers.Control);

        Assert.Equal(view.BlockStart + 7, view.State.Selection.Head);

        Key(view, global::Avalonia.Input.Key.Left, KeyModifiers.Control);

        Assert.Equal(view.BlockStart, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void ControlAselectsTheWholeBlock()
    {
        var view = Focused("hello");

        Key(view, global::Avalonia.Input.Key.A, KeyModifiers.Control);

        Assert.Equal(view.BlockStart, view.State.Selection.From);
        Assert.Equal(view.BlockStart + view.Block!.ContentSize, view.State.Selection.To);
    }

    [AvaloniaFact]
    public void AKeyThatIsNotHandledIsLeftForSomebodyElse()
    {
        var view = Focused("hello");
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = global::Avalonia.Input.Key.F5,
            Source = view,
        };

        view.RaiseEvent(args);

        Assert.False(args.Handled);
    }

    [AvaloniaFact]
    public void MovingTheCaretMakesItSolidAgain()
    {
        // Otherwise a keystroke landing just before a blink tick vanishes on arrival, which
        // reads as a dropped key.
        var view = Focused("hello");

        typeof(RichTextView)
            .GetField("_caretVisible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(view, false);

        Key(view, global::Avalonia.Input.Key.Right);

        Assert.True(view.IsCaretVisible);
    }

    [AvaloniaFact]
    public void AMoveThatChangesNothingStillResetsTheBlink()
    {
        var view = Focused("hello");

        typeof(RichTextView)
            .GetField("_caretVisible", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(view, false);

        Key(view, global::Avalonia.Input.Key.Left);

        Assert.Equal(view.BlockStart, view.State.Selection.Head);
        Assert.True(view.IsCaretVisible);
    }

    [AvaloniaFact]
    public void MoveToClampsToTheBlock()
    {
        var view = Focused("hello");

        view.MoveTo(-100, extend: false);

        Assert.Equal(view.BlockStart, view.State.Selection.Head);

        view.MoveTo(10_000, extend: false);

        Assert.Equal(view.BlockStart + view.Block!.ContentSize, view.State.Selection.Head);
    }

    private const string Wrapping =
        "the quick brown fox jumps over the lazy dog and then keeps running well past the "
        + "edge of the available width so that the paragraph is forced onto several lines. end";

    private static (Window Window, RichTextView View) Shown(string text)
    {
        var window = new Window { Width = Width, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(text)) };

        window.Content = view;
        window.Show();

        window.Measure(new Size(Width, 200));
        window.Arrange(new Rect(0, 0, Width, 200));

        return (window, view);
    }

    private static RichTextView Focused(string text)
    {
        var (_, view) = Shown(text);

        view.Focus();

        return view;
    }

    private static void Key(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });

    private static void Select(RichTextView view, int from, int to)
    {
        view.MoveTo(view.BlockStart + from, extend: false);
        view.MoveTo(view.BlockStart + to, extend: true);
    }

    /// <summary>
    /// The demo's layout, which is the one every other test here skips: the view nested in a
    /// padded <see cref="ScrollViewer"/> rather than filling the window. Every pointer
    /// coordinate then passes through an extra translation, and getting that wrong would put
    /// clicks in the wrong paragraph while a bare-view test stayed green.
    /// </summary>
    private static (Window Window, RichTextView View) Nested(params string[] paragraphs)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)),
            MinHeight = 300,
        };

        var window = new Window
        {
            Content = new ScrollViewer { Content = view, Padding = new Thickness(24, 18, 24, 18) },
            Width = 500,
            Height = 400,
        };

        window.Show();
        window.Measure(new Size(500, 400));
        window.Arrange(new Rect(0, 0, 500, 400));

        return (window, view);
    }

    /// <summary>A point inside the given block, in window coordinates.</summary>
    private static Point In(Window window, RichTextView view, int block, double x)
    {
        var y = 0.0;

        for (var i = 0; i < block; i++)
        {
            y += view.Blocks[i].Size.Height;
        }

        y += view.Blocks[block].Size.Height / 2;

        return view.TranslatePoint(new Point(x, y), window)!.Value;
    }

    [AvaloniaFact]
    public void ADragInsideAScrollViewerSelectsTheParagraphItIsOver()
    {
        var (window, view) = Nested("alpha bravo", "charlie delta", "echo foxtrot");
        var slot = view.Slots[1];

        window.MouseDown(In(window, view, 1, 5), MouseButton.Left);
        window.MouseMove(In(window, view, 1, 45));
        window.MouseUp(In(window, view, 1, 45), MouseButton.Left);

        Assert.False(view.State.Selection.IsEmpty);
        Assert.InRange(view.State.Selection.From, slot.Start, slot.End);
        Assert.InRange(view.State.Selection.To, slot.Start, slot.End);
    }

    /// <summary>
    /// The report was that a heading applied to every paragraph rather than the selected one.
    /// This drives the whole gesture the way a user does — drag, then command — because the
    /// command's own tests set the selection directly and so cannot catch a pointer that never
    /// moved the selection off the default at all.
    /// </summary>
    [AvaloniaFact]
    public void AHeadingAppliesOnlyToTheParagraphAPointerDragCovered()
    {
        var (window, view) = Nested("alpha bravo", "charlie delta", "echo foxtrot");

        window.MouseDown(In(window, view, 1, 5), MouseButton.Left);
        window.MouseMove(In(window, view, 1, 45));
        window.MouseUp(In(window, view, 1, 45), MouseButton.Left);

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading1));

        Assert.Equal(ParagraphKind.Body, ((ParagraphNode)view.Slots[0].Node).Kind);
        Assert.Equal(ParagraphKind.Heading1, ((ParagraphNode)view.Slots[1].Node).Kind);
        Assert.Equal(ParagraphKind.Body, ((ParagraphNode)view.Slots[2].Node).Kind);
    }

    /// <summary>
    /// A fresh document's caret sits in the first paragraph, not across the whole document. If it
    /// ever spanned everything, a block command issued before the user selected anything would
    /// reformat the entire document — which is exactly what a broken pointer would look like.
    /// </summary>
    [AvaloniaFact]
    public void AFreshDocumentStartsWithACaretRatherThanEverythingSelected()
    {
        var (_, view) = Nested("alpha bravo", "charlie delta", "echo foxtrot");

        Assert.True(view.State.Selection.IsEmpty);
        Assert.InRange(view.State.Selection.Head, view.Slots[0].Start, view.Slots[0].End);
    }

    /// <summary>
    /// The pointer shape follows what is actually under the pointer. An I-beam pinned to the
    /// whole control claims the empty space below the last paragraph is text; an arrow
    /// everywhere gives no sign that the document can be selected at all. Both have been
    /// reported, so the shape is asserted from a real pointer move rather than from the
    /// predicate alone.
    /// </summary>
    [AvaloniaFact]
    public void ThePointerIsAnIBeamOverTextAndAnArrowOffIt()
    {
        var (window, view) = Nested("alpha bravo", "charlie delta", "echo foxtrot");

        window.MouseMove(In(window, view, 1, 20));
        Assert.Same(RichTextView.TextCursor, view.Cursor);

        // Below the last block. The control is nearly always taller than its document.
        var below = view.TranslatePoint(
            new Point(20, view.Blocks.Sum(b => b.Size.Height) + 40), window)!.Value;

        window.MouseMove(below);
        Assert.Same(RichTextView.ArrowCursor, view.Cursor);

        window.MouseMove(In(window, view, 0, 20));
        Assert.Same(RichTextView.TextCursor, view.Cursor);
    }

    /// <summary>
    /// Dragging keeps the I-beam even once the pointer leaves the text, because the gesture is
    /// still a selection. Flickering to an arrow at the end of a short line during a drag reads
    /// as the selection having been dropped.
    /// </summary>
    [AvaloniaFact]
    public void ADragKeepsTheIBeamEvenPastTheEndOfTheText()
    {
        var (window, view) = Nested("alpha bravo", "charlie delta", "echo foxtrot");

        window.MouseMove(In(window, view, 1, 20));
        window.MouseDown(In(window, view, 1, 20), MouseButton.Left);
        window.MouseMove(In(window, view, 1, 380));

        Assert.Same(RichTextView.TextCursor, view.Cursor);

        window.MouseUp(In(window, view, 1, 380), MouseButton.Left);
    }
}
