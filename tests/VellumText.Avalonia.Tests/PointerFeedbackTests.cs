using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What the pointer says before a press, and what a drag shows before the button comes up.
/// </summary>
public class PointerFeedbackTests
{
    private const int Width = 400;
    private const int Height = 200;

    // ------------------------------------------------------------------ hover cursors

    [AvaloniaFact]
    public void OverSelectedTextThePointerSaysTheSelectionCanBeDragged()
    {
        // Word's and TextEdit's arrow over a selection: the press that follows picks the text up
        // rather than starting a new selection, and an I-beam promised the opposite.
        var (window, view) = Shown("alpha bravo charlie delta");

        view.MoveTo(view.BlockStart + 6, extend: false);
        view.MoveTo(view.BlockStart + 19, extend: true);

        window.MouseMove(At(view, 9));
        Assert.Same(RichTextView.DraggableCursor, view.Cursor);

        window.MouseMove(At(view, 2));
        Assert.Same(RichTextView.TextCursor, view.Cursor);
    }

    [AvaloniaFact]
    public void AReadOnlySelectionKeepsTheIBeam()
    {
        // A read-only view refuses the pickup, so it must not offer it.
        var (window, view) = Shown("alpha bravo charlie delta");

        view.IsReadOnly = true;
        view.MoveTo(view.BlockStart + 6, extend: false);
        view.MoveTo(view.BlockStart + 19, extend: true);

        window.MouseMove(At(view, 9));
        Assert.Same(RichTextView.TextCursor, view.Cursor);
    }

