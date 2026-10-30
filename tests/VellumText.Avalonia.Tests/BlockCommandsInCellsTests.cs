using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Heading, alignment and indent commands reach the paragraph a table cell's caret is in.
/// </summary>
/// <remarks>
/// The commands walked the drawn blocks, and a table is one drawn block, so a caret in a cell
/// touched no paragraph at all: Ctrl+1, Ctrl+E and the toolbar buttons silently did nothing in a
/// table, and the toolbar showed no state there. Character formatting already walked the leaves
/// for exactly this reason.
/// </remarks>
public class BlockCommandsInCellsTests
{
    private static DocumentNode Table() => new([
        ParagraphNode.FromText("before"),
        new TableNode([new TableRowNode([TableCellNode.FromText("a1"), TableCellNode.FromText("b1")])]),
    ]);

    private static (Window Window, RichTextView View) InCell(string text)
    {
        var doc = Table();
        var view = new RichTextView { State = EditorState.Create(doc), MinHeight = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        view.Focus();
        view.MoveTo(DocumentSearch.Find(doc, text)[0].From + 1, extend: false);

        return (window, view);
    }

    private static ParagraphNode Paragraph(RichTextView view, string text)
    {
        ParagraphNode? found = null;

        void Walk(IEnumerable<Node> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is ParagraphNode p && p.Content.Text == text)
                {
                    found ??= p;
                }
                else
                {
                    Walk(node.Children);
                }
            }
        }

        Walk(view.State.Doc.Blocks);

        return found ?? throw new InvalidOperationException(text);
    }

    [AvaloniaFact]
    public void A_heading_command_changes_the_cells_paragraph()
    {
        var (_, view) = InCell("a1");

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading1));
        Assert.Equal(ParagraphKind.Heading1, Paragraph(view, "a1").Kind);
        Assert.Equal(ParagraphKind.Body, Paragraph(view, "b1").Kind);
        Assert.Equal(ParagraphKind.Heading1, view.ParagraphKindAt);
    }

    [AvaloniaFact]
    public void An_alignment_command_changes_the_cells_paragraph()
    {
        var (_, view) = InCell("a1");

        Assert.True(view.SetAlign(TextAlign.Center));
        Assert.Equal(TextAlign.Center, Paragraph(view, "a1").Align);
        Assert.Equal(TextAlign.Center, view.AlignAt);
    }

    [AvaloniaFact]
    public void An_indent_command_changes_the_cells_paragraph()
    {
        var (_, view) = InCell("b1");

        Assert.True(view.SetIndentLevel(2));
        Assert.Equal(2, Paragraph(view, "b1").IndentLevel);
        Assert.Equal(2, view.IndentLevelAt);
    }

    [AvaloniaFact]
    public void The_shortcut_reaches_the_cell_too()
    {
        var (window, view) = InCell("a1");

        window.KeyPress(Key.D2, RawInputModifiers.Control, PhysicalKey.Digit2, null);

        Assert.Equal(ParagraphKind.Heading2, Paragraph(view, "a1").Kind);
    }

    [AvaloniaFact]
    public void A_range_inside_a_cell_reaches_its_paragraph()
    {
        var (_, view) = InCell("a1");

        view.MoveTo(view.State.Selection.From - 1, extend: false);
        view.MoveTo(view.State.Selection.From + 2, extend: true);

        Assert.True(view.SetAlign(TextAlign.Right));
        Assert.Equal(TextAlign.Right, Paragraph(view, "a1").Align);
        Assert.Equal(TextAlign.Default, Paragraph(view, "b1").Align);
    }

    /// <summary>The control: outside a table the commands always worked.</summary>
    [AvaloniaFact]
    public void Outside_a_table_the_heading_command_still_works()
    {
        var (_, view) = InCell("before");

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading3));
        Assert.Equal(ParagraphKind.Heading3, Paragraph(view, "before").Kind);
    }
}
