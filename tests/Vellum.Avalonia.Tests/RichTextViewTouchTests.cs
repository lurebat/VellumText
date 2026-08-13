using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Media;
using global::Avalonia.Interactivity;
using global::Avalonia.VisualTree;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Touch selection handles in <see cref="RichTextView"/>, per architecture 10.3 P7.
/// </summary>
/// <remarks>
/// The headless harness has no touch input at all — <c>HeadlessWindowExtensions</c> offers a mouse,
/// a keyboard and drag-and-drop and nothing else — so these raise genuine pointer events with a
/// <see cref="PointerType.Touch"/> pointer instead. That is not a simulation of the feature: the
/// events go through the real <c>OnPointerPressed</c>, and a test that drove the handles through
/// their own methods would never notice the routing being wrong.
/// </remarks>
public class RichTextViewTouchTests
{
    private const double Width = 400;
    private const double Height = 300;

    /// <summary>
    /// The window the events are addressed to.
    /// </summary>
    /// <remarks>
    /// A pointer event carries its position in the visual root's coordinates, and Avalonia 12
    /// exposes no way to get from a control back to its root — there is no <c>VisualRoot</c> and no
    /// <c>GetVisualRoot</c>. xUnit builds one instance of this class per test, so a field is safe.
    /// </remarks>
    private Window? _root;

