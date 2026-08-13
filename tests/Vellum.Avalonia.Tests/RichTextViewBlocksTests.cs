using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Tests for a view holding more than one block: where blocks are, which one a point or a
/// position belongs to, and what a selection spanning several of them draws.
/// </summary>
public class RichTextViewBlocksTests
{
    [AvaloniaFact]
    public void EveryBlockIsRealisedAndStackedInOrder()
    {
        var view = Shown(DocumentNode.FromParagraphs("one", "two", "three"));

        Assert.Equal(3, view.Blocks.Count);

        for (var i = 1; i < view.Blocks.Count; i++)
        {
            Assert.True(
                view.BlockOrigin(i).Y >= view.BlockOrigin(i - 1).Y + view.Blocks[i - 1].Size.Height - 0.01,
                $"block {i} overlaps the one above it");
        }
    }

    [AvaloniaFact]
    public void TheControlAsksForRoomForEveryBlock()
    {
        var one = Shown(DocumentNode.FromParagraphs("one"));
        var three = Shown(DocumentNode.FromParagraphs("one", "two", "three"));

        Assert.True(
            three.DesiredSize.Height > one.DesiredSize.Height * 2.5,
            $"three paragraphs asked for {three.DesiredSize.Height}, one asked for {one.DesiredSize.Height}");
    }

    [AvaloniaFact]
    public void APointInALaterBlockReportsAPositionInThatBlock()
    {
        var view = Shown(DocumentNode.FromParagraphs("first", "second", "third"));
        var third = view.BlockOrigin(2);

        var position = view.PositionAt(new Point(1, third.Y + 2));

        Assert.Equal(2, view.BlockIndexOf(position));
        Assert.Equal(view.Slots[2].Start, position);
    }

    [AvaloniaFact]
    public void ClickingIntoALaterBlockMovesTheCaretThere()
    {
        var view = Shown(DocumentNode.FromParagraphs("first", "second"));
        var second = view.Slots[1];

        view.MoveTo(second.Start + 3, extend: false);

        Assert.Equal(1, view.CaretBlockIndex);
        Assert.Equal(second.Start, view.BlockStart);
        Assert.Equal(second.Start + 3, view.State.Selection.Head);

        // And the caret is drawn where that block is, not at the top of the control.
        Assert.True(view.CaretRect().Y >= view.BlockOrigin(1).Y - 0.01);
    }

    [AvaloniaFact]
    public void APositionInTheGapBetweenBlocksBelongsToTheEarlierOne()
    {
        var view = Shown(DocumentNode.FromParagraphs("ab", "cd"));

        // "ab" ends at 3; 4 is the boundary between the two paragraphs and 5 opens the second.
        Assert.Equal(0, view.BlockIndexOf(view.Slots[0].End));
        Assert.Equal(0, view.BlockIndexOf(view.Slots[1].Start - 1));
        Assert.Equal(1, view.BlockIndexOf(view.Slots[1].Start));
    }

    [AvaloniaFact]
    public void EditingOneParagraphLeavesTheOthersViewsAlone()
    {
        // The point of matching by node reference. If this fails, an edit in a long document
        // relays out every block rather than one, which is the difference between an editor
        // that keeps up with typing and one that does not.
        var view = Shown(DocumentNode.FromParagraphs("first", "second", "third"));
        var before = view.Blocks.ToArray();

        view.MoveTo(view.Slots[1].Start + 1, extend: false);
        Assert.True(view.InsertText("X"));

        Assert.Same(before[0], view.Blocks[0]);
        Assert.Same(before[1], view.Blocks[1]);
        Assert.Same(before[2], view.Blocks[2]);
        Assert.Equal("sXecond", ((ParagraphView)view.Blocks[1]).Paragraph.Content.Text);
    }

    [AvaloniaFact]
    public void AParagraphInsertedAtTheTopDoesNotDisturbTheBlocksBelowIt()
    {
        var view = Shown(DocumentNode.FromParagraphs("first", "second"));
        var before = view.Blocks.ToArray();

        view.State = EditorState.Create(
            new DocumentNode(
                new BlockNode[] { ParagraphNode.FromText("new") }.Concat(view.State.Doc.Blocks)),
            view.State.Selection);

        view.Measure(new Size(400, 200));

        Assert.Equal(3, view.Blocks.Count);
        Assert.Same(before[0], view.Blocks[1]);
        Assert.Same(before[1], view.Blocks[2]);
    }

