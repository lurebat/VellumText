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

    // ---- helpers ------------------------------------------------------------------------------

    private static ParagraphNode Code(string text) =>
        new(InlineContent.FromText(text), ParagraphKind.Code);

    private static ParagraphNode Para(string text) => ParagraphNode.FromText(text);

    private static DocumentNode Doc(params BlockNode[] blocks) => new(blocks);

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

    /// <summary>The document's shape, indented, one node per line, newlines in text written as \n.</summary>
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
            ParagraphNode p => $"{Kind(p.Kind)} \"{p.Content.Text.Replace("\n", "\\n", StringComparison.Ordinal)}\"",
            ListNode { Kind: ListKind.Ordered } l => $"ordered-list from {l.Start}",
            ListNode => "unordered-list",
            _ => node.TypeName,
        };

        static string Kind(ParagraphKind kind) => kind switch
        {
            ParagraphKind.Body => "p",
            ParagraphKind.Code => "code",
            ParagraphKind.Quote => "quote",
            _ => kind.ToString().ToLowerInvariant(),
        };
    }
}