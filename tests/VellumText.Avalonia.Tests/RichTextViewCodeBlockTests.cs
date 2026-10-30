using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Typing in a code block: Enter, the ways out, Backspace and paste.
/// </summary>
/// <remarks>
/// <para>
/// A code block is a paragraph of kind <see cref="ParagraphKind.Code"/>, and its lines are
/// newlines in that paragraph's text — the soft break Shift+Enter already inserts and the HTML
/// importer reads out of <c>&lt;pre&gt;</c>. So every assertion here is on the document's shape:
/// one code paragraph with its lines in it, or the paragraphs that follow it.
/// </para>
/// <para>
/// Keys go in as key events on the view, the way a keyboard delivers them, so that the dispatch
/// in <c>OnKeyDown</c> is part of what is tested rather than skipped by calling a command.
/// </para>
/// </remarks>
public class RichTextViewCodeBlockTests
{
    // ---- Enter --------------------------------------------------------------------------------

    /// <summary>
    /// Enter in a code block adds a line to the same block. It used to split it, so every line of
    /// code became a code block of its own.
    /// </summary>
    [AvaloniaFact]
    public void Enter_in_a_code_block_adds_a_line_to_the_same_block()
    {
        var view = Laid(Doc(Code("let x = 1;")), In(0, 10));

        Press(view, Key.Enter);
        Type(view, "let y = 2;");

        Shaped(
            """
            code "let x = 1;\nlet y = 2;"
            """,
            view);
        Assert.Equal(("let x = 1;\nlet y = 2;", 21), Caret(view));
    }

    [AvaloniaFact]
    public void Enter_in_the_middle_of_a_code_line_breaks_it_there()
    {
        var view = Laid(Doc(Code("ab")), In(0, 1));

        Press(view, Key.Enter);

        Shaped(
            """
            code "a\nb"
            """,
            view);
        Assert.Equal(("a\nb", 2), Caret(view));
    }

    [AvaloniaFact]
    public void Enter_over_a_selection_in_code_replaces_it_with_a_line_break()
    {
        var view = Laid(Doc(Code("abcd")), In(0, 1), In(0, 3));

        Press(view, Key.Enter);

        Shaped(
            """
            code "a\nd"
            """,
            view);
    }

    /// <summary>A code block inside a bullet is still code: Enter breaks the line, it does not start a bullet.</summary>
    [AvaloniaFact]
    public void Enter_in_a_code_block_inside_a_bullet_adds_a_line_rather_than_a_bullet()
    {
        var doc = new DocumentNode([new ListNode([new ListItemNode([Code("x")])])]);
        var view = Laid(doc, In(0, 1));

        Press(view, Key.Enter);

        Shaped(
            """
            unordered-list
              list-item
                code "x\n"
            """,
            view);
    }