    [AvaloniaFact]
    public void ADeletedParagraphTakesItsViewWithIt()
    {
        var view = Shown(DocumentNode.FromParagraphs("first", "second", "third"));
        var before = view.Blocks.ToArray();

        view.State = EditorState.Create(
            new DocumentNode([view.State.Doc.Blocks[0], view.State.Doc.Blocks[2]]),
            TextSelection.Cursor(1));

        view.Measure(new Size(400, 200));

        Assert.Equal(2, view.Blocks.Count);
        Assert.Same(before[0], view.Blocks[0]);
        Assert.Same(before[2], view.Blocks[1]);
    }

    [AvaloniaFact]
    public void ARuleIsDrawnAndTakesUpRoom()
    {
        var view = Shown(new DocumentNode([
            ParagraphNode.FromText("above"),
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]));

        Assert.Equal(3, view.Blocks.Count);
        Assert.IsType<RuleView>(view.Blocks[1]);
        Assert.Equal(RuleView.BlockHeight, view.Blocks[1].Size.Height);

        // The paragraph below starts after the rule's whole height, not on top of it.
        Assert.True(view.BlockOrigin(2).Y >= view.BlockOrigin(1).Y + RuleView.BlockHeight - 0.01);
    }

    [AvaloniaFact]
    public void ACaretMovementNeverLandsOnARule()
    {
        var view = Shown(new DocumentNode([
            ParagraphNode.FromText("above"),
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]));

        var rule = view.Slots[1].Start;

        view.MoveTo(rule, extend: false);

        Assert.NotEqual(rule, view.State.Selection.Head);
        Assert.True(view.State.Doc.Resolve(view.State.Selection.Head).IsInText);
    }

    [AvaloniaFact]
    public void ASelectionSpanningBlocksIsHighlightedInEveryOneOfThem()
    {
        var view = Shown(DocumentNode.FromParagraphs("first", "second", "third"));

        view.MoveTo(view.Slots[0].Start + 2, extend: false);
        view.MoveTo(view.Slots[2].Start + 2, extend: true);

        var rows = HighlightedRows(view);

        // At least one highlighted row inside each of the three blocks. A selection that only
        // painted the block the caret is in is the classic multi-block bug.
        Assert.All(
            Enumerable.Range(0, 3),
            i => Assert.Contains(rows, y => y >= view.BlockOrigin(i).Y && y < view.BlockOrigin(i).Y + view.Blocks[i].Size.Height));
    }

    [AvaloniaFact]
    public void ABlockTheSelectionOnlyReachesTheEdgeOfIsNotHighlighted()
    {
        var view = Shown(DocumentNode.FromParagraphs("first", "second"));

        // Everything in the first paragraph and not one position more.
        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[0].End, extend: true);

        var rows = HighlightedRows(view);

