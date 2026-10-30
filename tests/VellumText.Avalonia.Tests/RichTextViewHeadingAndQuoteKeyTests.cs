using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Enter and Backspace in headings and quotes, measured against what Word, Google Docs, Notion
/// and Tiptap do.
/// </summary>
/// <remarks>
/// Keys go in as key events on the view, and every assertion is on the document's shape and the
/// caret, so the dispatch in <c>OnKeyDown</c> is part of what is tested.
/// </remarks>
public class RichTextViewHeadingAndQuoteKeyTests
{
    // ---- Enter in a heading ---------------------------------------------------------------------

    /// <summary>
    /// Enter at the end of a heading starts body text, as in every word processor. It used to start
    /// another heading, aligned like the first.
    /// </summary>
    [AvaloniaFact]
    public void Enter_at_the_end_of_a_heading_starts_a_body_paragraph()
    {
        var view = Laid(Doc(Heading("Title", TextAlign.Center)), In(0, 5));

        Press(view, Key.Enter);

        Shaped(
            """
            heading1 "Title" align=Center
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));

        Type(view, "text");

        Shaped(
            """
            heading1 "Title" align=Center
            p "text"
            """,
            view);
    }

    [AvaloniaFact]
    public void Enter_over_a_selection_reaching_the_end_of_a_heading_starts_a_body_paragraph()
    {
        var view = Laid(Doc(Heading("Title")), In(0, 3), In(0, 5));

        Press(view, Key.Enter);

        Shaped(
            """
            heading1 "Tit"
            p ""
            """,
            view);
    }

    [AvaloniaFact]
    public void Enter_in_an_empty_heading_starts_a_body_paragraph_below_it()
    {
        var view = Laid(Doc(Heading(string.Empty)), In(0, 0));

        Press(view, Key.Enter);

        Shaped(
            """
            heading1 ""
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>Only the kind and the alignment are the heading's; the indent stays where the user put it.</summary>
    [AvaloniaFact]
    public void Enter_at_the_end_of_an_indented_heading_keeps_the_indent()
    {
        var view = Laid(
            Doc(new ParagraphNode(InlineContent.FromText("Title"), ParagraphKind.Heading2, TextAlign.Right, 1)),
            In(0, 5));

        Press(view, Key.Enter);

        Shaped(
            """
            heading2 "Title" align=Right indent=1
            p "" indent=1
            """,
            view);
    }

    /// <summary>In a bullet, Enter at the end of a heading still starts the next bullet, with body text in it.</summary>
    [AvaloniaFact]
    public void Enter_at_the_end_of_a_heading_in_a_bullet_starts_a_bullet_of_body_text()
    {
        var doc = new DocumentNode([new ListNode([new ListItemNode([Heading("h")])])]);
        var view = Laid(doc, In(0, 1));

        Press(view, Key.Enter);

        Shaped(
            """
            unordered-list
              list-item
                heading1 "h"
              list-item
                p ""
            """,
            view);
    }

    [AvaloniaFact]
    public void Enter_in_the_middle_of_a_heading_still_gives_two_headings()
    {
        var view = Laid(Doc(Heading("Title", TextAlign.Center)), In(0, 2));

        Press(view, Key.Enter);

        Shaped(
            """
            heading1 "Ti" align=Center
            heading1 "tle" align=Center
            """,
            view);
        Assert.Equal(("tle", 0), Caret(view));
    }

    [AvaloniaFact]
    public void Enter_at_the_start_of_a_heading_still_pushes_it_down()
    {
        var view = Laid(Doc(Heading("Title")), In(0, 0));

        Press(view, Key.Enter);

        Shaped(
            """
            heading1 ""
            heading1 "Title"
            """,
            view);
        Assert.Equal(("Title", 0), Caret(view));
    }

    // ---- Enter in a quote -----------------------------------------------------------------------

    /// <summary>
    /// Typing a quote from the keyboard and leaving it: Enter continues the quote, and Enter on the
    /// empty line that made leaves it. That empty line used to be one more quote line, and so on
    /// for every Enter.
    /// </summary>
    [AvaloniaFact]
    public void Enter_on_an_empty_quote_line_leaves_the_quote()
    {
        var view = Laid(Doc(Quote("said")), In(0, 4));

        Press(view, Key.Enter);

        Shaped(
            """
            quote "said"
            quote ""
            """,
            view);

        Press(view, Key.Enter);

        Shaped(
            """
            quote "said"
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));

        Type(view, "reply");

        Shaped(
            """
            quote "said"
            p "reply"
            """,
            view);
    }

    /// <summary>An empty line in the middle of a quote leaves it there, splitting the quote around it.</summary>
    [AvaloniaFact]
    public void Enter_on_an_empty_line_in_the_middle_of_a_quote_splits_the_quote_around_it()
    {
        var view = Laid(Doc(Quote("one"), Quote(string.Empty), Quote("two")), In(1, 0));

        Press(view, Key.Enter);

        Shaped(
            """
            quote "one"
            p ""
            quote "two"
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>Leaving the quote is leaving its alignment too, as leaving a heading is.</summary>
    [AvaloniaFact]
    public void Leaving_a_centred_quote_gives_body_text_at_the_default_alignment()
    {
        var view = Laid(Doc(Quote("said", TextAlign.Center), Quote(string.Empty, TextAlign.Center)), In(1, 0));

        Press(view, Key.Enter);

        Shaped(
            """
            quote "said" align=Center
            p ""
            """,
            view);
    }

    /// <summary>In a bullet the empty quote line becomes body text in the bullet; the next Enter leaves the list.</summary>
    [AvaloniaFact]
    public void Enter_on_an_empty_quote_line_in_a_bullet_leaves_the_quote_before_the_list()
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("one"), new ListItemNode([Quote(string.Empty)])]),
        ]);
        var view = Laid(doc, In(1, 0));

