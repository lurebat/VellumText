using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Moving the selected content by dragging it with the pointer.
/// </summary>
/// <remarks>
/// <para>
/// Every range here is deliberately asymmetric. A move of three characters three characters to
/// the right reads the same whether or not the drop point was mapped through the deletion, so
/// each test puts the source and the target at different distances from each other and from the
/// ends of the paragraph, and asserts the whole resulting string rather than a length.
/// </para>
/// <para>
/// Distances in pixels are literals rather than reads of the control's own threshold, which is
/// the thing being pinned: a test stated in terms of the constant agrees with whatever value the
/// constant takes, including zero.
/// </para>
/// </remarks>
public class RichTextViewDragMoveTests
{
    private const string Alphabet = "abcdefghijklmnop";

    [AvaloniaFact]
    public void DraggingASelectionForwardMovesIt()
    {
        var (window, view) = Shown(Alphabet);

        Select(view, 2, 5);
        Drag(window, At(view, 2), At(view, 11));

        Assert.Equal("aefghijkbcdlmnop", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void DraggingASelectionBackwardMovesIt()
    {
        var (window, view) = Shown(Alphabet);

        Select(view, 11, 14);
        Drag(window, At(view, 11), At(view, 2));

        Assert.Equal("abklmcdefghijnop", DocumentText.Of(view.State.Doc));
    }

    /// <summary>
    /// The moved content stays selected, which is what makes a second drag of the same text
    /// possible without hunting for it again.
    /// </summary>
    [AvaloniaFact]
    public void TheMovedContentIsSelectedWhereItLands()
    {
        var (window, view) = Shown(Alphabet);

        Select(view, 2, 5);
        Drag(window, At(view, 2), At(view, 11));

        Assert.Equal("bcd", view.SelectedText());
    }

    /// <summary>
    /// One transaction, so one undo. Two would let an undo stop between the delete and the
    /// insert, leaving the text in neither place.
    /// </summary>
    [AvaloniaFact]
    public void OneUndoPutsBothHalvesBack()
    {
        var (window, view) = Shown(Alphabet);

        Select(view, 2, 5);
        Drag(window, At(view, 2), At(view, 11));

        Assert.True(view.CanUndo);

        view.Undo();

        Assert.Equal(Alphabet, DocumentText.Of(view.State.Doc));
        Assert.False(view.CanUndo);
    }

    /// <summary>
    /// Dropping inside what is being dragged is a no-op, not a delete followed by an insert of
    /// the text into the hole it just left.
    /// </summary>
    [AvaloniaFact]
    public void DroppingInsideTheDraggedRangeChangesNothing()
    {
        var (window, view) = Shown(Alphabet);
        var doc = view.State.Doc;

        Select(view, 4, 13);
        Drag(window, At(view, 4), At(view, 9));

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.CanUndo);
        Assert.Equal(4, view.State.Selection.From);
        Assert.Equal(13, view.State.Selection.To);
    }

    /// <summary>
    /// A drop exactly on either end of the dragged range is a no-op too: the text would land where
    /// it already is, and the move would still cost an undo step. Paragraph offsets 3 and 12 are
    /// positions 4 and 13, the selection's two ends.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(3)]
    [InlineData(12)]
    public void DroppingOnEitherEndOfTheDraggedRangeChangesNothing(int end)
    {
        var (window, view) = Shown(Alphabet);
        var doc = view.State.Doc;

        Select(view, 4, 13);
        Drag(window, At(view, 8), At(view, end));

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.CanUndo);
    }

    /// <summary>
    /// The deferred decision, resolved the other way: a press inside a selection that is released
    /// without moving is a click, and a click collapses the caret where it landed.
    /// </summary>
    [AvaloniaFact]
    public void PressingInsideASelectionAndReleasingCollapsesTheCaret()
    {
        var (window, view) = Shown(Alphabet);

        Select(view, 2, 5);

        window.MouseDown(At(view, 3), MouseButton.Left);
        window.MouseUp(At(view, 3), MouseButton.Left);

        Assert.True(view.State.Selection.IsEmpty);
        Assert.Equal(4, view.State.Selection.From);
    }