        Assert.DoesNotContain(rows, y => y >= view.BlockOrigin(1).Y);
    }

    [AvaloniaFact]
    public void ASelectionAcrossARuleHighlightsTheWholeRule()
    {
        var view = Shown(new DocumentNode([
            ParagraphNode.FromText("above"),
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]));

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[2].End, extend: true);

        var rows = HighlightedRows(view);
        var rule = view.BlockOrigin(1).Y;

        // A leaf has no inside to select part of, so a selection reaching across it covers the
        // whole box rather than nothing at all.
        Assert.Contains(rows, y => y >= rule && y < rule + RuleView.BlockHeight);
    }

    [AvaloniaFact]
    public void ATableIsDrawnAsTheParagraphsInsideItRatherThanRefused()
    {
        // This used to throw, and the HTML importer emits a table from any pasted web page, so
        // the throw was reachable by an ordinary copy out of a browser.
        var view = new RichTextView
        {
            State = EditorState.Create(new DocumentNode([
                new TableNode([new TableRowNode([new TableCellNode([ParagraphNode.FromText("cell")])])]),
            ])),
        };

        view.Measure(new Size(400, 200));

        Assert.Equal("cell", ((ParagraphNode)view.Slots[0].Node).Content.Text);
    }

    /// <summary>
    /// A block outside the schema still refuses loudly. Drawing nothing would lose a user's text
    /// silently, and the only way to reach this now is to grow the schema without teaching the
    /// view about the new block — which is precisely when a loud failure is wanted.
    /// </summary>
    [AvaloniaFact]
    public void ABlockTheViewHasNeverHeardOfSaysSoRatherThanDrawingNothing()
    {
        var view = new RichTextView
        {
            State = EditorState.Create(new DocumentNode([new UnknownBlock()])),
        };

        var thrown = Assert.Throws<NotSupportedException>(() => view.Measure(new Size(400, 200)));

        Assert.Contains("unknown", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class UnknownBlock : BlockNode
    {
        public override int ContentSize => 0;

        public override bool IsLeaf => true;

        public override IReadOnlyList<Node> Children => [];

        public override string TypeName => "unknown";

        public override Node WithChildren(IReadOnlyList<Node> children) => this;

        public override int GetHashCode() => 0;

        protected override bool EqualsCore(Node other) => ReferenceEquals(this, other);
    }

    [AvaloniaFact]
    public void EditingAParagraphBelowARuleLeavesTheRulesViewAlone()
    {
        // Rules cannot be updated in place the way paragraphs can, so a block list that failed
        // to recognise the unchanged prefix would rebuild this view on every keystroke.
        var view = Shown(new DocumentNode([
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]));

        var rule = view.Blocks[0];

        view.MoveTo(view.Slots[1].Start + 1, extend: false);
        Assert.True(view.InsertText("X"));

        Assert.Same(rule, view.Blocks[0]);
    }

    [AvaloniaFact]
    public void ASelectionEntirelyAboveARuleDoesNotHighlightIt()
    {
        var view = RuleDocument();

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[0].Start + 3, extend: true);

        AssertRuleNotHighlighted(view);
    }

    [AvaloniaFact]
    public void ASelectionEntirelyBelowARuleDoesNotHighlightIt()
    {
        var view = RuleDocument();

        view.MoveTo(view.Slots[2].Start, extend: false);
        view.MoveTo(view.Slots[2].Start + 3, extend: true);

        AssertRuleNotHighlighted(view);
    }

    [AvaloniaFact]
    public void ARuleThatIsItselfTheSelectionIsHighlighted()
    {
        // Unreachable by clicking — MoveTo snaps to text — but a host is free to set a
        // NodeSelection on the rule, and then the selection begins exactly at the leaf.
        var view = RuleDocument();

        view.State = EditorState.Create(
            view.State.Doc,
            NodeSelection.At(view.State.Doc, view.Slots[1].Start)!);

        var rows = HighlightedRows(view);
        var rule = view.BlockOrigin(1).Y;

        Assert.Contains(rows, y => y >= rule && y < rule + RuleView.BlockHeight);
    }

    private static void AssertRuleNotHighlighted(RichTextView view)
    {
        var rows = HighlightedRows(view);
        var rule = view.BlockOrigin(1).Y;

        // Rows lying *wholly* inside the rule's band. A block boundary need not fall on a pixel
        // edge -- a paragraph's height is its line height, which is fractional -- so the row
        // straddling the boundary is partly covered by the neighbouring block's highlight and
        // proves nothing either way.
        Assert.NotEmpty(rows);
        Assert.DoesNotContain(rows, y => y >= rule && y + 1 <= rule + RuleView.BlockHeight);
    }

    private static RichTextView RuleDocument() => Shown(new DocumentNode([
        ParagraphNode.FromText("above"),
        RuleNode.Instance,
        ParagraphNode.FromText("below"),
    ]));

    /// <summary>The rows of the rendered control that contain any selection highlight.</summary>
    /// <remarks>
    /// Selection blue is filtered by hue rather than by blanking the text, because blanking the
    /// foreground is a font-property change and discards every block, which would throw away
    /// the very geometry the assertions are about.
    /// </remarks>
    private static HashSet<double> HighlightedRows(RichTextView view)
    {
        const int width = 400;
        const int height = 300;

        view.Measure(new Size(width, height));
        view.Arrange(new Rect(0, 0, width, height));

        using var bitmap = new RenderTargetBitmap(new PixelSize(width, height));

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

        var rows = new HashSet<double>();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var pixel = pixels[(y * width) + x];
                var blue = pixel & 0xFF;
                var green = (pixel >> 8) & 0xFF;
                var red = (pixel >> 16) & 0xFF;

                if (blue > red + 20 && blue > green + 20)
                {
                    rows.Add(y);
                    break;
                }
            }
        }

        return rows;
    }

    private static RichTextView Shown(DocumentNode doc)
    {
        var root = new Window { Width = 400, Height = 300 };
        var view = new RichTextView { State = EditorState.Create(doc) };

        root.Content = view;
        root.Show();
        view.Focus();

        view.Measure(new Size(400, 300));
        view.Arrange(new Rect(0, 0, 400, 300));

        return view;
    }
}