    /// <summary>
    /// Typing a block of code from the keyboard and leaving it: the third Enter, on an empty line
    /// below another empty line, leaves the block and takes the two empty lines with it.
    /// </summary>
    [AvaloniaFact]
    public void Three_enters_at_the_end_of_code_leave_the_block_without_its_empty_lines()
    {
        var view = Laid(Doc(Code(string.Empty)), In(0, 0));

        Type(view, "x");
        Press(view, Key.Enter);
        Press(view, Key.Enter);

        Shaped(
            """
            code "x\n\n"
            """,
            view);

        Press(view, Key.Enter);

        Shaped(
            """
            code "x"
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));

        Type(view, "prose");

        Shaped(
            """
            code "x"
            p "prose"
            """,
            view);
    }

    /// <summary>The paragraph the block is left for goes directly after it, not at the end of the document.</summary>
    [AvaloniaFact]
    public void Leaving_a_code_block_puts_the_paragraph_directly_below_it()
    {
        var view = Laid(Doc(Code("x\n\n"), Para("after")), In(0, 3));

        Press(view, Key.Enter);

        Shaped(
            """
            code "x"
            p ""
            p "after"
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>
    /// The rule is the empty last line under another empty line, so in an empty block two Enters are
    /// enough: the first makes the second line, and both lines are then empty.
    /// </summary>
    [AvaloniaFact]
    public void Two_enters_in_an_empty_code_block_leave_it()
    {
        var view = Laid(Doc(Code(string.Empty)), In(0, 0));

        Press(view, Key.Enter);

        Shaped(
            """
            code "\n"
            """,
            view);

        Press(view, Key.Enter);

        Shaped(
            """
            code ""
            p ""
            """,
            view);
    }

    /// <summary>Every trailing empty line goes, not just the two the Enters made.</summary>
    [AvaloniaFact]
    public void Leaving_drops_every_empty_line_at_the_end_of_the_block()
    {
        var view = Laid(Doc(Code("x\n\n\n")), In(0, 4));

        Press(view, Key.Enter);

        Shaped(
            """
            code "x"
            p ""
            """,
            view);
    }

    /// <summary>One empty line at the end is a blank line in the code, and Enter there adds another.</summary>
    [AvaloniaFact]
    public void Enter_on_an_empty_last_line_under_code_adds_a_line()
    {
        var view = Laid(Doc(Code("x\n")), In(0, 2));

        Press(view, Key.Enter);

        Shaped(
            """
            code "x\n\n"
            """,
            view);
    }

    /// <summary>Two empty lines in the middle of the code are content; only the end of the block is a way out.</summary>
    [AvaloniaFact]
    public void Enter_on_an_empty_line_in_the_middle_of_code_adds_a_line()
    {
        var view = Laid(Doc(Code("x\n\n\ny")), In(0, 3));

        Press(view, Key.Enter);

        Shaped(
            """
            code "x\n\n\n\ny"
            """,
            view);
    }

    /// <summary>Empty lines below the caret do not make it the way out; the caret has to be on the last of them.</summary>
    [AvaloniaFact]
    public void Enter_on_a_line_above_two_empty_ones_adds_a_line()
    {
        var view = Laid(Doc(Code("x\n\n")), In(0, 1));

        Press(view, Key.Enter);

        Shaped(
            """
            code "x\n\n\n"
            """,
            view);
        Assert.Equal(("x\n\n\n", 2), Caret(view));
    }

    /// <summary>A selection reaching the end is replaced by the line break; the block is only left from a caret.</summary>
    [AvaloniaFact]
    public void Enter_over_a_selection_ending_on_empty_lines_does_not_leave_the_block()
    {
        var view = Laid(Doc(Code("x\n\n")), In(0, 2), In(0, 3));

        Press(view, Key.Enter);

        Shaped(
            """
            code "x\n\n"
            """,
            view);
        Assert.Equal(("x\n\n", 3), Caret(view));
    }

    [AvaloniaFact]
    public void One_undo_after_leaving_a_code_block_brings_back_its_empty_lines_and_the_caret()
    {
        var view = Laid(Doc(Code("x\n\n")), In(0, 3));

        Press(view, Key.Enter);
        Press(view, Key.Z, KeyModifiers.Control);

        Shaped(
            """
            code "x\n\n"
            """,
            view);
        Assert.Equal(("x\n\n", 3), Caret(view));
    }

    // ---- Ctrl+Enter -----------------------------------------------------------------------------

    /// <summary>
    /// Ctrl+Enter leaves the block from anywhere in it, keeping every line, and the next paragraph
    /// is ordinary body text.
    /// </summary>
    [AvaloniaFact]
    public void Ctrl_enter_leaves_a_code_block_from_anywhere_and_keeps_its_lines()
    {
        var view = Laid(Doc(Code("x\n\n"), Para("after")), In(0, 1));

        Press(view, Key.Enter, KeyModifiers.Control);

        Shaped(
            """
            code "x\n\n"
            p ""
            p "after"
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>Cmd+Enter is the same gesture on macOS.</summary>
    [AvaloniaFact]
    public void Cmd_enter_leaves_a_code_block_too()
    {
        var view = Laid(Doc(Code("x")), In(0, 0));

        Press(view, Key.Enter, KeyModifiers.Meta);

        Shaped(
            """
            code "x"
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>In a table cell, the way out stays in the cell.</summary>
    [AvaloniaFact]
    public void Ctrl_enter_in_a_code_block_in_a_table_cell_opens_a_paragraph_in_the_cell()
    {
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([Code("x")]),
                new TableCellNode([Para("y")]),
            ])]),
        ]);
        var view = Laid(doc, In(0, 1));

        Press(view, Key.Enter, KeyModifiers.Control);

        Shaped(
            """
            table
              table-row
                table-cell
                  code "x"
                  p ""
                table-cell
                  p "y"
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>Outside code Ctrl+Enter is what it was: Enter.</summary>
    [AvaloniaFact]
    public void Ctrl_enter_outside_a_code_block_still_splits_the_paragraph()
    {
        var view = Laid(Doc(Para("ab")), In(0, 1));

        Press(view, Key.Enter, KeyModifiers.Control);

        Shaped(
            """
            p "a"
            p "b"
            """,
            view);
    }

    // ---- ↓ ------------------------------------------------------------------------------------

    /// <summary>
    /// ↓ on the last line of a code block that nothing follows opens a paragraph below it. Before,
    /// the caret only went to the end of the document, and the toolbar was the way out.
    /// </summary>
    [AvaloniaFact]
    public void Down_on_the_last_line_of_a_code_block_at_the_end_opens_a_paragraph_below()
    {
        var view = Laid(Doc(Para("above"), Code("a\nbc")), In(1, 3));

        Press(view, Key.Down);

        Shaped(
            """
            p "above"
            code "a\nbc"
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));

        Type(view, "prose");

        Shaped(
            """
            p "above"
            code "a\nbc"
            p "prose"
            """,
            view);
    }

    /// <summary>An empty last line is a line too.</summary>
    [AvaloniaFact]
    public void Down_on_an_empty_last_line_of_code_opens_a_paragraph_below()
    {
        var view = Laid(Doc(Code("a\n")), In(0, 2));

        Press(view, Key.Down);

        Shaped(
            """
            code "a\n"
            p ""
            """,
            view);
    }

    [AvaloniaFact]
    public void Down_on_an_earlier_line_of_code_moves_to_the_next_line()
    {
        var view = Laid(Doc(Code("ab\ncd")), In(0, 1));

        Press(view, Key.Down);

        Shaped(
            """
            code "ab\ncd"
            """,
            view);
        Assert.Equal(("ab\ncd", 4), Caret(view));
    }

    /// <summary>With a block below, ↓ just moves into it, as it does from any paragraph.</summary>
    [AvaloniaFact]
    public void Down_from_a_code_block_with_a_paragraph_below_moves_into_it()
    {
        var view = Laid(Doc(Code("a"), Para("next")), In(0, 1));

        Press(view, Key.Down);

        Shaped(
            """
            code "a"
            p "next"
            """,
            view);
        Assert.Equal("next", Caret(view).Text);
    }

    /// <summary>In a bullet the paragraph goes after the code, inside the bullet, where Ctrl+Enter puts it too.</summary>
    [AvaloniaFact]
    public void Down_from_a_code_block_ending_a_bullet_at_the_end_opens_a_paragraph_in_the_bullet()
    {
        var doc = new DocumentNode([new ListNode([new ListItemNode([Code("x")])])]);
        var view = Laid(doc, In(0, 1));

        Press(view, Key.Down);

        Shaped(
            """
            unordered-list
              list-item
                code "x"
                p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>Shift+↓ selects; it does not add anything.</summary>
    [AvaloniaFact]
    public void Shift_down_at_the_end_of_a_final_code_block_adds_nothing()
    {
        var view = Laid(Doc(Code("ab")), In(0, 0));

        Press(view, Key.Down, KeyModifiers.Shift);

        Shaped(
            """
            code "ab"
            """,
            view);
    }

    /// <summary>Only code is a trap; ↓ at the end of a final body paragraph is what it was.</summary>
    [AvaloniaFact]
    public void Down_at_the_end_of_a_final_body_paragraph_adds_nothing()
    {
        var view = Laid(Doc(Para("ab")), In(0, 1));

        Press(view, Key.Down);

        Shaped(
            """
            p "ab"
            """,
            view);
        Assert.Equal(("ab", 2), Caret(view));
    }

    /// <summary>A read-only view moves the caret to the end, as it would in any final paragraph.</summary>
    [AvaloniaFact]
    public void Down_in_a_read_only_final_code_block_moves_to_its_end()
    {
        var view = Laid(Doc(Code("ab")), In(0, 1));

        view.IsReadOnly = true;
        Press(view, Key.Down);

        Shaped(
            """
            code "ab"
            """,
            view);
        Assert.Equal(("ab", 2), Caret(view));
    }

    // ---- Backspace ----------------------------------------------------------------------------

    /// <summary>
    /// Backspace in an empty code block turns it into an ordinary paragraph, where it used to join
    /// the line above — or, with nothing above, do nothing, leaving the toolbar as the only way.
    /// </summary>
    [AvaloniaFact]
    public void Backspace_in_an_empty_code_block_makes_it_a_paragraph()
    {
        var view = Laid(Doc(Para("above"), Code(string.Empty)), In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "above"
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));

        Type(view, "prose");

        Shaped(
            """
            p "above"
            p "prose"
            """,
            view);
    }

    [AvaloniaFact]
    public void Backspace_in_an_empty_code_block_alone_in_the_document_makes_it_a_paragraph()
    {
        var view = Laid(Doc(Code(string.Empty)), In(0, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p ""
            """,
            view);
    }

    /// <summary>In a bullet the code becomes a paragraph and the bullet stays; the next Backspace lifts it.</summary>
    [AvaloniaFact]
    public void Backspace_in_an_empty_code_block_in_a_bullet_keeps_the_bullet()
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("one"), new ListItemNode([Code(string.Empty)])]),
        ]);
        var view = Laid(doc, In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            unordered-list
              list-item
                p "one"
              list-item
                p ""
            """,
            view);
    }

    /// <summary>A code block with something in it is joined to the line above, as it was.</summary>
    [AvaloniaFact]
    public void Backspace_at_the_start_of_code_with_words_in_it_still_joins_it_to_the_line_above()
    {
        var view = Laid(Doc(Para("above"), Code("x")), In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "abovex"
            """,
            view);
    }

    [AvaloniaFact]
    public void One_undo_brings_back_the_code_block_backspace_turned_into_a_paragraph()
    {
        var view = Laid(Doc(Para("above"), Code(string.Empty)), In(1, 0));

        Press(view, Key.Back);
        Press(view, Key.Z, KeyModifiers.Control);

        Shaped(
            """
            p "above"
            code ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    // ---- paste --------------------------------------------------------------------------------
    //
    // Driven through PasteText and PasteImport, the methods Ctrl+Shift+V, Ctrl+V and a drop end in
    // once the clipboard has been read: the tests here do not touch the real clipboard.

    /// <summary>
    /// Pasted lines go into the code block as its lines. Each line used to become a paragraph, so
    /// the block was split into a code block and body paragraphs.
    /// </summary>
    [AvaloniaFact]
    public void Pasting_lines_of_text_into_a_code_block_keeps_them_in_the_block()
    {
        var view = Laid(Doc(Code("ab")), In(0, 1));

        Assert.True(view.PasteText("x\r\n  y  \n\n\tz"));

        Shaped(
            """
            code "ax\n  y  \n\n\tzb"
            """,
            view);
        Assert.Equal(("ax\n  y  \n\n\tzb", 12), Caret(view));
    }

    [AvaloniaFact]
    public void Pasting_lines_into_an_empty_code_block_keeps_it_a_code_block()
    {
        var view = Laid(Doc(Code(string.Empty), Para("after")), In(0, 0));

        Assert.True(view.PasteText("a\nb"));

        Shaped(
            """
            code "a\nb"
            p "after"
            """,
            view);
    }

    [AvaloniaFact]
    public void Pasting_lines_over_a_selection_in_code_replaces_it_inside_the_block()
    {
        var view = Laid(Doc(Code("abcd")), In(0, 1), In(0, 3));

        Assert.True(view.PasteText("x\ny"));

        Shaped(
            """
            code "ax\nyd"
            """,
            view);
    }

    /// <summary>
    /// Rich content pasted or dropped into code arrives as its text, a line per paragraph, the way
    /// Tiptap and ProseMirror paste into code: a heading or a list has no meaning inside a code
    /// block, and keeping them meant splitting the block around them.
    /// </summary>
    [AvaloniaFact]
    public void Pasting_rich_paragraphs_into_a_code_block_pastes_their_text_as_lines()
    {
        var view = Laid(Doc(Code("x")), In(0, 1));
        var pasted = new DocumentNode([
            ParagraphNode.FromText("one"),
            new ParagraphNode(InlineContent.FromText("two"), ParagraphKind.Heading1),
            new ListNode([ListItemNode.FromText("three")]),
        ]);

        Assert.True(view.PasteImport(new ImportResult(pasted)));

        Shaped(
            """
            code "xone\ntwo\nthree"
            """,
            view);
    }

    /// <summary>Into an empty block a rich paste used to replace the block outright, code and all.</summary>
    [AvaloniaFact]
    public void Pasting_rich_paragraphs_into_an_empty_code_block_keeps_it_a_code_block()
    {
        var view = Laid(Doc(Code(string.Empty)), In(0, 0));

        Assert.True(view.PasteDocument(Doc(Para("one"), Para("two"))));

        Shaped(
            """
            code "one\ntwo"
            """,
            view);
    }

    /// <summary>One pasted paragraph merges into the code, whatever its kind; the block stays code.</summary>
    [AvaloniaFact]
    public void Pasting_one_heading_into_an_empty_code_block_keeps_it_a_code_block()
    {
        var view = Laid(Doc(Code(string.Empty)), In(0, 0));

        Assert.True(view.PasteDocument(Doc(
            new ParagraphNode(InlineContent.FromText("h"), ParagraphKind.Heading1))));

        Shaped(
            """
            code "h"
            """,
            view);
    }

    /// <summary>
    /// An image put into a code block lands there as it did before: one paragraph merges into the
    /// code rather than being reduced to its text, which for an image without alt text is nothing.
    /// </summary>
    [AvaloniaFact]
    public void An_image_inserted_into_a_code_block_still_lands_in_it()
    {
        var view = Laid(Doc(Code("ab")), In(0, 1));

        Assert.True(view.InsertImage(Png(2, 2), "image/png"));

        var code = Assert.IsType<ParagraphNode>(Assert.Single(view.State.Doc.Blocks));

        Assert.Equal(ParagraphKind.Code, code.Kind);
        Assert.IsType<ImageEmbed>(Assert.Single(code.Content.Embeds));
        Assert.Equal($"a{InlineContent.Placeholder}b", code.Content.Text);
    }

    /// <summary>Outside code, pasted lines are still paragraphs.</summary>
    [AvaloniaFact]
    public void Pasting_lines_into_a_body_paragraph_still_makes_paragraphs()
    {
        var view = Laid(Doc(Para("ab")), In(0, 1));

        Assert.True(view.PasteText("x\ny"));

        Shaped(
            """
            p "ax"
            p "yb"
            """,
            view);
    }

    [AvaloniaFact]
    public void One_undo_takes_back_lines_pasted_into_code()
    {
        var view = Laid(Doc(Code("ab")), In(0, 1));

        Assert.True(view.PasteText("x\ny"));
        Press(view, Key.Z, KeyModifiers.Control);

        Shaped(
            """
            code "ab"
            """,
            view);
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ParagraphNode Code(string text) =>
        new(InlineContent.FromText(text), ParagraphKind.Code);

    private static ParagraphNode Para(string text) => ParagraphNode.FromText(text);

    private static DocumentNode Doc(params BlockNode[] blocks) => new(blocks);

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width, height));
        using var buffer = new MemoryStream();

        bitmap.Save(buffer, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return buffer.ToArray();
    }

    private static void Press(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });

    private static void Type(RichTextView view, string text) =>
        view.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = text,
            Source = view,
        });

    /// <summary>A position inside the nth paragraph of the document, wherever it is nested.</summary>
    private static Func<DocumentNode, int> In(int paragraph, int offset) =>
        doc => Paragraphs(doc).Skip(paragraph).First() + offset;

    private static IEnumerable<int> Paragraphs(DocumentNode doc)
    {
        return Walk(doc, 0);

        static IEnumerable<int> Walk(Node node, int start)
        {
            if (node is ParagraphNode)
            {
                yield return start;
                yield break;
            }

            var at = start;

            foreach (var child in node.Children)
            {
                foreach (var found in Walk(child, at + 1))
                {
                    yield return found;
                }

                at += child.NodeSize;
            }
        }
    }

    /// <summary>A view that has been shown and measured, as a keyboard user's would be.</summary>
    private static RichTextView Laid(DocumentNode doc, Func<DocumentNode, int> where) =>
        Laid(doc, where, where);

    private static RichTextView Laid(
        DocumentNode doc, Func<DocumentNode, int> anchor, Func<DocumentNode, int> head)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(doc, new TextSelection(anchor(doc), head(doc))),
        };
        var root = new Window { Width = 300, Height = 200, Content = view };

        root.Show();
        view.Measure(new Size(300, 200));
        view.Arrange(new Rect(0, 0, 300, 200));

        return view;
    }

    private static (string Text, int Offset) Caret(RichTextView view)
    {
        var at = view.State.Doc.Resolve(view.State.Selection.Head);

        Assert.True(view.State.Selection.IsEmpty);

        return (at.Paragraph!.Content.Text, at.ParentOffset);
    }

    /// <summary>The document's shape, indented, one node per line, newlines and tabs in text written as \n and \t.</summary>
    private static void Shaped(string expected, RichTextView view) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), Shape(view.State.Doc));

    private static string Shape(DocumentNode doc)
    {
        var text = new StringBuilder();

        Write(doc, -1);

        return text.ToString().TrimEnd('\n');

        void Write(Node node, int depth)
        {
            if (node is not DocumentNode)
            {
                text.Append(new string(' ', depth * 2)).Append(Describe(node)).Append('\n');
            }

            foreach (var child in node.Children)
            {
                Write(child, depth + 1);
            }
        }

        static string Describe(Node node) => node switch
        {
            ParagraphNode p => $"{Kind(p.Kind)} \"{Escaped(p.Content.Text)}\"",
            ListNode { Kind: ListKind.Ordered } l => $"ordered-list from {l.Start}",
            ListNode => "unordered-list",
            _ => node.TypeName,
        };

        static string Escaped(string text) =>
            text.Replace("\n", "\\n", StringComparison.Ordinal).Replace("\t", "\\t", StringComparison.Ordinal);

        static string Kind(ParagraphKind kind) => kind switch
        {
            ParagraphKind.Body => "p",
            ParagraphKind.Code => "code",
            ParagraphKind.Quote => "quote",
            _ => kind.ToString().ToLowerInvariant(),
        };
    }
}