        Press(view, Key.Enter);

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

    [AvaloniaFact]
    public void Enter_on_a_quote_line_with_words_still_continues_the_quote()
    {
        var view = Laid(Doc(Quote("said")), In(0, 2));

        Press(view, Key.Enter);

        Shaped(
            """
            quote "sa"
            quote "id"
            """,
            view);
    }

    [AvaloniaFact]
    public void One_undo_puts_back_the_quote_line_enter_left()
    {
        var view = Laid(Doc(Quote("said"), Quote(string.Empty)), In(1, 0));

        Press(view, Key.Enter);
        Press(view, Key.Z, KeyModifiers.Control);

        Shaped(
            """
            quote "said"
            quote ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    // ---- Backspace at the start of a quote ------------------------------------------------------

    /// <summary>
    /// Backspace at the start of a quote takes the quote off first, as Notion and Tiptap do, and the
    /// next one joins. It used to join straight away, so the quoted words became body text inside
    /// the paragraph above.
    /// </summary>
    [AvaloniaFact]
    public void Backspace_at_the_start_of_a_quote_takes_the_quote_off_first()
    {
        var view = Laid(Doc(Para("above"), Quote("said")), In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "above"
            p "said"
            """,
            view);
        Assert.Equal(("said", 0), Caret(view));

        Press(view, Key.Back);

        Shaped(
            """
            p "abovesaid"
            """,
            view);
        Assert.Equal(("abovesaid", 5), Caret(view));
    }

    [AvaloniaFact]
    public void Backspace_at_the_start_of_a_line_in_the_middle_of_a_quote_takes_only_that_line_out()
    {
        var view = Laid(Doc(Quote("one"), Quote("two"), Quote("three")), In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            quote "one"
            p "two"
            quote "three"
            """,
            view);
    }

    /// <summary>Taking the kind off keeps the paragraph's own alignment; nothing was left.</summary>
    [AvaloniaFact]
    public void Backspace_at_the_start_of_a_centred_quote_keeps_it_centred()
    {
        var view = Laid(Doc(Para("above"), Quote("said", TextAlign.Center)), In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "above"
            p "said" align=Center
            """,
            view);
    }

