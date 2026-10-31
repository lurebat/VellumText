using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Media;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Selecting and resizing images in <see cref="RichTextView"/>, per architecture 10.3 P7.
/// </summary>
public class RichTextViewImageTests
{
    private const double Width = 400;
    private const double Height = 300;

    /// <summary>
    /// The whole feature stands on knowing where the picture is. The selection rectangle is not
    /// that: it is the line box, which is taller than an inline image by the descent of the text
    /// beside it, so handles placed on it hang off the bottom.
    /// </summary>
    [AvaloniaFact]
    public void AnInlineImageReportsItsOwnBoxRatherThanTheLineBox()
    {
        var (_, view) = Shown(Inline(60, 40));
        var position = view.SelectedImage ?? Image(view);

        var image = view.ImageRect(position);
        var line = view.Block!.GetSelectionRects(position - view.BlockStart, position - view.BlockStart + 1);

        Assert.NotNull(image);
        Assert.Equal(60, image!.Value.Width, 3);
        Assert.Equal(40, image.Value.Height, 3);

        // The line box is strictly taller: the image sits on the baseline and the text beside it
        // has a descent below that, so handles placed on the line box hang off the bottom.
        Assert.Single(line);
        Assert.True(
            line[0].Height > image.Value.Height,
            $"expected the line box to be taller than the image, got {line[0].Height}");
    }

    [AvaloniaFact]
    public void AnImageBoxSitsInsideTheLineItIsOn()
    {
        var (_, view) = Shown(Inline(60, 40));
        var position = Image(view);
        var image = view.ImageRect(position)!.Value;
        var line = view.Block!
            .GetSelectionRects(position - view.BlockStart, position - view.BlockStart + 1)[0];

        Assert.InRange(image.Top, line.Top - 0.5, line.Bottom);
        Assert.InRange(image.Bottom, line.Top, line.Bottom + 0.5);
        Assert.Equal(line.Left, image.Left, 3);
    }

    [AvaloniaFact]
    public void ThereIsNoImageBoxWhereThereIsNoImage()
    {
        var (_, view) = Shown(DocumentNode.FromParagraphs("no pictures here"));

        Assert.Null(view.ImageRect(view.BlockStart));
        Assert.Null(view.ImageRect(view.BlockStart + 3));
        Assert.Null(view.SelectedImage);
    }

    [AvaloniaFact]
    public void ABlockImageReportsTheWholeBlock()
    {
        var (_, view) = Shown(new DocumentNode([new BlockImageNode(new ImageEmbed("x.png", 80, 50))]));
        var rect = view.ImageRect(view.Slots[0].Start);

        Assert.NotNull(rect);
        Assert.Equal(80, rect!.Value.Width, 3);
        Assert.Equal(50, rect.Value.Height, 3);
    }

    /// <remarks>
    /// A table is one drawn block holding many paragraphs, so an image in a cell can only be
    /// found by asking the table to look inside itself.
    /// </remarks>
    [AvaloniaFact]
    public void AnImageInATableCellIsFound()
    {
        var cell = new TableCellNode([new ParagraphNode(InlineContent.FromEmbed(
            new ImageEmbed("x.png", 30, 20)))]);
        var doc = new DocumentNode([new TableNode([new TableRowNode([cell])])]);
        var (_, view) = Shown(doc);

        // The table opens at 0, the row at 1, the cell at 2 and the paragraph at 3, so the
        // paragraph's content — and the image in it — begins at 4.
        var position = 4;
        var rect = view.ImageRect(position);

        Assert.NotNull(rect);
        Assert.Equal(30, rect!.Value.Width, 3);
        Assert.Equal(20, rect.Value.Height, 3);

        // Inside the cell, not at the table's own origin: the cell's border and padding are in
        // front of it, so a rect that forgot to add them would be at zero.
        Assert.True(rect.Value.Left > 0, $"the image is at x={rect.Value.Left}");
        Assert.True(rect.Value.Top > 0, $"the image is at y={rect.Value.Top}");
        Assert.True(view.SelectImage(position));
    }