    [AvaloniaFact]
    public void ATouchTapShowsTheHandles()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));

        Assert.True(view.AreTouchHandlesVisible);
        Assert.Single(view.TouchHandles);
    }

    [AvaloniaFact]
    public void AMouseClickDoesNotShowTheHandles()
    {
        var (window, view) = Shown();

        window.MouseDown(TextPoint(view), MouseButton.Left);
        window.MouseUp(TextPoint(view), MouseButton.Left);

        Assert.False(view.AreTouchHandlesVisible);
        Assert.Empty(view.TouchHandles);
    }

    /// <remarks>
    /// A hybrid device has both, and touch chrome left under a mouse caret is clutter that nothing
    /// would ever remove.
    /// </remarks>
    [AvaloniaFact]
    public void AMouseClickTakesTheHandlesAway()
    {
        var (window, view) = Shown();

        Touch(view, TextPoint(view));
        Assert.True(view.AreTouchHandlesVisible);

        window.MouseDown(TextPoint(view), MouseButton.Left);

        Assert.False(view.AreTouchHandlesVisible);
    }

    [AvaloniaFact]
    public void TypingTakesTheHandlesAway()
    {
        var (window, view) = Shown();

        Touch(view, TextPoint(view));
        window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);

        Assert.False(view.AreTouchHandlesVisible);
    }

    [AvaloniaFact]
    public void LosingFocusTakesTheHandlesAway()
    {
        var (window, view) = Shown();
        var other = new Button();

        ((Panel)window.Content!).Children.Add(other);
        Touch(view, TextPoint(view));

        other.Focus();

        Assert.False(view.AreTouchHandlesVisible);
    }

    [AvaloniaFact]
    public void ACaretGetsOneHandleAndASelectionTwo()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        Assert.Single(view.TouchHandles);

        view.MoveTo(view.BlockStart + 4, extend: true);
        Assert.Equal(2, view.TouchHandles.Count);
    }

    [AvaloniaFact]
    public void NoHandlesAreReportedWhileTheyAreHidden()
    {
        var (_, view) = Shown();

        view.MoveTo(view.BlockStart + 4, extend: true);

        Assert.False(view.AreTouchHandlesVisible);
        Assert.Empty(view.TouchHandles);
    }

    /// <remarks>
    /// A handle that covered the character it points at would hide exactly the thing the user is
    /// trying to aim at, which is the whole reason the gesture exists.
    /// </remarks>
    [AvaloniaFact]
    public void AHandleHangsBelowTheLineItPointsAt()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));

        var handle = Assert.Single(view.TouchHandles);
        var caret = view.CaretRect();

        Assert.Equal(caret.Bottom, handle.Top, 3);
        Assert.Equal(caret.X, handle.Center.X, 3);
        Assert.Equal(RichTextView.TouchHandleRadius * 2, handle.Width, 3);
    }

    [AvaloniaFact]
    public void TheHandlesSitUnderTheEndsOfTheSelection()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 5, extend: true);

        var handles = view.TouchHandles;

        Assert.Equal(2, handles.Count);
        Assert.True(
            handles[0].Center.X < handles[1].Center.X,
            $"the start handle is at {handles[0].Center.X} and the end at {handles[1].Center.X}");
    }

    [AvaloniaFact]
    public void DraggingTheEndHandleExtendsTheSelection()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 3, extend: true);

        var end = view.TouchHandles[1].Center;

        TouchDrag(view, end, end + new Vector(60, 0));

        Assert.Equal(view.BlockStart, view.State.Selection.From);
        Assert.True(
            view.State.Selection.To > view.BlockStart + 3,
            $"the selection only reached {view.State.Selection.To}");
    }

    /// <remarks>
    /// The point of two handles is that each one moves its own end. A drag that moved whichever end
    /// the selection happened to be anchored at would work in one direction and silently invert the
    /// selection in the other.
    /// </remarks>
    [AvaloniaFact]
    public void DraggingTheStartHandleLeavesTheEndWhereItWas()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart + 4, extend: false);
        view.MoveTo(view.BlockStart + 8, extend: true);

        var end = view.State.Selection.To;
        var start = view.TouchHandles[0].Center;

        TouchDrag(view, start, start - new Vector(40, 0));

        Assert.Equal(end, view.State.Selection.To);
        Assert.True(
            view.State.Selection.From < view.BlockStart + 4,
            $"the start only reached {view.State.Selection.From}");
    }

    /// <remarks>
    /// The selection was made head-first by the drag that produced it, so the anchor is at the end
    /// the user is now grabbing. Nothing but pinning the anchor at the far end on press stops the
    /// gesture from moving the wrong end.
    /// </remarks>
    [AvaloniaFact]
    public void DraggingTheStartHandleWorksWhenTheAnchorIsAlreadyThere()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));

        // Anchor at 8, head at 4: the selection was dragged backwards, so its anchor is its end.
        view.MoveTo(view.BlockStart + 8, extend: false);
        view.MoveTo(view.BlockStart + 4, extend: true);

        var start = view.TouchHandles[0].Center;

        TouchDrag(view, start, start - new Vector(40, 0));

        Assert.Equal(view.BlockStart + 8, view.State.Selection.To);
    }

    [AvaloniaFact]
    public void ADragIsReportedWhileItIsHappening()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 3, extend: true);

        var end = view.TouchHandles[1].Center;

        Press(view, end);
        Assert.True(view.IsDraggingTouchHandle);

        // Anything that floats beside the selection has to wait for the gesture to finish, exactly
        // as it does for a mouse drag.
        Assert.True(view.IsDragging);

        Release(view, end);
        Assert.False(view.IsDraggingTouchHandle);
    }

    [AvaloniaFact]
    public void ATouchAwayFromAHandleMovesTheCaretInstead()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 3, extend: true);

        var far = view.TouchHandles[1].Center + new Vector(0, RichTextHeight(view));

        Touch(view, far);

        Assert.False(view.IsDraggingTouchHandle);
        Assert.True(view.State.Selection.IsEmpty);
    }

    /// <remarks>
    /// A fingertip is about 9mm across and lands where the user cannot see. A target the size of
    /// the drawn circle is one nobody can hit, so the grab radius is deliberately much larger.
    /// </remarks>
    [AvaloniaFact]
    public void AHandleIsGrabbedFromWellOutsideTheCircleItIsDrawnAs()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 3, extend: true);

        var end = view.TouchHandles[1].Center;
        var outside = end + new Vector(RichTextView.TouchHandleRadius * 2, 0);

        Press(view, outside);

        Assert.True(view.IsDraggingTouchHandle);

        Release(view, outside);
    }

    /// <remarks>
    /// The selection has to follow the same place in the handle it was grabbed by. Hit-testing the
    /// raw touch point instead puts the end of the selection a whole handle below where the user is
    /// aiming, which on a one-line paragraph is past the end of the document.
    /// </remarks>
    [AvaloniaFact]
    public void GrabbingAHandleDoesNotMoveTheSelection()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 3, extend: true);

        var before = view.State.Selection;
        var end = view.TouchHandles[1].Center;

        Press(view, end);
        Move(view, end);

        Assert.Equal(before, view.State.Selection);

        Release(view, end);
    }

    [AvaloniaFact]
    public void HidingTheHandlesEndsADragInProgress()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 3, extend: true);

        var end = view.TouchHandles[1].Center;

        Press(view, end);
        Assert.True(view.IsDraggingTouchHandle);

        view.HideTouchHandles();

        Assert.False(view.IsDraggingTouchHandle);
        Assert.False(view.AreTouchHandlesVisible);
    }

    /// <remarks>
    /// A handle drag does not go through <c>PositionAt</c> on the raw point, so a move arriving
    /// after the drag ended must not still be steering the selection.
    /// </remarks>
    [AvaloniaFact]
    public void AMoveAfterTheDragEndsDoesNothing()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 3, extend: true);

        var end = view.TouchHandles[1].Center;

        Press(view, end);
        Release(view, end);

        var after = view.State.Selection;

        Move(view, end + new Vector(80, 0));

        Assert.Equal(after, view.State.Selection);
    }

    /// <remarks>
    /// The handle is placed from the caret rectangle inside a paragraph, which knows nothing about
    /// where that paragraph is drawn. A single-paragraph document sits at the origin, so it cannot
    /// tell whether the block offset was added at all.
    /// </remarks>
    [AvaloniaFact]
    public void AHandleInALaterParagraphIsPlacedFurtherDown()
    {
        var (_, view) = Shown(DocumentNode.FromParagraphs("first", "second"));

        view.MoveTo(view.State.Doc.ContentSize - 1, extend: false);
        view.ShowTouchHandles();

        var handle = Assert.Single(view.TouchHandles);
        var caret = view.CaretRect();

        Assert.Equal(caret.Bottom, handle.Top, 3);
        Assert.True(
            handle.Top > RichTextView.TouchHandleRadius * 2,
            $"a handle in the second paragraph was placed at {handle.Top}");
    }

    /// <remarks>
    /// The selection has to follow the same place in the handle it was grabbed by, and a handle
    /// hangs a whole line below the text it points at. Hit-testing the raw touch point puts the end
    /// of the selection on the line below — which a one-line document cannot show, because there is
    /// no line below to land on.
    /// </remarks>
    [AvaloniaFact]
    public void GrabbingAHandleDoesNotDragTheSelectionOntoTheNextLine()
    {
        var (_, view) = Shown(DocumentNode.FromParagraphs(
            "a paragraph long enough that it has to wrap onto a second line inside the view"));

        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 5, extend: true);
        view.ShowTouchHandles();

        var before = view.State.Selection;
        var end = view.TouchHandles[1].Center;
        var firstLine = view.Block!.GetSelectionRects(0, 5)[0];

        // The handle hangs off the first line and onto the second, which is the whole problem.
        Assert.True(
            end.Y > firstLine.Bottom,
            $"the handle at {end.Y} is still on the first line, which ends at {firstLine.Bottom}");

        Press(view, end);
        Move(view, end);

        Assert.Equal(before, view.State.Selection);

        Release(view, end);
    }

    /// <remarks>
    /// The caret handle is the only way to place a caret precisely on a touch screen, where the
    /// finger covers the character being aimed at. Dragging it moves the caret rather than
    /// extending from it, which is what makes it a caret handle and not a selection end.
    /// </remarks>
    [AvaloniaFact]
    public void DraggingTheCaretHandleMovesTheCaretWithoutSelecting()
    {
        var (_, view) = Shown();

        Touch(view, TextPoint(view));

        var before = view.State.Selection.Head;
        var handle = Assert.Single(view.TouchHandles).Center;

        TouchDrag(view, handle, handle + new Vector(60, 0));

        Assert.True(view.State.Selection.IsEmpty, "dragging the caret handle made a selection");
        Assert.True(
            view.State.Selection.Head > before,
            $"the caret stayed at {view.State.Selection.Head}");
    }

    [AvaloniaFact]
    public void TheHandlesArePainted()
    {
        var doc = DocumentNode.FromParagraphs("hello there");
        var accent = Color.FromRgb(200, 30, 30);

        var without = Painted(doc, touch: false, accent);
        var with = Painted(doc, touch: true, accent);

        Assert.Equal(0, without);
        Assert.True(with > 0, "no handle pixels were painted");
    }

    private static double RichTextHeight(RichTextView view) =>
        view.Block?.Size.Height ?? 20;

    /// <summary>A point over the text of the first paragraph.</summary>
    private static Point TextPoint(RichTextView view) =>
        view.Block is { } block
            ? new Point(block.GetCaretRect(2).X, block.GetCaretRect(2).Center.Y)
            : new Point(4, 4);

    private (Window Window, RichTextView View) Shown(DocumentNode? doc = null)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(doc ?? DocumentNode.FromParagraphs("hello there world")),
        };
        var window = new Window
        {
            Content = new Panel { Children = { view } },
            Width = Width,
            Height = Height,
        };

        window.Show();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));
        view.Focus();

        _root = window;

        return (window, view);
    }

    /// <summary>Counts pixels of an exact colour after rendering, optionally with handles shown.</summary>
    private int Painted(DocumentNode doc, bool touch, Color accent)
    {
        var (window, view) = Shown(doc);

        view.CaretBrush = Brushes.Transparent;
        view.TouchHandleBrush = new SolidColorBrush(accent);
        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 5, extend: true);

        if (touch)
        {
            view.ShowTouchHandles();
        }

        var frame = window.CaptureRenderedFrame()!;
        var count = 0;

        // CaptureRenderedFrame returns Rgba8888, not BGRA: reading it the other way round counts
        // zero pixels of every colour and makes the assertion vacuous.
        foreach (var (r, g, b) in Pixels(frame))
        {
            if (r == accent.R && g == accent.G && b == accent.B)
            {
                count++;
            }
        }

        return count;
    }

    private static IEnumerable<(byte R, byte G, byte B)> Pixels(
        global::Avalonia.Media.Imaging.WriteableBitmap frame)
    {
        using var locked = frame.Lock();
        var bytes = new byte[locked.RowBytes * locked.Size.Height];

        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        for (var i = 0; i + 3 < bytes.Length; i += 4)
        {
            yield return (bytes[i], bytes[i + 1], bytes[i + 2]);
        }
    }

    private void Touch(RichTextView view, Point point)
    {
        Press(view, point);
        Release(view, point);
    }

    private void TouchDrag(RichTextView view, Point from, Point to)
    {
        Press(view, from);
        Move(view, to);
        Release(view, to);
    }

    private void Press(RichTextView view, Point point)
    {
        var root = _root!;

        view.RaiseEvent(new PointerPressedEventArgs(
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: true),
            root,
            point * ScaleTo(view, root),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            1));
    }

    private void Move(RichTextView view, Point point)
    {
        var root = _root!;

        view.RaiseEvent(new PointerEventArgs(
            InputElement.PointerMovedEvent,
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: true),
            root,
            point * ScaleTo(view, root),
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.Other),
            KeyModifiers.None));
    }

    private void Release(RichTextView view, Point point)
    {
        var root = _root!;

        view.RaiseEvent(new PointerReleasedEventArgs(
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: true),
            root,
            point * ScaleTo(view, root),
            0,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None,
            MouseButton.Left));

        view.RaiseEvent(new PointerCaptureLostEventArgs(
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Touch, isPrimary: true)));
    }

    /// <summary>
    /// The identity, expressed as a point, because the events take root coordinates and the view
    /// fills the root.
    /// </summary>
    private static Matrix ScaleTo(RichTextView view, Visual root) =>
        view.TransformToVisual(root) ?? Matrix.Identity;
}