    [AvaloniaFact]
    public void OverASelectedPictureThePointerSaysItMoves()
    {
        var (window, view) = Shown(new DocumentNode([new ParagraphNode(
            InlineContent.FromText("a")
                .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 60, 40)))
                .Concat(InlineContent.FromText("b")))]));

        view.SelectImage(view.BlockStart + 1);

        var rect = view.ImageRect(view.BlockStart + 1)!.Value;

        window.MouseMove(rect.Center);
        Assert.Same(RichTextView.MoveCursor, view.Cursor);

        // The handles still win at the corners.
        window.MouseMove(rect.TopLeft);
        Assert.Same(RichTextView.HandleCursors[0], view.Cursor);
    }

    [AvaloniaFact]
    public void WhileSelectedTextIsBeingMovedThePointerSaysSo()
    {
        var (window, view) = Shown("alpha bravo charlie delta echo");

        view.MoveTo(view.BlockStart + 6, extend: false);
        view.MoveTo(view.BlockStart + 11, extend: true);

        window.MouseMove(At(view, 8));
        window.MouseDown(At(view, 8), MouseButton.Left);
        window.MouseMove(At(view, 25));

        Assert.True(view.IsMovingSelection);
        Assert.Same(RichTextView.MovingCursor, view.Cursor);

        window.MouseUp(At(view, 25), MouseButton.Left);

        Assert.False(view.IsMovingSelection);
        Assert.NotSame(RichTextView.MovingCursor, view.Cursor);
    }

    [AvaloniaFact]
    public void CtrlOverALinkShowsTheHandWithoutWaitingForTheMouseToMove()
    {
        var (window, view) = Shown("a link here and then plain text after it");

        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 11, extend: true);
        view.ApplyLink(new LinkMark("https://example.com"));
        view.MoveTo(view.BlockStart, extend: false);
        window.UpdateLayout();

        window.MouseMove(At(view, 4));
        Assert.Same(RichTextView.TextCursor, view.Cursor);

        Press(view, InputElement.KeyDownEvent, Key.LeftCtrl, KeyModifiers.Control);
        Assert.Same(RichTextView.HandCursor, view.Cursor);

        Press(view, InputElement.KeyUpEvent, Key.LeftCtrl, KeyModifiers.None);
        Assert.Same(RichTextView.TextCursor, view.Cursor);
    }

    [AvaloniaFact]
    public void CtrlAfterThePointerLeftChangesNothing()
    {
        var (window, view) = Shown("a link here and then plain text after it");

        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 11, extend: true);
        view.ApplyLink(new LinkMark("https://example.com"));
        view.MoveTo(view.BlockStart, extend: false);
        window.UpdateLayout();

        window.MouseMove(At(view, 4));
        view.RaiseEvent(new PointerEventArgs(
            InputElement.PointerExitedEvent, view, new global::Avalonia.Input.Pointer(1, PointerType.Mouse, true), window,
            default, 0, new PointerPointProperties(), KeyModifiers.None));

        var before = view.Cursor;

        Press(view, InputElement.KeyDownEvent, Key.LeftCtrl, KeyModifiers.Control);
        Assert.Same(before, view.Cursor);
    }

    // ------------------------------------------------------------------ external drop indicator

    [AvaloniaFact]
    public void ADragFromOutsideShowsWhereItWouldLand()
    {
        var (window, view) = Shown("alpha bravo charlie delta");

        Assert.Empty(Marks(window));

        var over = Boundary(view, 11);

        DragOver(view, over);

        var columns = Marks(window);
        var expected = Math.Round(view.BlockOrigin(0).X + view.Blocks[0].GetCaretRect(11).X);

        Assert.NotEmpty(columns);
        Assert.Contains(columns, x => Math.Abs(x - expected) <= 1);
    }

    [AvaloniaFact]
    public void TheDropCaretFollowsTheDrag()
    {
        var (window, view) = Shown("alpha bravo charlie delta");

        DragOver(view, At(view, 3));
        var first = Marks(window).Min();

        DragOver(view, At(view, 20));
        var second = Marks(window).Min();

        Assert.True(second > first + 20, $"the drop caret stayed at {first}, {second}");
    }

    [AvaloniaFact]
    public void TheDropCaretGoesWhenTheDragLeaves()
    {
        var (window, view) = Shown("alpha bravo charlie delta");

        DragOver(view, At(view, 11));
        Assert.NotEmpty(Marks(window));

        view.RaiseEvent(new DragEventArgs(DragDrop.DragLeaveEvent, Text(), view, default, KeyModifiers.None));

        Assert.Empty(Marks(window));
    }

    [AvaloniaFact]
    public async Task TheDropCaretGoesWhenTheDropLands()
    {
        var (window, view) = Shown("alpha bravo charlie delta");

        DragOver(view, At(view, 11));
        view.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, Text(), view, At(view, 11), KeyModifiers.None));
        await view.PendingDrop;

        // The view is not focused, so the only caret-coloured mark there could be is the drop caret.
        Assert.Empty(Marks(window));
    }

    [AvaloniaFact]
    public void AReadOnlyViewShowsNoDropCaret()
    {
        // It refuses the drop, so it must not show where the drop would go.
        var (window, view) = Shown("alpha bravo charlie delta");

        view.IsReadOnly = true;
        DragOver(view, At(view, 11));

        Assert.Empty(Marks(window));
    }

    // ------------------------------------------------------------------ helpers

    private static (Window Window, RichTextView View) Shown(string text) =>
        Shown(DocumentNode.FromParagraphs(text));

    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc), CaretBrush = Brushes.Red };
        var window = new Window { Content = view, Width = Width, Height = Height, Background = Brushes.White };

        TextOptions.SetTextRenderingMode(window, TextRenderingMode.Antialias);
        window.Show();
        window.UpdateLayout();

        return (window, view);
    }

    /// <summary>The middle of one character of the first paragraph, in the view's coordinates.</summary>
    private static Point At(RichTextView view, int character)
    {
        var block = view.Blocks[0];
        var left = block.GetCaretRect(character);
        var right = block.GetCaretRect(character + 1);

        return view.BlockOrigin(0) + new Point((left.X + right.X) / 2, left.Center.Y);
    }

    /// <summary>Just past the boundary before a character, so the nearest position is that boundary.</summary>
    private static Point Boundary(RichTextView view, int character)
    {
        var caret = view.Blocks[0].GetCaretRect(character);

        return view.BlockOrigin(0) + new Point(caret.X + 1, caret.Center.Y);
    }

    private static void Press(RichTextView view, RoutedEvent<KeyEventArgs> routed, Key key, KeyModifiers modifiers) =>
        view.RaiseEvent(new KeyEventArgs { RoutedEvent = routed, Key = key, KeyModifiers = modifiers });

    private static DataTransfer Text()
    {
        var data = new DataTransfer();

        data.Add(DataTransferItem.CreateText("dropped"));

        return data;
    }

    private static void DragOver(RichTextView view, Point point) =>
        view.RaiseEvent(new DragEventArgs(DragDrop.DragOverEvent, Text(), view, point, KeyModifiers.None));

    /// <summary>The columns holding a pixel of the caret's red.</summary>
    private static List<int> Marks(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");

        var pixels = RenderedPixels.Rgba(frame);
        var w = frame.PixelSize.Width;
        var h = frame.PixelSize.Height;
        var columns = new List<int>();

        for (var x = 0; x < w; x++)
        {
            for (var y = 0; y < h; y++)
            {
                var i = ((y * w) + x) * 4;

                if (pixels[i] > 200 && pixels[i + 1] < 60 && pixels[i + 2] < 60)
                {
                    columns.Add(x);
                    break;
                }
            }
        }

        return columns;
    }
}