    [AvaloniaFact]
    public void ClickingAnImageSelectsIt()
    {
        var (window, view) = Shown(Inline(60, 40));
        var rect = view.ImageRect(Image(view))!.Value;

        window.MouseDown(rect.Center, MouseButton.Left);

        Assert.Equal(Image(view), view.SelectedImage);
        Assert.Equal(1, view.State.Selection.To - view.State.Selection.From);
    }

    /// <remarks>
    /// Clicking the picture must not be an edit. A selection change on the undo stack means one
    /// Ctrl+Z appears to do nothing.
    /// </remarks>
    [AvaloniaFact]
    public void SelectingAnImageIsNotUndoable()
    {
        var (_, view) = Shown(Inline(60, 40));

        Assert.True(view.SelectImage(Image(view)));
        Assert.False(view.CanUndo);
    }

    [AvaloniaFact]
    public void ABlockImageIsSelectedAsANode()
    {
        var (_, view) = Shown(new DocumentNode([new BlockImageNode(new ImageEmbed("x.png", 80, 50))]));
        var position = view.Slots[0].Start;

        Assert.True(view.SelectImage(position));
        Assert.IsType<NodeSelection>(view.State.Selection);
        Assert.Equal(position, view.SelectedImage);
    }

    [AvaloniaFact]
    public void SelectingWhereThereIsNoImageDoesNothing()
    {
        var (_, view) = Shown(DocumentNode.FromParagraphs("plain text"));
        var before = view.State;

        Assert.False(view.SelectImage(view.BlockStart + 2));
        Assert.Same(before, view.State);
    }

    /// <remarks>
    /// The state is derived from the selection's length, so one selected <em>character</em> is
    /// exactly the shape that could be mistaken for a selected image.
    /// </remarks>
    [AvaloniaFact]
    public void OneSelectedCharacterIsNotASelectedImage()
    {
        var (_, view) = Shown(DocumentNode.FromParagraphs("plain text"));

        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 1, extend: true);

