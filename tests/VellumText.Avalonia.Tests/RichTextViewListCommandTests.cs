using System.Text;
using global::Avalonia;
using global::Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Making, unmaking and re-shaping lists.
/// </summary>
/// <remarks>
/// <para>
/// Every test here asserts on the whole tree rather than on one node, because a list command
/// rebuilds a run of blocks and the interesting failures are the ones that leave something
/// behind — an empty item, a stray second list, a number that restarted.
/// </para>
/// <para>
/// The caret is asserted separately and deliberately: a replace step maps every position inside
/// the range it replaced onto the range's edge, so a command that got the selection wrong would
/// still produce a perfectly correct document with the cursor at the top of the list.
/// </para>
/// </remarks>
public class RichTextViewListCommandTests
{
    // ---- turning a list on ------------------------------------------------------------------

    [AvaloniaFact]
    public void Bullets_wrap_the_paragraph_the_caret_is_in()
    {
        var view = At(Paragraphs("one", "two"), In(0, 1));

        Assert.True(view.ToggleList(ListKind.Unordered));

        Shaped(
            """
            unordered-list
              list-item
                p "one"
            p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void Bullets_wrap_every_paragraph_the_selection_touches()
    {
        var view = Across(Paragraphs("one", "two", "three"), In(0, 1), In(1, 1));

        Assert.True(view.ToggleList(ListKind.Unordered));

        Shaped(
            """
            unordered-list
              list-item
                p "one"
              list-item
                p "two"
            p "three"
            """,
            view);
    }

    [AvaloniaFact]
    public void Wrapping_leaves_the_caret_on_the_character_it_was_on()
    {
        var view = At(Paragraphs("one", "two"), In(1, 2));

        view.ToggleList(ListKind.Ordered);

        Assert.Equal(("two", 2), Caret(view));
    }

    [AvaloniaFact]
    public void Wrapping_keeps_a_selection_that_spans_two_paragraphs()
    {
        var view = Across(Paragraphs("one", "two"), In(0, 1), In(1, 2));

        view.ToggleList(ListKind.Unordered);

        Assert.Equal(("one", 1), CaretAt(view, view.State.Selection.Anchor));
        Assert.Equal(("two", 2), CaretAt(view, view.State.Selection.Head));
    }

    [AvaloniaFact]
    public void A_paragraph_below_a_list_of_the_same_kind_joins_it_rather_than_starting_a_second()
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("one")], ListKind.Ordered),
            ParagraphNode.FromText("two"),
        ]);

        var view = At(doc, In(1, 1));

        Assert.True(view.ToggleList(ListKind.Ordered));

        Shaped(
            """
            ordered-list from 1
              list-item
                p "one"
              list-item
                p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void A_paragraph_above_a_list_of_the_same_kind_joins_it_too()
    {
        var doc = new DocumentNode([
            ParagraphNode.FromText("one"),
            new ListNode([ListItemNode.FromText("two")]),
        ]);

        var view = At(doc, In(0, 1));

        Assert.True(view.ToggleList(ListKind.Unordered));

        Shaped(
            """
            unordered-list
              list-item
                p "one"
              list-item
                p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void A_list_of_the_other_kind_inside_the_selection_is_absorbed_rather_than_nested()
    {
        var doc = new DocumentNode([
            ParagraphNode.FromText("one"),
            new ListNode([ListItemNode.FromText("two")], ListKind.Ordered),
        ]);

        var view = Across(doc, In(0, 1), In(1, 1));

        Assert.True(view.ToggleList(ListKind.Unordered));

        Shaped(
            """
            unordered-list
              list-item
                p "one"
              list-item
                p "two"
            """,
            view);
    }

    // ---- turning it off, and re-typing it ---------------------------------------------------

    [AvaloniaFact]
    public void Pressing_the_same_kind_again_unwraps()
    {
        var view = At(Bulleted("one", "two"), In(0, 1));

        Assert.True(view.ToggleList(ListKind.Unordered));

        Shaped(
            """
            p "one"
            unordered-list
              list-item
                p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void Pressing_the_other_kind_re_types_the_items_that_were_touched()
    {
        var view = At(Bulleted("one", "two"), In(0, 1));

        Assert.True(view.ToggleList(ListKind.Ordered));

        Shaped(
            """
            ordered-list from 1
              list-item
                p "one"
            unordered-list
              list-item
                p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void Unwrapping_the_middle_of_a_list_splits_it_and_carries_the_numbering_on()
    {
        var doc = new DocumentNode([
            new ListNode(
                [ListItemNode.FromText("a"), ListItemNode.FromText("b"), ListItemNode.FromText("c")],
                ListKind.Ordered),
        ]);

        var view = At(doc, In(1, 1));

        Assert.True(view.ToggleList(ListKind.Ordered));

        Shaped(
            """
            ordered-list from 1
              list-item
                p "a"
            p "b"
            ordered-list from 3
              list-item
                p "c"
            """,
            view);
    }

    [AvaloniaFact]
    public void Unwrapping_leaves_the_caret_where_it_was()
    {
        var view = At(Bulleted("one", "two"), In(1, 3));

        view.ToggleList(ListKind.Unordered);

        Assert.Equal(("two", 3), Caret(view));
    }

    // ---- nesting -----------------------------------------------------------------------------

    [AvaloniaFact]
    public void An_item_nests_under_the_one_before_it()
    {
        var view = At(Bulleted("one", "two"), In(1, 1));

        Assert.True(view.NestListItem());

        Shaped(
            """
            unordered-list
              list-item
                p "one"
                unordered-list
                  list-item
                    p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void The_first_item_of_a_list_cannot_nest_because_there_is_nothing_to_nest_under()
    {
        var view = At(Bulleted("one", "two"), In(0, 1));

        Assert.False(view.NestListItem());
        Assert.False(view.CanNestListItem);
    }

    [AvaloniaFact]
    public void A_second_nested_item_joins_the_nested_list_rather_than_starting_another()
    {
        var view = At(Bulleted("one", "two", "three"), In(1, 1));

        view.NestListItem();

        // "three" is now the second item of the outer list again, so nesting it puts it beside
        // "two" rather than beneath it.
        Move(view, In(2, 1));
        Assert.True(view.NestListItem());

        Shaped(
            """
            unordered-list
              list-item
                p "one"
                unordered-list
                  list-item
                    p "two"
                  list-item
                    p "three"
            """,
            view);
    }

    [AvaloniaFact]
    public void Nesting_leaves_the_caret_in_the_item_that_moved()
    {
        var view = At(Bulleted("one", "two"), In(1, 2));

        view.NestListItem();

        Assert.Equal(("two", 2), Caret(view));
    }

    // ---- lifting -----------------------------------------------------------------------------

    [AvaloniaFact]
    public void A_nested_item_lifts_back_into_the_list_outside_it()
    {
        var view = At(Bulleted("one", "two"), In(1, 1));

        view.NestListItem();
        Assert.True(view.LiftListItem());

        Shaped(
            """
            unordered-list
              list-item
                p "one"
              list-item
                p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void What_followed_a_lifted_item_stays_nested_under_it()
    {
        var doc = new DocumentNode([
            new ListNode([
                new ListItemNode([
                    ParagraphNode.FromText("one"),
                    new ListNode([ListItemNode.FromText("a"), ListItemNode.FromText("b")]),
                ]),
            ]),
        ]);

        var view = At(doc, In(1, 1));

        Assert.True(view.LiftListItem());

        Shaped(
            """
            unordered-list
              list-item
                p "one"
              list-item
                p "a"
                unordered-list
                  list-item
                    p "b"
            """,
            view);
    }

    [AvaloniaFact]
    public void An_item_holding_nothing_but_a_nested_list_goes_when_the_nested_list_leaves()
    {
        var doc = new DocumentNode([
            new ListNode([
                new ListItemNode([new ListNode([ListItemNode.FromText("only")])]),
            ]),
        ]);

        var view = At(doc, In(0, 1));

        Assert.True(view.LiftListItem());

        Shaped(
            """
            unordered-list
              list-item
                p "only"
            """,
            view);
    }

    [AvaloniaFact]
    public void Lifting_out_of_a_top_level_list_leaves_the_list_altogether()
    {
        var view = At(Bulleted("one"), In(0, 1));

        Assert.True(view.LiftListItem());

        Assert.Equal("p \"one\"", Shape(view));
    }

    [AvaloniaFact]
    public void Lifting_outside_a_list_does_nothing()
    {
        var view = At(Paragraphs("one"), In(0, 1));

        Assert.False(view.LiftListItem());
    }

    [AvaloniaFact]
    public void Lifting_leaves_the_caret_in_the_item_that_moved()
    {
        var view = At(Bulleted("one", "two"), In(1, 3));

        view.NestListItem();
        view.LiftListItem();

        Assert.Equal(("two", 3), Caret(view));
    }

    // ---- pressing Enter and Tab --------------------------------------------------------------

    [AvaloniaFact]
    public void Enter_in_an_empty_bullet_leaves_the_list()
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("one"), new ListItemNode([ParagraphNode.Empty])]),
        ]);

        var view = Laid(doc, In(1, 0));

        Assert.True(view.Split());

        Shaped(
            """
            unordered-list
              list-item
                p "one"
            p ""
            """,
            view);
    }

    [AvaloniaFact]
    public void Enter_in_a_bullet_with_words_in_it_still_splits_it()
    {
        var view = Laid(Bulleted("one"), In(0, 3));

        Assert.True(view.Split());

        Shaped(
            """
            unordered-list
              list-item
                p "one"
                p ""
            """,
            view);
    }

    [AvaloniaFact]
    public void Tab_nests_inside_a_list_and_indents_outside_one()
    {
        var listed = At(Bulleted("one", "two"), In(1, 1));

        Assert.True(listed.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.Contains("    unordered-list", Shape(listed));

        var plain = At(Paragraphs("one"), In(0, 1));

        Assert.True(plain.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.Equal(1, ((ParagraphNode)plain.State.Doc.Blocks[0]).IndentLevel);
    }

    [AvaloniaFact]
    public void Shift_tab_lifts_inside_a_list_and_outdents_outside_one()
    {
        var listed = At(Bulleted("one"), In(0, 1));

        Assert.True(listed.BlockShortcut(Key.Tab, control: false, shift: true));
        Assert.Equal("p \"one\"", Shape(listed));

        var plain = At(
            new DocumentNode([ParagraphNode.FromText("one").WithIndentLevel(2)]), In(0, 1));

        Assert.True(plain.BlockShortcut(Key.Tab, control: false, shift: true));
        Assert.Equal(1, ((ParagraphNode)plain.State.Doc.Blocks[0]).IndentLevel);
    }

    // ---- what the toolbar reads --------------------------------------------------------------

    [AvaloniaFact]
    public void The_kind_of_list_the_caret_is_in_is_reported()
    {
        Assert.Null(At(Paragraphs("one"), In(0, 1)).ListKindAt);
        Assert.Equal(ListKind.Unordered, At(Bulleted("one"), In(0, 1)).ListKindAt);

        var ordered = new DocumentNode([
            new ListNode([ListItemNode.FromText("one")], ListKind.Ordered),
        ]);

        Assert.Equal(ListKind.Ordered, At(ordered, In(0, 1)).ListKindAt);
    }

    // ---- undo --------------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("wrap")]
    [InlineData("nest")]
    [InlineData("unwrap")]
    public void Every_list_command_is_one_undo_back_to_exactly_what_was_there(string command)
    {
        var doc = Bulleted("one", "two");
        var view = At(doc, In(1, 1));

        Assert.True(command switch
        {
            "wrap" => view.ToggleList(ListKind.Ordered),
            "nest" => view.NestListItem(),
            _ => view.ToggleList(ListKind.Unordered),
        });

        Assert.NotEqual(doc, view.State.Doc);
        Assert.True(view.Undo());
        Assert.Equal(doc, view.State.Doc);
    }

    /// <summary>
    /// Tab on the first item of a list is consumed and changes nothing, rather than indenting
    /// the bullet or letting focus escape.
    /// </summary>
    /// <remarks>
    /// Nesting is impossible there because there is no item above to nest under. Falling through
    /// to a plain indent is worse than refusing: it slides the bullet right, so it reads as
    /// nested while behaving as a top-level item, and each further Tab slides it further.
    /// Reporting it unhandled is worse still, because Tab then moves focus out of the editor.
    /// </remarks>
    [AvaloniaFact]
    public void Tab_on_the_first_item_of_a_list_does_not_indent_the_bullet_instead()
    {
        var view = At(Bulleted("one", "two"), In(0, 1));
        var before = view.State.Doc;

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: false));
        Assert.Equal(before, view.State.Doc);
    }

    /// <summary>Shift+Tab on a top-level item takes it out of the list, rather than outdenting.</summary>
    /// <remarks>Lifting always has somewhere to go, so unlike Tab this needs no special case; the
    /// test is here to pin that the outdent arm is genuinely unreachable inside a list.</remarks>
    [AvaloniaFact]
    public void Shift_tab_on_a_top_level_list_item_takes_it_out_of_the_list()
    {
        var view = At(Bulleted("one", "two"), In(1, 1));

        Assert.True(view.BlockShortcut(Key.Tab, control: false, shift: true));

        Shaped(
            """
            unordered-list
              list-item
                p "one"
            p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void A_list_of_the_other_kind_above_is_left_alone_rather_than_absorbed()
    {
        var doc = new DocumentNode([
            new ListNode([ListItemNode.FromText("one")], ListKind.Ordered),
            ParagraphNode.FromText("two"),
        ]);

        var view = At(doc, In(1, 1));

        Assert.True(view.ToggleList(ListKind.Unordered));

        Shaped(
            """
            ordered-list from 1
              list-item
                p "one"
            unordered-list
              list-item
                p "two"
            """,
            view);
    }

    [AvaloniaFact]
    public void A_list_of_the_other_kind_below_is_left_alone_rather_than_absorbed()
    {
        var doc = new DocumentNode([
            ParagraphNode.FromText("one"),
            new ListNode([ListItemNode.FromText("two")], ListKind.Ordered),
        ]);

        var view = At(doc, In(0, 1));

        Assert.True(view.ToggleList(ListKind.Unordered));

        Shaped(
            """
            unordered-list
              list-item
                p "one"
            ordered-list from 1
              list-item
                p "two"
            """,
            view);
    }

    // ---- helpers -----------------------------------------------------------------------------

    private static DocumentNode Paragraphs(params string[] text) =>
        new(text.Select(ParagraphNode.FromText));

    private static DocumentNode Bulleted(params string[] text) =>
        new([new ListNode(text.Select(ListItemNode.FromText))]);

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

    private static RichTextView At(DocumentNode doc, Func<DocumentNode, int> where) =>
        new() { State = EditorState.Create(doc, TextSelection.Cursor(where(doc))) };

    /// <summary>A view that has been measured, which is what a command needing layout requires.</summary>
    private static RichTextView Laid(DocumentNode doc, Func<DocumentNode, int> where)
    {
        var view = At(doc, where);
        var root = new Window { Width = 300, Height = 200, Content = view };

        root.Show();
        view.Measure(new Size(300, 200));
        view.Arrange(new Rect(0, 0, 300, 200));

        return view;
    }

    private static RichTextView Across(
        DocumentNode doc, Func<DocumentNode, int> anchor, Func<DocumentNode, int> head) =>
        new() { State = EditorState.Create(doc, new TextSelection(anchor(doc), head(doc))) };

    private static void Move(RichTextView view, Func<DocumentNode, int> where) =>
        view.State = EditorState.Create(
            view.State.Doc, TextSelection.Cursor(where(view.State.Doc)));

    private static (string Text, int Offset) Caret(RichTextView view) =>
        CaretAt(view, view.State.Selection.Head);

    private static (string Text, int Offset) CaretAt(RichTextView view, int pos)
    {
        var at = view.State.Doc.Resolve(pos);

        return (at.Paragraph!.Content.Text, at.ParentOffset);
    }

    /// <summary>The document's shape, indented, one node per line.</summary>
    private static void Shaped(string expected, RichTextView view) =>
        Assert.Equal(expected.ReplaceLineEndings("\n"), Shape(view));

    private static string Shape(RichTextView view)
    {
        var text = new StringBuilder();

        Write(view.State.Doc, -1);

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
            ParagraphNode p => $"p \"{p.Content.Text}\"",
            ListNode { Kind: ListKind.Ordered } l => $"ordered-list from {l.Start}",
            ListNode => "unordered-list",
            _ => node.TypeName,
        };
    }
}
