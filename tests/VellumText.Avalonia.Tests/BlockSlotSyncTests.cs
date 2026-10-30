using global::Avalonia;
using global::Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The view keeps its block list in step with the document by patching only the top-level
/// blocks an edit replaced. Whatever the edit, the result has to be exactly the list flattening
/// the whole document would give.
/// </summary>
/// <remarks>
/// <para>
/// The list was rebuilt from the whole document on every change, which was 3.2 MB per keystroke
/// at 50,000 paragraphs (F-14). Patching it is only safe if it can never drift: a slot one
/// position out puts every caret and selection after it in the wrong place, and a marker or a
/// depth left behind draws a list wrongly with nothing to say why.
/// </para>
/// <para>
/// So <see cref="DocumentBlocks.Flatten(DocumentNode)"/> is the oracle and random editing through
/// the view's own commands is the driver — typing, splitting, joining, list and table commands,
/// pasting blocks, undo and redo — over a document with nested lists, a numbered list starting
/// past one, a table, a rule and a heading. After every command the slots must match the
/// oracle node for node and position for position, and every block view must be drawing the
/// block, depth and marker its slot says.
/// </para>
/// </remarks>
public class BlockSlotSyncTests
{
    private const double Width = 500;
    private const double Height = 300;

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void TheBlockListMatchesTheDocumentAfterEveryEdit(int seed)
    {
        var random = new Random(seed);
        var view = new RichTextView { State = EditorState.Create(Seed()) };

        Layout(view);
        AssertInStep(view, "start");

        for (var step = 0; step < 80; step++)
        {
            var command = Edit(view, random);

            Layout(view);
            AssertInStep(view, $"seed {seed}, step {step}: {command}");
        }
    }

    /// <remarks>
    /// Replacing the document outright shares nothing with the old one, and a font change throws
    /// every layout away; after both the list has to be rebuilt rather than patched.
    /// </remarks>
    [AvaloniaFact]
    public void TheBlockListIsRebuiltWhenTheDocumentIsReplacedOrTheLayoutDropped()
    {
        var view = new RichTextView { State = EditorState.Create(Seed()) };

        Layout(view);

        view.State = EditorState.Create(DocumentNode.FromParagraphs("one", "two"));
        Layout(view);
        AssertInStep(view, "replaced");

        view.State = EditorState.Create(Seed());
        view.FontSize = 23;
        Layout(view);
        AssertInStep(view, "font change");

        view.MoveTo(view.State.Doc.ContentSize - 2, false);
        view.InsertText("after");
        Layout(view);
        AssertInStep(view, "typed after the font change");
    }

    private static string Edit(RichTextView view, Random random)
    {
        var doc = view.State.Doc;
        var leaves = DocumentBlocks.Leaves(doc).Where(l => l.Node is ParagraphNode).ToArray();
        var leaf = leaves[random.Next(leaves.Length)];

        // Most edits happen at the edges of a block, where the structural keys do something.
        var at = random.Next(3) switch
        {
            0 => leaf.Start,
            1 => leaf.End,
            _ => leaf.Start + random.Next(leaf.ContentSize + 1),
        };

        view.MoveTo(at, false);

        switch (random.Next(15))
        {
            case 0:
                view.InsertText("xy");
                return $"type at {at}";
            case 1:
                view.Split();
                return $"Enter at {at}";
            case 2:
                view.Backspace();
                return $"Backspace at {at}";
            case 3:
                view.Delete();
                return $"Delete at {at}";
            case 4:
                view.ToggleList(random.Next(2) == 0 ? ListKind.Unordered : ListKind.Ordered);
                return $"toggle list at {at}";
            case 5:
                view.NestListItem();
                return $"nest at {at}";
            case 6:
                view.LiftListItem();
                return $"lift at {at}";
            case 7:
                view.Undo();
                return "undo";
            case 8:
                view.Redo();
                return "redo";
            case 9:
                var other = leaves[random.Next(leaves.Length)].Start;

                view.MoveTo(other, true);
                view.DeleteSelection();
                return $"delete {at}..{other}";
            case 10:
                view.PasteDocument(new DocumentNode(
                [
                    ParagraphNode.FromText("pasted"),
                    new ListNode([ListItemNode.FromText("p1"), ListItemNode.FromText("p2")], ListKind.Ordered),
                ]));
                return $"paste blocks at {at}";
            case 11:
                view.InsertRowBelow();
                return $"row below at {at}";
            case 12:
                view.SetParagraphKind(random.Next(2) == 0 ? ParagraphKind.Heading2 : ParagraphKind.Body);
                return $"kind at {at}";
            case 13:
                view.Indent();
                return $"indent at {at}";
            default:
                view.InsertText("\U0001F600");
                return $"astral at {at}";
        }
    }

    private static void AssertInStep(RichTextView view, string after)
    {
        var expected = DocumentBlocks.Flatten(view.State.Doc);
        var actual = view.Slots;

        Assert.True(
            expected.Length == actual.Length,
            $"{after}: {actual.Length} slots where the document has {expected.Length}");
        Assert.Equal(expected.Length, view.Blocks.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            var want = expected[i];
            var have = actual[i];

            Assert.True(
                ReferenceEquals(want.Node, have.Node)
                    && want.Start == have.Start
                    && want.Depth == have.Depth
                    && want.Marker == have.Marker,
                $"{after}: slot {i} is {have} where the document says {want}");

            switch (view.Blocks[i])
            {
                case ParagraphView paragraph:
                    Assert.True(
                        want.Node is ParagraphNode node && paragraph.Paragraph.Equals(node),
                        $"{after}: block {i} draws a different paragraph from its slot");
                    Assert.True(
                        paragraph.Depth == want.Depth && paragraph.Marker == want.Marker,
                        $"{after}: block {i} is drawn at depth {paragraph.Depth} with {paragraph.Marker}, "
                        + $"where its slot says {want.Depth} with {want.Marker}");
                    break;

                case TableBlockView:
                    Assert.IsType<TableNode>(want.Node);
                    break;

                default:
                    Assert.True(want.Node.IsLeaf, $"{after}: block {i} has the wrong kind of view");
                    break;
            }
        }
    }

    /// <summary>
    /// Every kind of top-level block, and lists whose markers depend on their neighbours.
    /// </summary>
    private static DocumentNode Seed() => new(
    [
        new ParagraphNode(InlineContent.FromText("title"), ParagraphKind.Heading1),
        ParagraphNode.FromText("alpha"),
        new ListNode(
        [
            ListItemNode.FromText("one"),
            new ListItemNode(
            [
                ParagraphNode.FromText("two"),
                new ListNode([ListItemNode.FromText("deep"), ListItemNode.FromText("deeper")], ListKind.Ordered),
            ]),
            ListItemNode.FromText("three"),
        ]),
        RuleNode.Instance,
        new ListNode([ListItemNode.FromText("third"), ListItemNode.FromText("fourth")], ListKind.Ordered, start: 3),
        new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]),
        ParagraphNode.FromText("beta"),
        ParagraphNode.FromText("gamma"),
    ]);

    private static void Layout(RichTextView view)
    {
        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));
    }
}
