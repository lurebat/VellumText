using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Keys and typed text reach the commands while a cell rectangle or a block image is selected.
/// </summary>
/// <remarks>
/// Both handlers began with "no paragraph under the caret, nothing to do", and neither selection
/// has a paragraph under its head: a mouse-made rectangle could not be cleared, formatted or
/// copied from the keyboard, and a clicked picture could not be deleted, typed over or undone —
/// while the same commands called directly worked. Only the keys that move within a paragraph
/// need one.
/// </remarks>
public class SelectionKeyboardTests
{
    private static (Window Window, RichTextView View) Shown(DocumentNode doc, double w = 400, double h = 300)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = w, Height = h };

        window.Show();
        window.Measure(new Size(w, h));
        window.Arrange(new Rect(0, 0, w, h));
        view.Focus();

        return (window, view);
    }

    private static DocumentNode WithBlockImage() => new([
        new ParagraphNode(InlineContent.FromText("above")),
        new BlockImageNode(new ImageEmbed("x.png", 80, 50)),
        new ParagraphNode(InlineContent.FromText("below")),
    ]);

    private static DocumentNode WithTable() => new([
        ParagraphNode.FromText("before"),
        new TableNode([
            new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
            new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
        ]),
        ParagraphNode.FromText("after"),
    ]);

    [AvaloniaFact]
    public void Delete_removes_a_selected_block_image()
    {
        var (window, view) = Shown(WithBlockImage());

        Assert.True(view.SelectImage(view.Slots[1].Start));
        Assert.IsType<NodeSelection>(view.State.Selection);

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        Assert.Equal(2, view.State.Doc.Blocks.Length);
        Assert.DoesNotContain(view.State.Doc.Blocks, b => b is BlockImageNode);
    }

    [AvaloniaFact]
    public void Backspace_removes_a_selected_block_image()
    {
        var (window, view) = Shown(WithBlockImage());

        Assert.True(view.SelectImage(view.Slots[1].Start));

        window.KeyPress(Key.Back, RawInputModifiers.None, PhysicalKey.Backspace, null);

        Assert.DoesNotContain(view.State.Doc.Blocks, b => b is BlockImageNode);
    }

    [AvaloniaFact]
    public void Ctrl_Z_works_while_a_block_image_is_selected()
    {
        var (window, view) = Shown(new DocumentNode([
            new ParagraphNode(InlineContent.FromText("above")),
            new BlockImageNode(new ImageEmbed("x.png", 80, 50)),
        ]));

        view.MoveTo(view.BlockStart + 5, extend: false);
        Assert.True(view.InsertText("!"));
        Assert.True(view.SelectImage(view.Slots[1].Start));

        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);

        Assert.Equal("above", ((ParagraphNode)view.State.Doc.Blocks[0]).Content.Text);
    }

    [AvaloniaFact]
    public void Typing_replaces_a_selected_block_image()
    {
        var doc = WithBlockImage();
        var (window, view) = Shown(doc);

        Assert.True(view.SelectImage(view.Slots[1].Start));

        window.KeyTextInput("x");

        Assert.DoesNotContain(view.State.Doc.Blocks, b => b is BlockImageNode);
        Assert.Contains("x", DocumentText.Of(view.State.Doc), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void Delete_clears_a_rectangle_of_cells()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")]),
                new TableRowNode([TableCellNode.FromText("a2"), TableCellNode.FromText("b2")]),
            ]),
        ]);
        var (window, view) = Shown(doc, 500, 400);
        var grid = TableGrid.Build(0, (TableNode)doc.Blocks[0]);

        view.State = EditorState.Create(
            doc, CellSelection.Create(doc, grid.At(0, 0)!.Value.Pos, grid.At(1, 1)!.Value.Pos)!);

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        Assert.Equal(string.Empty, DocumentText.Of(view.State.Doc).Replace("\n", string.Empty, StringComparison.Ordinal));
    }

    /// <summary>The rectangle made the way a user makes one, then Ctrl+B and Delete from the keyboard.</summary>
    [AvaloniaFact]
    public void Keys_act_on_a_mouse_made_rectangle()
    {
        var doc = WithTable();
        var (window, view) = Shown(doc, 500, 400);

        var p1 = Center(view, Find(doc, "a1"));
        var p2 = Center(view, Find(doc, "b2") + 1);

        window.MouseDown(p1, MouseButton.Left);
        window.MouseMove(p2);
        window.MouseUp(p2, MouseButton.Left);

        Assert.IsType<CellSelection>(view.State.Selection);

        window.KeyPress(Key.B, RawInputModifiers.Control, PhysicalKey.B, null);

        var bolded = view.State.Doc;
        Assert.NotSame(doc, bolded);

        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

        Assert.NotSame(bolded, view.State.Doc);
        var text = DocumentText.Of(view.State.Doc);

        Assert.All(new[] { "a1", "b1", "a2", "b2" }, cell => Assert.DoesNotContain(cell, text, StringComparison.Ordinal));
        Assert.Contains("before", text, StringComparison.Ordinal);
        Assert.Contains("after", text, StringComparison.Ordinal);
    }

    /// <summary>The control: with ordinary text selected the same keys already worked.</summary>
    [AvaloniaFact]
    public void Keys_reach_the_view_when_text_is_selected()
    {
        var (window, view) = Shown(DocumentNode.FromParagraphs("above", "below"));

        view.MoveTo(view.BlockStart + 5, extend: false);
        Assert.True(view.InsertText("!"));
        window.KeyPress(Key.Z, RawInputModifiers.Control, PhysicalKey.Z, null);
        Assert.Equal("above\nbelow", DocumentText.Of(view.State.Doc));

        view.MoveTo(view.BlockStart, extend: false);
        view.MoveTo(view.BlockStart + 2, extend: true);
        window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);
        Assert.Equal("ove\nbelow", DocumentText.Of(view.State.Doc));
    }

    /// <summary>Keys that move within a paragraph have nothing to do without one, and must not throw.</summary>
    [AvaloniaFact]
    public void Paragraph_movement_keys_are_ignored_on_a_block_image_rather_than_throwing()
    {
        var (window, view) = Shown(WithBlockImage());

        Assert.True(view.SelectImage(view.Slots[1].Start));

        foreach (var key in new[] { Key.Home, Key.End, Key.Up, Key.Down, Key.Left, Key.Right })
        {
            window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
        }

        Assert.Equal(3, view.State.Doc.Blocks.Length);
    }

    private static Point Center(RichTextView view, int position)
    {
        view.MoveTo(position, extend: false);
        var r = view.CaretRect();
        return new Point(r.X + 1, r.Y + (r.Height / 2));
    }

    private static int Find(DocumentNode doc, string text) => Find(doc.Blocks, 0, text) ?? -1;

    private static int? Find(IReadOnlyList<Node> nodes, int start, string text)
    {
        var pos = start;

        foreach (var node in nodes)
        {
            if (node is ParagraphNode paragraph && paragraph.Content.Text == text)
            {
                return pos + 1;
            }

            if (node.Children.Length > 0 && Find(node.Children, pos + 1, text) is { } found)
            {
                return found;
            }

            pos += node.NodeSize;
        }

        return null;
    }
}