    [AvaloniaFact]
    public void Backspace_in_an_empty_quote_line_takes_the_quote_off_first()
    {
        var view = Laid(Doc(Para("above"), Quote(string.Empty)), In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "above"
            p ""
            """,
            view);
        Assert.Equal((string.Empty, 0), Caret(view));
    }

    /// <summary>A quote as the first block used to ignore Backspace, with nothing to join to.</summary>
    [AvaloniaFact]
    public void Backspace_at_the_start_of_a_quote_that_is_the_first_block_makes_it_body_text()
    {
        var view = Laid(Doc(Quote("said"), Para("below")), In(0, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "said"
            p "below"
            """,
            view);
        Assert.Equal(("said", 0), Caret(view));
    }

    [AvaloniaFact]
    public void Backspace_inside_a_quote_still_deletes_a_character()
    {
        var view = Laid(Doc(Quote("said")), In(0, 1));

        Press(view, Key.Back);

        Shaped(
            """
            quote "aid"
            """,
            view);
    }

    /// <summary>Headings are left as they were: Backspace at the start joins them to the paragraph above.</summary>
    [AvaloniaFact]
    public void Backspace_at_the_start_of_a_heading_after_a_paragraph_still_joins_it()
    {
        var view = Laid(Doc(Para("above"), Heading("Title")), In(1, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "aboveTitle"
            """,
            view);
    }

    [AvaloniaFact]
    public void One_undo_puts_the_quote_backspace_took_off_back()
    {
        var view = Laid(Doc(Para("above"), Quote("said")), In(1, 0));

        Press(view, Key.Back);
        Press(view, Key.Z, KeyModifiers.Control);

        Shaped(
            """
            p "above"
            quote "said"
            """,
            view);
        Assert.Equal(("said", 0), Caret(view));
    }

    // ---- Backspace at the start of the document ------------------------------------------------

    /// <summary>
    /// Backspace at the start of a heading that is the first block makes it body text, as Notion and
    /// Tiptap do. With nothing above to join to, it used to do nothing, so the toolbar was the only
    /// way to turn the first line of a document back into text.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(ParagraphKind.Heading1)]
    [InlineData(ParagraphKind.Heading6)]
    public void Backspace_at_the_start_of_a_heading_that_is_the_first_block_makes_it_body_text(ParagraphKind kind)
    {
        var view = Laid(
            Doc(new ParagraphNode(InlineContent.FromText("Title"), kind, TextAlign.Center), Para("below")),
            In(0, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p "Title" align=Center
            p "below"
            """,
            view);
        Assert.Equal(("Title", 0), Caret(view));
    }

    [AvaloniaFact]
    public void Backspace_in_an_empty_heading_alone_in_the_document_makes_it_body_text()
    {
        var view = Laid(Doc(Heading(string.Empty)), In(0, 0));

        Press(view, Key.Back);

        Shaped(
            """
            p ""
            """,
            view);
    }

    [AvaloniaFact]
    public void Backspace_at_the_start_of_the_document_in_body_text_still_does_nothing()
    {
        var view = Laid(Doc(Para("first"), Para("below")), In(0, 0));
        var before = view.State.Doc;

        Press(view, Key.Back);

        Assert.Equal(before, view.State.Doc);
        Assert.Equal(("first", 0), Caret(view));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static ParagraphNode Heading(string text, TextAlign align = TextAlign.Default) =>
        new(InlineContent.FromText(text), ParagraphKind.Heading1, align);

    private static ParagraphNode Quote(string text, TextAlign align = TextAlign.Default) =>
        new(InlineContent.FromText(text), ParagraphKind.Quote, align);

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

    /// <summary>
    /// The document's shape, indented, one node per line: kind, text with newlines and tabs written as
    /// \n and \t, then any alignment or indent that is not the default.
    /// </summary>
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
            ParagraphNode p => $"{Kind(p.Kind)} \"{Escaped(p.Content.Text)}\"{Attrs(p)}",
            ListNode { Kind: ListKind.Ordered } l => $"ordered-list from {l.Start}",
            ListNode => "unordered-list",
            _ => node.TypeName,
        };

        static string Attrs(ParagraphNode p) =>
            (p.Align == TextAlign.Default ? string.Empty : $" align={p.Align}")
            + (p.IndentLevel == 0 ? string.Empty : $" indent={p.IndentLevel}");

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