using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What a list looks like: how far its items are pushed in, what is drawn in front of them, and
/// what happens to the numbering when the list changes underneath.
/// </summary>
public class RichTextViewListTests
{
    [AvaloniaFact]
    public void AListItemIsPushedInByItsDepth()
    {
        var flat = Shown(new DocumentNode([ParagraphNode.FromText("item")]));
        var listed = Shown(Bulleted("item"));

        Assert.Equal(0, Paragraph(flat, 0).Depth);
        Assert.Equal(1, Paragraph(listed, 0).Depth);
        Assert.True(
            Paragraph(listed, 0).Indent > Paragraph(flat, 0).Indent,
            "a list item was not indented past a plain paragraph");
    }

    [AvaloniaFact]
    public void ANestedListItemIsPushedInFurtherStill()
    {
        var doc = new DocumentNode([
            new ListNode([
                new ListItemNode([
                    ParagraphNode.FromText("outer"),
                    new ListNode([ListItemNode.FromText("inner")]),
                ]),
            ]),
        ]);

        var view = Shown(doc);

        Assert.Equal(1, Paragraph(view, 0).Depth);
        Assert.Equal(2, Paragraph(view, 1).Depth);
        Assert.True(Paragraph(view, 1).Indent > Paragraph(view, 0).Indent);
    }

    [AvaloniaFact]
    public void AnUnorderedItemGetsABulletAndAnOrderedOneGetsItsNumber()
    {
        var bullets = Shown(Bulleted("one", "two"));

        Assert.Equal(new ListMarker(ListKind.Unordered, 1), Paragraph(bullets, 0).Marker);
        Assert.Equal(new ListMarker(ListKind.Unordered, 2), Paragraph(bullets, 1).Marker);

        var numbers = Shown(new DocumentNode([
            new ListNode(
                [ListItemNode.FromText("one"), ListItemNode.FromText("two")],
                ListKind.Ordered,
                start: 3),
        ]));

        Assert.Equal(new ListMarker(ListKind.Ordered, 3), Paragraph(numbers, 0).Marker);
        Assert.Equal(new ListMarker(ListKind.Ordered, 4), Paragraph(numbers, 1).Marker);
    }

    [AvaloniaFact]
    public void APlainParagraphHasNoMarker() =>
        Assert.Null(Paragraph(Shown(new DocumentNode([ParagraphNode.FromText("plain")])), 0).Marker);

    [AvaloniaFact]
    public void OnlyAnItemsFirstParagraphIsMarked()
    {
        var view = Shown(new DocumentNode([
            new ListNode([
                new ListItemNode([
                    ParagraphNode.FromText("first"),
                    ParagraphNode.FromText("second"),
                ]),
            ]),
        ]));

        Assert.NotNull(Paragraph(view, 0).Marker);
        Assert.Null(Paragraph(view, 1).Marker);
        Assert.Equal(Paragraph(view, 0).Depth, Paragraph(view, 1).Depth);
    }

    [AvaloniaFact]
    public void RemovingAnItemRenumbersTheOnesBelowItEvenThoughTheirParagraphsAreUnchanged()
    {
        // The whole reason a marker is set on the view rather than read from the paragraph: the
        // surviving items are the very same node objects, so anything keyed on the paragraph
        // would leave the old numbers on screen.
        var items = new[]
        {
            ListItemNode.FromText("one"),
            ListItemNode.FromText("two"),
            ListItemNode.FromText("three"),
        };

        var view = Shown(new DocumentNode([new ListNode(items, ListKind.Ordered)]));
        var second = Paragraph(view, 1);

        Assert.Equal(2, second.Marker!.Value.Ordinal);

        view.State = EditorState.Create(new DocumentNode([
            new ListNode([items[1], items[2]], ListKind.Ordered),
        ]));

        Remeasure(view);

        Assert.Equal(1, Paragraph(view, 0).Marker!.Value.Ordinal);
        Assert.Equal(2, Paragraph(view, 1).Marker!.Value.Ordinal);
    }

    [AvaloniaFact]
    public void AMarkerIsActuallyDrawnInTheGutter()
    {
        var view = Shown(Bulleted("item"));
        var indent = Paragraph(view, 0).Indent;

        var painted = PaintedColumns(view);

        Assert.Contains(painted, x => x < indent - 1);
        Assert.Contains(painted, x => x >= indent);
    }

    [AvaloniaFact]
    public void NothingIsDrawnInTheGutterOfAPlainParagraph()
    {
        var view = Shown(new DocumentNode([ParagraphNode.FromText("item")]));
        var painted = PaintedColumns(view);

        // Same text at the same place, minus the list. Whatever appears left of a list item's
        // text must be the marker and not the text itself creeping left. Bounded by the text's
        // own laid-out width rather than by the indent step: whether "item" is narrower than one
        // indent step is a property of the font, and in a wide one it is not.
        var end = Math.Ceiling(Paragraph(view, 0).Layout.Width);

        Assert.NotEmpty(painted);
        Assert.DoesNotContain(painted, x => x > end);
    }