        Assert.Equal(1, view.State.Selection.To - view.State.Selection.From);
        Assert.Null(view.SelectedImage);
    }

    /// <remarks>
    /// The image and the text beside it are on the same line, so a hit test alone cannot tell
    /// them apart — the box has to be consulted.
    /// </remarks>
    [AvaloniaFact]
    public void ClickingTheTextBesideAnImageDoesNotSelectIt()
    {
        var (window, view) = Shown(Inline(60, 40));
        var rect = view.ImageRect(Image(view))!.Value;

        window.MouseDown(new Point(rect.Left / 2, rect.Center.Y), MouseButton.Left);

        Assert.Null(view.SelectedImage);
    }

    /// <remarks>
    /// Every block above it has to be accounted for, or a handle is drawn on the wrong picture.
    /// </remarks>
    [AvaloniaFact]
    public void AnImageInALaterBlockIsReportedBelowTheOneAboveIt()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromText("first")),
            new ParagraphNode(InlineContent.FromEmbed(new ImageEmbed("x.png", 60, 40)))]);
        var (_, view) = Shown(doc);

        // "first" is 5 long, so the first paragraph spans 0..6 and the second opens at 7.
        var rect = view.ImageRect(8);

        Assert.NotNull(rect);
        Assert.True(
            rect!.Value.Top >= view.BlockOrigin(1).Y,
            $"the image is at {rect.Value.Top} but its block starts at {view.BlockOrigin(1).Y}");
    }

    [AvaloniaFact]
    public void HandlesAppearOnlyWhenAnImageIsSelected()
    {
        var plain = Pixels(Inline(60, 40), select: false);
        var selected = Pixels(Inline(60, 40), select: true);

        Assert.NotEqual(plain, selected);
    }

    /// <remarks>
    /// The handles are drawn over the text, not under it: half of each one lies outside the
    /// picture, on whatever is beside it, and a handle hidden behind a glyph cannot be aimed at.
    /// </remarks>
    [AvaloniaFact]
    public void HandlesAreDrawnOverTheText()
    {
        var pixels = Pixels(Inline(60, 40), select: true, handleBrush: Brushes.Magenta);

        Assert.True(CountOf(pixels, Colors.Magenta) > 0, "no handle was painted");
    }

    [AvaloniaFact]
    public void ThePointerOverAHandleSaysItCanBeDragged()
    {
        var (window, view) = Shown(Inline(60, 40));

        view.SelectImage(Image(view));

        var rect = view.ImageRect(Image(view))!.Value;

        // The middle of a selected picture picks it up and moves it.
        window.MouseMove(rect.Center);
        Assert.Same(RichTextView.MoveCursor, view.Cursor);

        window.MouseMove(rect.TopLeft);
        Assert.Same(RichTextView.HandleCursors[0], view.Cursor);

        window.MouseMove(rect.TopRight);
        Assert.Same(RichTextView.HandleCursors[1], view.Cursor);

        window.MouseMove(rect.BottomRight);
        Assert.Same(RichTextView.HandleCursors[2], view.Cursor);

        window.MouseMove(rect.BottomLeft);
        Assert.Same(RichTextView.HandleCursors[3], view.Cursor);
    }

    /// <remarks>
    /// A handle straddles the corner rather than sitting inside it, which is what makes a small
    /// image resizable at all — handles wholly inside a 16pt icon would cover the picture.
    /// </remarks>
    [AvaloniaFact]
    public void AHandleStraddlesTheCornerItIsOn()
    {
        var (window, view) = Shown(Inline(60, 40));

        view.SelectImage(Image(view));

        var rect = view.ImageRect(Image(view))!.Value;

        window.MouseMove(rect.BottomLeft + new Vector(-2, 2));

        Assert.Same(RichTextView.HandleCursors[3], view.Cursor);
    }

    [AvaloniaFact]
    public void DraggingACornerResizesTheImage()
    {
        var (window, view) = Shown(Inline(60, 40));
        var rect = view.ImageRect(Image(view))!.Value;

        view.SelectImage(Image(view));

        window.MouseDown(rect.BottomRight, MouseButton.Left);
        window.MouseMove(rect.BottomRight + new Vector(60, 0));
        window.MouseUp(rect.BottomRight + new Vector(60, 0), MouseButton.Left);

        var image = Embed(view);

        Assert.Equal(120, image.Width!.Value, 1);

        // The aspect ratio is kept: a corner drag that changed it independently on each axis
        // would distort a picture for a gesture nobody makes deliberately.
        Assert.Equal(80, image.Height!.Value, 1);
    }

    [AvaloniaFact]
    public void DraggingHoldsTheOppositeCornerStill()
    {
        var (window, view) = Shown(Inline(60, 40));
        var rect = view.ImageRect(Image(view))!.Value;

        view.SelectImage(Image(view));

        window.MouseDown(rect.TopLeft, MouseButton.Left);
        window.MouseMove(rect.TopLeft + new Vector(-30, 0));
        window.MouseUp(rect.TopLeft + new Vector(-30, 0), MouseButton.Left);

        // Dragging the top-left corner leftwards grows the image: the bottom-right is the anchor.
        Assert.Equal(90, Embed(view).Width!.Value, 1);
    }

    [AvaloniaFact]
    public void DraggingIsOneUndoStep()
    {
        var (window, view) = Shown(Inline(60, 40));
        var before = view.State.Doc;
        var rect = view.ImageRect(Image(view))!.Value;

        view.SelectImage(Image(view));

        window.MouseDown(rect.BottomRight, MouseButton.Left);
        window.MouseMove(rect.BottomRight + new Vector(20, 0));
        window.MouseMove(rect.BottomRight + new Vector(40, 0));
        window.MouseMove(rect.BottomRight + new Vector(60, 0));
        window.MouseUp(rect.BottomRight + new Vector(60, 0), MouseButton.Left);

        Assert.True(view.Undo());
        Assert.Equal(before, view.State.Doc);
    }

    /// <remarks>
    /// A moving pointer must not write anything until it is released, or a single gesture leaves
    /// a hundred steps on the undo stack and reflows the document under the pointer.
    /// </remarks>
    [AvaloniaFact]
    public void NothingIsWrittenUntilThePointerIsReleased()
    {
        var (window, view) = Shown(Inline(60, 40));
        var before = view.State.Doc;
        var rect = view.ImageRect(Image(view))!.Value;

        view.SelectImage(Image(view));

        window.MouseDown(rect.BottomRight, MouseButton.Left);
        window.MouseMove(rect.BottomRight + new Vector(60, 0));

        Assert.True(view.IsResizingImage);
        Assert.Same(before, view.State.Doc);

        window.MouseUp(rect.BottomRight + new Vector(60, 0), MouseButton.Left);

        Assert.False(view.IsResizingImage);
        Assert.NotSame(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void PressingAHandleWithoutMovingChangesNothing()
    {
        var (window, view) = Shown(Inline(60, 40));
        var before = view.State;
        var rect = view.ImageRect(Image(view))!.Value;

        view.SelectImage(Image(view));

        window.MouseDown(rect.BottomRight, MouseButton.Left);
        window.MouseUp(rect.BottomRight, MouseButton.Left);

        Assert.False(view.CanUndo);
        Assert.Same(before.Doc, view.State.Doc);
    }

    /// <remarks>
    /// Dragging a corner past the opposite one would otherwise ask for a zero or negative
    /// dimension, which <see cref="ImageEmbed"/> refuses outright — the gesture would throw
    /// rather than doing something sensible.
    /// </remarks>
    [AvaloniaFact]
    public void DraggingPastTheOppositeCornerStopsAtTheMinimum()
    {
        var (window, view) = Shown(Inline(60, 40));
        var rect = view.ImageRect(Image(view))!.Value;

        view.SelectImage(Image(view));

        window.MouseDown(rect.BottomRight, MouseButton.Left);
        window.MouseMove(rect.TopLeft - new Vector(200, 200));
        window.MouseUp(rect.TopLeft - new Vector(200, 200), MouseButton.Left);

        Assert.Equal(RichTextView.MinimumImageSide, Embed(view).Width!.Value, 1);
        Assert.True(Embed(view).Height!.Value >= RichTextView.MinimumImageSide);
    }

    [AvaloniaFact]
    public void AResizedImageStaysSelected()
    {
        var (_, view) = Shown(Inline(60, 40));

        view.SelectImage(Image(view));

        Assert.True(view.ResizeSelectedImage(new Size(100, 70)));
        Assert.Equal(Image(view), view.SelectedImage);
    }

    [AvaloniaFact]
    public void ResizingRefusesToShrinkBelowTheMinimum()
    {
        var (_, view) = Shown(Inline(60, 40));

        view.SelectImage(Image(view));

        Assert.True(view.ResizeSelectedImage(new Size(1, 1)));
        Assert.Equal(RichTextView.MinimumImageSide, Embed(view).Width!.Value, 3);
        Assert.Equal(RichTextView.MinimumImageSide, Embed(view).Height!.Value, 3);
    }

    /// <remarks>
    /// Two deliberate resizes are two things the user did, so one Ctrl+Z has to undo one of them.
    /// A resize typed as an ordinary edit would coalesce with the one before it.
    /// </remarks>
    [AvaloniaFact]
    public void TwoResizesAreTwoUndoSteps()
    {
        var (_, view) = Shown(Inline(60, 40));

        view.SelectImage(Image(view));
        view.ResizeSelectedImage(new Size(100, 70));
        view.ResizeSelectedImage(new Size(120, 90));

        Assert.True(view.Undo());
        Assert.Equal(100, Embed(view).Width!.Value, 1);

        Assert.True(view.Undo());
        Assert.Equal(60, Embed(view).Width!.Value, 1);
    }

    [AvaloniaFact]
    public void ABlockImageResizes()
    {
        var (_, view) = Shown(new DocumentNode([new BlockImageNode(new ImageEmbed("x.png", 80, 50))]));

        view.SelectImage(view.Slots[0].Start);

        Assert.True(view.ResizeSelectedImage(new Size(120, 75)));

        var block = Assert.IsType<BlockImageNode>(view.State.Doc.Children[0]);

        Assert.Equal(120, block.Image.Width!.Value, 1);
        Assert.Equal(75, block.Image.Height!.Value, 1);
        Assert.IsType<NodeSelection>(view.State.Selection);
    }

    /// <remarks>
    /// The alignment is an attribute of the block, not of the picture, and losing it on a resize
    /// would silently re-lay-out the page.
    /// </remarks>
    [AvaloniaFact]
    public void ResizingABlockImageKeepsItsAlignment()
    {
        var doc = new DocumentNode(
            [new BlockImageNode(new ImageEmbed("x.png", 80, 50), TextAlign.Center)]);
        var (_, view) = Shown(doc);

        view.SelectImage(view.Slots[0].Start);
        view.ResizeSelectedImage(new Size(120, 75));

        Assert.Equal(TextAlign.Center, ((BlockImageNode)view.State.Doc.Children[0]).Align);
    }

    /// <remarks>
    /// An inline image carries the marks of the text around it. Rewriting it through a slice that
    /// forgot them would drop a link, or the colour of the run it sits in.
    /// </remarks>
    [AvaloniaFact]
    public void ResizingAnInlineImageKeepsItsMarks()
    {
        var mark = new MarkSet { Style = TextStyle.Bold };
        var doc = new DocumentNode([new ParagraphNode(
            InlineContent.FromText("a", mark)
                .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 60, 40), mark))
                .Concat(InlineContent.FromText("b", mark)))]);
        var (_, view) = Shown(doc);

        view.SelectImage(Image(view));
        view.ResizeSelectedImage(new Size(30, 20));

        var paragraph = (ParagraphNode)view.State.Doc.Children[0];

        Assert.True(paragraph.Content.MarkAt(1).Has(TextStyle.Bold));
    }

    [AvaloniaFact]
    public void ResizingRefusesASizeThatIsNotANumber()
    {
        var (_, view) = Shown(Inline(60, 40));

        view.SelectImage(Image(view));

        Assert.False(view.ResizeSelectedImage(new Size(double.NaN, 40)));
        Assert.False(view.ResizeSelectedImage(new Size(60, double.PositiveInfinity)));
        Assert.Equal(60, Embed(view).Width!.Value, 1);
    }

    [AvaloniaFact]
    public void ResizingWithNothingSelectedDoesNothing()
    {
        var (_, view) = Shown(Inline(60, 40));

        Assert.False(view.ResizeSelectedImage(new Size(100, 70)));
    }

    /// <remarks>
    /// A handle sits half outside the picture, over the text beside it. Without checking the
    /// handles first, pressing one places a caret and drops the selection that put the handle on
    /// screen — the image would be impossible to resize by its outer corners.
    /// </remarks>
    [AvaloniaFact]
    public void PressingAHandleDoesNotMoveTheCaret()
    {
        var (window, view) = Shown(Inline(60, 40));
        var rect = view.ImageRect(Image(view))!.Value;

        view.SelectImage(Image(view));

        window.MouseDown(rect.TopLeft, MouseButton.Left);

        Assert.Equal(Image(view), view.SelectedImage);
        Assert.False(view.IsDragging);
    }

    /// <remarks>
    /// Shift-clicking is how a selection is extended across a picture, so it has to reach the
    /// text path rather than being swallowed by the image one.
    /// </remarks>
    [AvaloniaFact]
    public void ShiftClickingAnImageExtendsTheSelectionInstead()
    {
        var (window, view) = Shown(Inline(60, 40));
        var rect = view.ImageRect(Image(view))!.Value;

        view.MoveTo(view.BlockStart, extend: false);

        window.MouseDown(rect.Center, MouseButton.Left, RawInputModifiers.Shift);

        Assert.Equal(view.BlockStart, view.State.Selection.Anchor);
        Assert.True(view.State.Selection.To > view.State.Selection.From);
        Assert.Null(view.SelectedImage);
    }

    [AvaloniaFact]
    public void DeletingASelectedImageRemovesIt()
    {
        var (_, view) = Shown(Inline(60, 40));

        view.SelectImage(Image(view));

        Assert.True(view.DeleteSelection());
        Assert.Equal("ab", ((ParagraphNode)view.State.Doc.Children[0]).Content.Text);
    }

    /// <remarks>
    /// The picture sits <em>on</em> the baseline, so a small one beside ordinary text is pushed
    /// down rather than hung from the top of the line. Arithmetic that ignored the baselines would
    /// be indistinguishable on an image that is itself the tallest thing on the line.
    /// </remarks>
    [AvaloniaFact]
    public void ASmallImageSitsOnTheTextBaseline()
    {
        var (_, view) = Shown(Inline(8, 8));
        var rect = view.ImageRect(Image(view))!.Value;
        var line = view.Block!.GetSelectionRects(1, 2)[0];

        Assert.True(
            rect.Top > line.Top + 1,
            $"an 8pt image on a {line.Height}pt line was placed at its very top");
        Assert.True(rect.Bottom <= line.Bottom + 0.5);
    }

    /// <remarks>
    /// Every line above has to be stepped over. An image on the first line is the one case where
    /// forgetting to do so cannot be seen.
    /// </remarks>
    [AvaloniaFact]
    public void AnImageOnALaterLineIsReportedLowerDown()
    {
        // 350pt of picture cannot follow the text on a 400pt line, so it wraps and is the first
        // thing on the second line — which is also where an off-by-one in the line search shows.
        var doc = new DocumentNode([new ParagraphNode(
            InlineContent.FromText("some words that fill the line ")
                .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", 350, 40))))]);
        var (_, view) = Shown(doc);
        var first = view.Block!.GetSelectionRects(0, 1)[0];
        var rect = view.ImageRect(view.BlockStart + 30);

        Assert.True(view.Block.Layout!.TextLines.Count > 1, "the paragraph did not wrap");
        Assert.NotNull(rect);
        Assert.True(
            rect!.Value.Top >= first.Bottom - 0.5,
            $"an image on the second line was reported at {rect.Value.Top}, "
                + $"but the first line ends at {first.Bottom}");
    }

    [AvaloniaFact]
    public void AskingBeyondTheDocumentIsNull()
    {
        var (_, view) = Shown(Inline(60, 40));

        Assert.Null(view.ImageRect(10_000));
        Assert.Null(view.ImageRect(-1));
    }

    /// <remarks>
    /// A selection that merely <em>contains</em> the image is an ordinary text selection — it has
    /// text in it too, and putting resize handles on it would be lying about what Delete does.
    /// </remarks>
    [AvaloniaFact]
    public void ASelectionAroundAnImageIsNotASelectedImage()
    {
        var (_, view) = Shown(Inline(60, 40));

        view.MoveTo(Image(view), extend: false);
        view.MoveTo(Image(view) + 2, extend: true);

        Assert.Equal(2, view.State.Selection.To - view.State.Selection.From);
        Assert.Null(view.SelectedImage);
    }

    private static DocumentNode Inline(double width, double height) =>
        new([new ParagraphNode(
            InlineContent.FromText("a")
                .Concat(InlineContent.FromEmbed(new ImageEmbed("x.png", width, height)))
                .Concat(InlineContent.FromText("b")))]);

    /// <summary>The document position of the image in a document built by <see cref="Inline"/>.</summary>
    private static int Image(RichTextView view) => view.BlockStart + 1;

    private static ImageEmbed Embed(RichTextView view)
    {
        var paragraph = (ParagraphNode)view.State.Doc.Children[0];

        Assert.True(paragraph.Content.TryGetEmbedAt(1, out var embed));

        return Assert.IsType<ImageEmbed>(embed);
    }

    private static (Window Window, RichTextView View) Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        return (window, view);
    }

    /// <summary>Renders a document and returns the raw RGBA pixels.</summary>
    private static byte[] Pixels(
        DocumentNode doc,
        bool select,
        IBrush? handleBrush = null)
    {
        var (window, view) = Shown(doc);

        view.CaretBrush = Brushes.Transparent;

        if (handleBrush is not null)
        {
            view.ImageHandleBrush = handleBrush;
        }

        if (select)
        {
            view.SelectImage(view.BlockStart + 1);
        }

        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");

        return RenderedPixels.Rgba(frame);
    }

    /// <summary>Counts the pixels painted in exactly <paramref name="colour"/>.</summary>
    private static int CountOf(byte[] rgba, Color colour)
    {
        var found = 0;

        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i] == colour.R
                && rgba[i + 1] == colour.G
                && rgba[i + 2] == colour.B
                && rgba[i + 3] == colour.A)
            {
                found++;
            }
        }

        return found;
    }
}