    /// <summary>
    /// A click that changed nothing must not put a transaction on the undo stack, which is the
    /// same rule the column resize follows for a press and release on a boundary.
    /// </summary>
    [AvaloniaFact]
    public void AClickInsideASelectionPutsNothingOnTheUndoStack()
    {
        var (window, view) = Shown(Alphabet);
        var doc = view.State.Doc;

        Select(view, 2, 5);

        window.MouseDown(At(view, 3), MouseButton.Left);
        window.MouseUp(At(view, 3), MouseButton.Left);

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.CanUndo);
    }

    /// <remarks>
    /// Three pixels and six, either side of a threshold of four. Both are asserted in the one
    /// test because what is being pinned is the boundary between them, not either value alone.
    /// </remarks>
    [AvaloniaFact]
    public void TheGestureBecomesAMoveOnlyPastFourPixels()
    {
        var (window, view) = Shown(Alphabet);
        var press = At(view, 6);

        Select(view, 2, 13);

        window.MouseDown(press, MouseButton.Left);

        window.MouseMove(press + new Vector(3, 0));
        Assert.False(view.IsMovingSelection);

        // Exactly four is still a click. Without this the threshold could be four or five and
        // nothing here would notice.
        window.MouseMove(press + new Vector(4, 0));
        Assert.False(view.IsMovingSelection);

        window.MouseMove(press + new Vector(6, 0));
        Assert.True(view.IsMovingSelection);

        window.MouseUp(press + new Vector(6, 0), MouseButton.Left);
        Assert.False(view.IsMovingSelection);
    }

    /// <summary>
    /// A short drag inside a selection is still a click, and still collapses the caret rather
    /// than leaving the selection standing.
    /// </summary>
    [AvaloniaFact]
    public void ADragShorterThanTheThresholdIsStillAClick()
    {
        var (window, view) = Shown(Alphabet);
        var press = At(view, 3);

        Select(view, 2, 5);

        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(press + new Vector(3, 0));
        window.MouseUp(press + new Vector(3, 0), MouseButton.Left);

        Assert.True(view.State.Selection.IsEmpty);
        Assert.Equal(4, view.State.Selection.From);
    }

    /// <summary>
    /// A read-only control arms nothing. Asserting only that the document is unchanged would pass
    /// whether the gesture was refused at the press or merely at the commit, so this asserts what
    /// the press did instead: on a read-only control a drag from inside a selection is an
    /// ordinary drag-select, exactly as it was before this feature existed.
    /// </summary>
    [AvaloniaFact]
    public void ReadOnlyDragsSelectRatherThanMove()
    {
        var (window, view) = Shown(Alphabet);
        var doc = view.State.Doc;

        view.IsReadOnly = true;
        Select(view, 2, 5);
        Drag(window, At(view, 3), At(view, 11));

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.IsMovingSelection);
        Assert.Equal(4, view.State.Selection.From);
        Assert.Equal(12, view.State.Selection.To);
    }

    /// <summary>
    /// A drop that lands after the host swapped the document is dropped, not applied to whatever
    /// happens to be there now.
    /// </summary>
    /// <remarks>
    /// The replacement carries a selection at the same numbers, which is the case the guard exists
    /// for and the only one that can see it: a swap to a bare document leaves an empty selection,
    /// and the move refuses that a step earlier for a different reason.
    /// </remarks>
    [AvaloniaFact]
    public void ADocumentSwappedDuringTheDragDropsTheMove()
    {
        var (window, view) = Shown(Alphabet);
        var other = DocumentNode.FromParagraphs("somewhere else entirely");

        Select(view, 2, 5);

        window.MouseDown(At(view, 2), MouseButton.Left);
        window.MouseMove(At(view, 11));

        view.State = EditorState.Create(other, TextSelection.Create(other, 2, 5));

        window.MouseUp(At(view, 11), MouseButton.Left);

        Assert.Same(other, view.State.Doc);
    }

    /// <summary>
    /// Capture taken away is not a drop. A popup opening or the window deactivating mid-drag
    /// cancels the move rather than committing one the user never released.
    /// </summary>
    [AvaloniaFact]
    public void LosingCaptureCancelsTheMove()
    {
        var (window, view) = Shown(Alphabet);
        var doc = view.State.Doc;

        Select(view, 2, 5);

        window.MouseDown(At(view, 2), MouseButton.Left);
        window.MouseMove(At(view, 11));

        view.RaiseEvent(new PointerCaptureLostEventArgs(
            view, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true)));

        Assert.False(view.IsMovingSelection);
        Assert.Same(doc, view.State.Doc);
        Assert.Equal(2, view.State.Selection.From);
        Assert.Equal(5, view.State.Selection.To);
    }

    /// <summary>
    /// The press chain still works. A third click inside the selection the second one made must
    /// widen it to the paragraph rather than being taken for the pickup of that selection.
    /// </summary>
    [AvaloniaFact]
    public void ATripleClickInsideASelectionStillSelectsTheParagraph()
    {
        var (_, view) = Shown(Alphabet);

        Select(view, 2, 5);
        Click(view, At(view, 3), clicks: 3);

        Assert.Equal(Alphabet, view.SelectedText());
    }

    /// <summary>
    /// Shift is the extend gesture and stays one. A shift-click inside a selection moves the end
    /// of it rather than picking the selection up.
    /// </summary>
    [AvaloniaFact]
    public void ShiftClickingInsideASelectionStillExtendsIt()
    {
        var (window, view) = Shown(Alphabet);

        Select(view, 2, 13);

        window.MouseDown(At(view, 5), MouseButton.Left, RawInputModifiers.Shift);
        window.MouseUp(At(view, 5), MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal(2, view.State.Selection.From);
        Assert.Equal(6, view.State.Selection.To);
    }

    /// <remarks>
    /// Both modifiers, because the pickup is gated on there being none of them and a gate that
    /// named only one would quietly let the other through.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Shift)]
    [InlineData(RawInputModifiers.Control)]
    public void AModifiedDragInsideASelectionDoesNotMoveIt(RawInputModifiers modifier)
    {
        var (window, view) = Shown(Alphabet);
        var doc = view.State.Doc;

        Select(view, 2, 13);

        window.MouseDown(At(view, 5), MouseButton.Left, modifier);
        window.MouseMove(At(view, 14));
        window.MouseUp(At(view, 14), MouseButton.Left, modifier);

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.IsMovingSelection);
    }

    /// <summary>
    /// A drag that begins outside the selection is an ordinary drag-select. Without this the
    /// pickup could claim every press made while anything at all was selected.
    /// </summary>
    [AvaloniaFact]
    public void ADragStartingOutsideTheSelectionStillSelects()
    {
        var (window, view) = Shown(Alphabet);
        var doc = view.State.Doc;

        Select(view, 2, 5);
        Drag(window, At(view, 8), At(view, 13));

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.IsMovingSelection);
        Assert.Equal(9, view.State.Selection.From);
        Assert.Equal(14, view.State.Selection.To);
    }

    /// <summary>
    /// The drop caret is the whole affordance: without it the gesture is aimed blind, because the
    /// selection stays where it is until the button comes up.
    /// </summary>
    [AvaloniaFact]
    public void ADropCaretIsDrawnWhileTheContentIsHeld()
    {
        var accent = Color.FromRgb(255, 0, 0);
        var (window, view) = Shown(Alphabet);

        view.CaretBrush = new SolidColorBrush(accent);

        Select(view, 2, 5);

        var before = Painted(window, accent);

        window.MouseDown(At(view, 2), MouseButton.Left);
        window.MouseMove(At(view, 11));

        Assert.True(view.IsMovingSelection);

        var during = Painted(window, accent);

        window.MouseUp(At(view, 11), MouseButton.Left);

        Assert.True(
            during > before,
            $"expected the drop caret to add caret-coloured pixels, but the count went from "
                + $"{before} to {during}");
    }

    /// <summary>
    /// The drop caret is drawn at the pointer, not merely somewhere. Two holds at different
    /// offsets, because a caret pinned to one end of the selection would satisfy a test that only
    /// asked whether anything was drawn.
    /// </summary>
    [AvaloniaFact]
    public void TheDropCaretFollowsThePointer()
    {
        var accent = Color.FromRgb(255, 0, 0);

        var near = HeldAt(accent, 8);
        var far = HeldAt(accent, 14);

        Assert.NotEmpty(near);
        Assert.True(
            far[^1] > near[^1],
            $"expected the drop caret to move right with the pointer, but the rightmost "
                + $"caret-coloured column went from {near[^1]} to {far[^1]}");
    }

    /// <summary>
    /// A rectangle of cells is refused. Its From..To runs through cells outside it, so a move
    /// stated in those numbers would take whole cells out of their rows.
    /// </summary>
    [AvaloniaFact]
    public void ARectangleOfCellsIsNotPickedUp()
    {
        var (window, view) = Shown(WithTable());
        var doc = view.State.Doc;

        view.MoveTo(StartOf(doc, "a1"), extend: false);
        view.MoveTo(StartOf(doc, "b2"), extend: true);

        var rectangle = Assert.IsType<CellSelection>(view.State.Selection);
        var inside = view.Blocks[0].Size;

        window.MouseDown(new Point(inside.Width / 4, inside.Height / 2), MouseButton.Left);
        window.MouseMove(new Point((inside.Width / 4) + 30, inside.Height / 2));
        window.MouseUp(new Point((inside.Width / 4) + 30, inside.Height / 2), MouseButton.Left);

        Assert.False(view.IsMovingSelection);
        Assert.Same(doc, view.State.Doc);
        Assert.NotEqual(rectangle, view.State.Selection);
    }

    /// <summary>
    /// A move whose insert cannot be made abandons the delete with it. Applying half of it would
    /// be the content vanishing, which is worse than the gesture doing nothing.
    /// </summary>
    /// <remarks>
    /// A selection running from a paragraph into a table cell is the shape that produces it: what
    /// it cuts is open three levels deep at its trailing edge, and there is nowhere in a paragraph
    /// that can be joined to.
    /// </remarks>
    [AvaloniaFact]
    public void ASelectionReachingIntoATableIsNotMoved()
    {
        var (window, view) = Shown(WithTableBetweenParagraphs());
        var doc = view.State.Doc;

        view.MoveTo(StartOf(doc, "before the table") + 2, extend: false);
        view.MoveTo(StartOf(doc, "a1") + 1, extend: true);

        Assert.False(view.State.Selection.IsEmpty);
        Assert.IsNotType<CellSelection>(view.State.Selection);

        var press = Middle(view, 0, 60);
        var target = Middle(view, 2);

        window.MouseDown(press, MouseButton.Left);
        window.MouseMove(target);

        // The move must actually have started, or this would pass by never having tried.
        Assert.True(view.IsMovingSelection);

        window.MouseUp(target, MouseButton.Left);

        Assert.Same(doc, view.State.Doc);
        Assert.False(view.CanUndo);
    }

    /// <summary>
    /// The same gesture, on the existing node selection. A picture is picked up from its own box
    /// and lands where the drop caret was, splitting the paragraph it was dropped into.
    /// </summary>
    [AvaloniaFact]
    public void DraggingASelectedImageMovesIt()
    {
        var (window, view) = Shown(WithImageAbove());

        Assert.True(view.SelectImage(view.Slots[0].Start));

        var box = view.ImageRect(view.SelectedImage!.Value)!.Value;

        // Exactly between "after the pi" and "cture", from the laid-out text: a fixed x falls
        // between different letters in a different font, and the split asserted below moves.
        var target = view.BlockOrigin(1) + view.Blocks[1].GetCaretRect("after the pi".Length).Center;

        window.MouseDown(box.Center, MouseButton.Left);
        window.MouseMove(target);

        Assert.True(view.IsMovingSelection);

        window.MouseUp(target, MouseButton.Left);

        var children = view.State.Doc.Children;

        Assert.Equal(3, children.Length);
        Assert.IsType<ParagraphNode>(children[0]);
        Assert.IsType<BlockImageNode>(children[1]);
        Assert.IsType<ParagraphNode>(children[2]);
        Assert.Equal("after the pi", ((ParagraphNode)children[0]).Content.Text);
        Assert.Equal("cture", ((ParagraphNode)children[2]).Content.Text);
    }

    /// <summary>
    /// A moved picture stays selected, so it can be nudged again. This is the node selection, not
    /// a text range that happens to cover it.
    /// </summary>
    [AvaloniaFact]
    public void TheMovedImageIsStillSelected()
    {
        var (window, view) = Shown(WithImageAbove());

        Assert.True(view.SelectImage(view.Slots[0].Start));

        var box = view.ImageRect(view.SelectedImage!.Value)!.Value;
        var target = Middle(view, 1, 60);

        Drag(window, box.Center, target);

        // The picture started as the first block; a node selection that stayed where it was would
        // still be a node selection, so this asserts where it points rather than what it is.
        Assert.IsType<BlockImageNode>(view.State.Doc.Children[1]);
        Assert.IsType<NodeSelection>(view.State.Selection);

        var moved = view.SelectedImage;

        Assert.NotNull(moved);
        Assert.NotNull(view.ImageRect(moved!.Value));
        Assert.Equal(view.State.Selection.From, moved.Value);
    }

    /// <summary>
    /// One undo puts the picture back where it came from, for the same reason it does for text:
    /// the delete and the insert are two steps of one transaction.
    /// </summary>
    [AvaloniaFact]
    public void OneUndoPutsTheImageBack()
    {
        var (window, view) = Shown(WithImageAbove());
        var doc = view.State.Doc;

        Assert.True(view.SelectImage(view.Slots[0].Start));

        var box = view.ImageRect(view.SelectedImage!.Value)!.Value;

        Drag(window, box.Center, Middle(view, 1, 60));

        Assert.NotSame(doc, view.State.Doc);
        Assert.True(view.Undo());
        Assert.Equal(2, view.State.Doc.Children.Length);
        Assert.IsType<BlockImageNode>(view.State.Doc.Children[0]);
        Assert.Equal("after the picture", ((ParagraphNode)view.State.Doc.Children[1]).Content.Text);
    }

    /// <summary>
    /// A click on a selected picture leaves it selected. Collapsing a caret into it would drop the
    /// selection that a plain click is meant to keep, and put a picture out of reach of the delete
    /// key by clicking on it.
    /// </summary>
    [AvaloniaFact]
    public void AClickOnASelectedImageKeepsItSelected()
    {
        var (window, view) = Shown(WithImageAbove());

        Assert.True(view.SelectImage(view.Slots[0].Start));

        var position = view.SelectedImage!.Value;
        var box = view.ImageRect(position)!.Value;

        window.MouseDown(box.Center, MouseButton.Left);
        window.MouseUp(box.Center, MouseButton.Left);

        Assert.IsType<NodeSelection>(view.State.Selection);
        Assert.Equal(position, view.SelectedImage);
        Assert.False(view.CanUndo);
    }

    /// <summary>
    /// The picture is picked up from its own box, not from anywhere the node selection's two
    /// positions happen to cover. A press beside it in the same row is an ordinary press.
    /// </summary>
    [AvaloniaFact]
    public void PressingBesideASelectedImageDoesNotPickItUp()
    {
        var (window, view) = Shown(WithImageAbove());
        var doc = view.State.Doc;

        Assert.True(view.SelectImage(view.Slots[0].Start));

        var box = view.ImageRect(view.SelectedImage!.Value)!.Value;
        var beside = new Point(box.Right + 40, box.Center.Y);
        var target = Middle(view, 1, 60);

        window.MouseDown(beside, MouseButton.Left);
        window.MouseMove(target);

        Assert.False(view.IsMovingSelection);

        window.MouseUp(target, MouseButton.Left);

        Assert.Same(doc, view.State.Doc);
    }

    /// <summary>
    /// A press on a picture inside a longer text selection selects that picture, which is what it
    /// did before this gesture existed and must keep doing.
    /// </summary>
    [AvaloniaFact]
    public void AnImageInsideATextSelectionIsStillSelectedByPressingIt()
    {
        var (window, view) = Shown(new DocumentNode([
            ParagraphNode.FromText("before the picture"),
            new BlockImageNode(new ImageEmbed("x.png", 80, 50)),
            ParagraphNode.FromText("after the picture"),
        ]));
        var doc = view.State.Doc;

        Assert.True(view.SelectAll());

        Assert.False(view.State.Selection.IsEmpty);

        var box = ImageBox(view);

        window.MouseDown(box.Center, MouseButton.Left);
        window.MouseUp(box.Center, MouseButton.Left);

        Assert.Same(doc, view.State.Doc);
        Assert.IsType<NodeSelection>(view.State.Selection);
        Assert.NotNull(view.SelectedImage);
    }

    /// <summary>
    /// A drop aimed at a picture lands in the text beside it rather than between the blocks, where
    /// an open slice of text cannot go.
    /// </summary>
    [AvaloniaFact]
    public void DroppingOntoAPictureLandsInTheTextBesideIt()
    {
        var (window, view) = Shown(new DocumentNode([
            ParagraphNode.FromText("some words here"),
            new BlockImageNode(new ImageEmbed("x.png", 80, 50)),
        ]));

        Select(view, 1, 6);

        var box = ImageBox(view);

        Drag(window, At(view, 2), box.Center);

        Assert.Equal(2, view.State.Doc.Children.Length);
        Assert.Equal("words heresome ", ((ParagraphNode)view.State.Doc.Children[0]).Content.Text);
    }

    private static (Window Window, RichTextView View) Shown(string paragraph) =>
        Shown(DocumentNode.FromParagraphs(paragraph));

    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        window.Measure(new Size(500, 400));
        window.Arrange(new Rect(0, 0, 500, 400));

        view.Focus();

        return (window, view);
    }

    /// <summary>Counts pixels of an exact colour in a freshly rendered frame.</summary>
    /// <remarks>
    /// CaptureRenderedFrame hands back Rgba8888, not BGRA. Reading it the other way round counts
    /// zero pixels of every colour and makes the assertion vacuous.
    /// </remarks>
    private static int Painted(Window window, Color accent) =>
        PaintedColumns(window, accent).Sum(c => c.Count);

    /// <summary>The columns holding pixels of an exact colour, left to right.</summary>
    private static List<(int Column, int Count)> PaintedColumns(Window window, Color accent)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");
        using var locked = frame.Lock();

        var bytes = new byte[locked.RowBytes * locked.Size.Height];

        System.Runtime.InteropServices.Marshal.Copy(locked.Address, bytes, 0, bytes.Length);

        var counts = new Dictionary<int, int>();

        for (var y = 0; y < locked.Size.Height; y++)
        {
            for (var x = 0; x < locked.Size.Width; x++)
            {
                var i = (y * locked.RowBytes) + (x * 4);

                if (bytes[i] == accent.R && bytes[i + 1] == accent.G && bytes[i + 2] == accent.B)
                {
                    counts[x] = counts.GetValueOrDefault(x) + 1;
                }
            }
        }

        return [.. counts.OrderBy(p => p.Key).Select(p => (p.Key, p.Value))];
    }

    /// <summary>The caret-coloured columns while a move is held over a paragraph offset.</summary>
    private static List<int> HeldAt(Color accent, int offset)
    {
        var (window, view) = Shown(Alphabet);

        view.CaretBrush = new SolidColorBrush(accent);

        Select(view, 2, 5);

        window.MouseDown(At(view, 2), MouseButton.Left);
        window.MouseMove(At(view, offset));

        Assert.True(view.IsMovingSelection);

        return [.. PaintedColumns(window, accent).Select(c => c.Column)];
    }

    private static void Select(RichTextView view, int from, int to)
    {
        view.MoveTo(from, extend: false);
        view.MoveTo(to, extend: true);
    }

    /// <summary>A point one pixel into the character at a paragraph offset.</summary>
    /// <remarks>
    /// One pixel in rather than on the caret line, so the point is unambiguously nearer the
    /// position asked for than the one before it.
    /// </remarks>
    private static Point At(RichTextView view, int offset)
    {
        var caret = view.Block!.GetCaretRect(offset);

        return new Point(caret.X + 1, caret.Y + 2);
    }

    private static void Drag(Window window, Point from, Point to)
    {
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(to);
        window.MouseUp(to, MouseButton.Left);
    }

    /// <summary>A point a little way into a block, in the control's coordinates.</summary>
    private static Point Middle(RichTextView view, int block, double x = 8)
    {
        var y = 0d;

        for (var i = 0; i < block; i++)
        {
            y += view.Blocks[i].Size.Height;
        }

        return new Point(x, y + (view.Blocks[block].Size.Height / 2));
    }

    /// <summary>A press with a click count, which the headless input helpers cannot state.</summary>
    private static void Click(RichTextView view, Point point, int clicks) =>
        view.RaiseEvent(new PointerPressedEventArgs(
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
            view,
            point,
            0,
            new PointerPointProperties(
                RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clicks));

    private static int StartOf(DocumentNode doc, string text)
    {
        for (var position = 0; position <= doc.ContentSize; position++)
        {
            var at = doc.Resolve(position);

            if (at.Parent is ParagraphNode paragraph
                && at.ParentOffset == 0
                && paragraph.Content.Text == text)
            {
                return position;
            }
        }

        throw new InvalidOperationException($"No paragraph reading \"{text}\".");
    }

    private static DocumentNode WithTable() =>
        new([
            new TableNode([
                new TableRowNode([
                    TableCellNode.FromText("a1"),
                    TableCellNode.FromText("b1"),
                ]),
                new TableRowNode([
                    TableCellNode.FromText("a2"),
                    TableCellNode.FromText("b2"),
                ]),
            ]),
        ]);

    /// <summary>The box of the one picture in the document.</summary>
    private static Rect ImageBox(RichTextView view) =>
        view.Slots.Select(s => view.ImageRect(s.Start)).First(r => r is not null)!.Value;

    private static DocumentNode WithImageAbove() =>
        new([
            new BlockImageNode(new ImageEmbed("x.png", 80, 50)),
            ParagraphNode.FromText("after the picture"),
        ]);

    private static DocumentNode WithTableBetweenParagraphs() =>
        new([
            ParagraphNode.FromText("before the table"),
            new TableNode([
                new TableRowNode([
                    TableCellNode.FromText("a1"),
                    TableCellNode.FromText("b1"),
                ]),
            ]),
            ParagraphNode.FromText("after the table"),
        ]);
}