    [AvaloniaFact]
    public void AMarkedParagraphIsStillHitTestedFromItsOwnLeftEdge()
    {
        var view = Shown(Bulleted("item"));
        var block = Paragraph(view, 0);

        // A click on the bullet belongs to the start of the item's text, not to a position
        // computed as though the text began in the gutter.
        Assert.Equal(0, block.HitTest(new Point(1, 1)));
        Assert.Equal(block.Indent, block.GetCaretRect(0).X, 3);
    }

    [AvaloniaFact]
    public void ABulletIsDrawnAsABulletAndANumberAsANumber()
    {
        Assert.Equal("\u2022", Paragraph(Shown(Bulleted("item")), 0).MarkerText);

        var numbers = Shown(new DocumentNode([
            new ListNode([ListItemNode.FromText("item")], ListKind.Ordered, start: 3),
        ]));

        Assert.Equal("3.", Paragraph(numbers, 0).MarkerText);
        Assert.Null(Paragraph(Shown(new DocumentNode([ParagraphNode.FromText("plain")])), 0).MarkerText);
    }

    [AvaloniaFact]
    public void WrappingAParagraphInAListIndentsItEvenThoughTheParagraphIsUnchanged()
    {
        // The paragraph node survives the wrap unchanged, so the reconciler matches it as an
        // untouched prefix and never rebuilds its view. Its depth changed all the same.
        var paragraph = ParagraphNode.FromText("wrap me");
        var view = Shown(new DocumentNode([paragraph]));
        var before = Paragraph(view, 0);

        Assert.Equal(0, before.Depth);

        view.State = EditorState.Create(new DocumentNode([
            new ListNode([new ListItemNode([paragraph])]),
        ]));

        Remeasure(view);

        Assert.Same(before, view.Blocks[0]);
        Assert.Equal(1, Paragraph(view, 0).Depth);
        Assert.NotNull(Paragraph(view, 0).MarkerText);
    }

    [AvaloniaFact]
    public void WrappingAParagraphInAListRewrapsItsTextToTheNarrowerWidth()
    {
        // The indent eats into the width the text has, so a lead change is a relayout. Keeping
        // the old layout would leave the item's lines running past the right edge.
        var paragraph = ParagraphNode.FromText(string.Join(' ', Enumerable.Repeat("ab", 200)));
        var view = Shown(new DocumentNode([paragraph]));
        var before = Paragraph(view, 0).LineCount;

        view.State = EditorState.Create(new DocumentNode([
            new ListNode([new ListItemNode([
                ParagraphNode.FromText("outer"),
                new ListNode([new ListItemNode([paragraph])]),
            ])]),
        ]));

        Remeasure(view);

        var block = Paragraph(view, 1);

        Assert.Equal(2, block.Depth);
        Assert.True(
            block.LineCount > before,
            $"the item wraps into {block.LineCount} lines, the same as the {before} it had unindented");

        Assert.True(
            block.Layout.Width <= 300 - block.Indent,
            $"the item's text is {block.Layout.Width} wide inside {300 - block.Indent} of room");
    }
    private static DocumentNode Bulleted(params string[] items) =>
        new([new ListNode(items.Select(ListItemNode.FromText))]);

    private static ParagraphView Paragraph(RichTextView view, int index) =>
        (ParagraphView)view.Blocks[index];

    /// <summary>The x of every column that has anything painted in it.</summary>
    private static IReadOnlyList<double> PaintedColumns(RichTextView view)
    {
        const int width = 300;
        const int height = 200;

        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(width, height));

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context);
        }

        var pixels = new uint[width * height];

        unsafe
        {
            fixed (uint* buffer = pixels)
            {
                bitmap.CopyPixels(
                    new PixelRect(new PixelSize(width, height)),
                    (nint)buffer,
                    pixels.Length * sizeof(uint),
                    width * sizeof(uint));
            }
        }

        var columns = new List<double>();

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                if ((pixels[(y * width) + x] & 0xFF000000) != 0)
                {
                    columns.Add(x);
                    break;
                }
            }
        }

        return columns;
    }

    private static void Remeasure(RichTextView view)
    {
        view.Measure(new Size(300, 200));
        view.Arrange(new Rect(0, 0, 300, 200));
    }

    private static RichTextView Shown(DocumentNode doc)
    {
        var root = new Window { Width = 300, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(doc) };

        root.Content = view;
        root.Show();
        Remeasure(view);

        return view;
    }
}
