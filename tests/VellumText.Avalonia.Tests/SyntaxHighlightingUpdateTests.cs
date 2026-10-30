using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A highlighted code block stays highlighted when it changes.
/// </summary>
/// <remarks>
/// Highlighting was applied only when a block's view was first created. An edit updates the view
/// in place, and so does a viewer whose document is replaced by one of the same shape, so typing
/// one character — or a clipboard history recycling its viewer — drew the code plain until the
/// layout happened to be thrown away.
/// </remarks>
public class SyntaxHighlightingUpdateTests
{
    private static DocumentNode Code(string text) =>
        new([new ParagraphNode(InlineContent.FromText(text), ParagraphKind.Code)]);

    private static void Layout(Window window)
    {
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
    }

    private static bool Highlighted(BlockView view) => !((ParagraphView)view).Paragraph.Content.Marks.IsEmpty;

    [AvaloniaFact]
    public void Typing_in_a_code_block_keeps_it_highlighted()
    {
        var view = new RichTextView { State = EditorState.Create(Code("{\"a\": 1}")), SyntaxHighlighter = JsonHighlighter.Instance };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        Layout(window);

        Assert.True(Highlighted(view.Blocks[0]), "highlighted before the edit");

        view.MoveTo(view.BlockStart + 8, extend: false);
        Assert.True(view.InsertText(" "));
        Layout(window);

        Assert.True(Highlighted(view.Blocks[0]), "highlighting lost after typing one space");
    }

    [AvaloniaFact]
    public void A_viewer_whose_document_is_replaced_stays_highlighted()
    {
        var viewer = new RichTextViewer { Document = Code("{\"a\": 1}"), SyntaxHighlighter = JsonHighlighter.Instance };
        var window = new Window { Content = viewer, Width = 400, Height = 300 };

        window.Show();
        Layout(window);

        Assert.True(Highlighted(viewer.Blocks[0]), "first document highlighted");

        viewer.Document = Code("{\"b\": 2}");
        Layout(window);

        Assert.True(Highlighted(viewer.Blocks[0]), "second document lost highlighting after a Document swap");
    }

    [AvaloniaFact]
    public void Typing_in_a_code_block_inside_a_table_cell_keeps_it_highlighted()
    {
        var doc = new DocumentNode([
            new TableNode([
                new TableRowNode([
                    new TableCellNode([new ParagraphNode(InlineContent.FromText("{\"a\": 1}"), ParagraphKind.Code)]),
                ]),
            ]),
        ]);
        var view = new RichTextView { State = EditorState.Create(doc), SyntaxHighlighter = JsonHighlighter.Instance };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        Layout(window);

        var table = Assert.IsType<TableBlockView>(view.Blocks[0]);

        Assert.True(Highlighted(table.ParagraphViews.Single()), "highlighted before the edit");

        // Into the table, its row, its cell and the paragraph: four boundaries, then eight characters.
        view.MoveTo(4 + 8, extend: false);
        Assert.True(view.InsertText(" "));
        Layout(window);

        table = Assert.IsType<TableBlockView>(view.Blocks[0]);
        Assert.True(Highlighted(table.ParagraphViews.Single()), "highlighting lost after typing in a cell");
    }
}
