using Avalonia.Headless.XUnit;
using global::Avalonia.Controls;
using Avalonia.Input;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A list numbered from near <see cref="int.MaxValue"/>, which list commands used to overflow
/// computing the number the items after a split continue from.
/// </summary>
/// <remarks>
/// The overflow reached <see cref="ListNode"/>'s constructor as a negative start, which throws, and
/// the throw escaped the key handler: a 48-byte paste followed by Shift+Tab ended the process of
/// any host without a dispatcher exception handler.
/// </remarks>
public sealed class HostileListStartTests
{
    private static RichTextView InItem(DocumentNode doc, int item)
    {
        var list = Assert.IsType<ListNode>(doc.Blocks[0]);
        var position = 1 + list.Items.Take(item).Sum(i => i.NodeSize) + 1 + 1;

        return new RichTextView { State = EditorState.Create(doc, TextSelection.Cursor(position)) };
    }

    private static void Press(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });

    private static DocumentNode Ordered(int start, params string[] items) =>
        new([new ListNode(items.Select(ListItemNode.FromText), ListKind.Ordered, start)]);

    [AvaloniaFact]
    public void An_imported_start_is_clamped_and_Shift_Tab_does_not_throw()
    {
        var result = HtmlImporter.Import("<ol start=\"2147483646\"><li>a<li>b<li>c<li>d</ol>");
        var list = Assert.IsType<ListNode>(Assert.Single(result.Doc.Blocks));

        Assert.Equal(999_999_999, list.Start);
        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Downgraded && d.Context == "2147483646");

        var view = InItem(result.Doc, 1);
        var window = new Window { Width = 300, Height = 200, Content = view };

        window.Show();
        Press(view, Key.Tab, KeyModifiers.Shift);

        Assert.Equal("b", Assert.IsType<ParagraphNode>(view.State.Doc.Blocks[1]).Content.Text);
        Assert.Equal(999_999_999 + 2, Assert.IsType<ListNode>(view.State.Doc.Blocks[2]).Start);
    }

    [AvaloniaFact]
    public void Splitting_a_list_numbered_from_the_top_of_the_range_saturates()
    {
        var view = InItem(Ordered(int.MaxValue - 1, "a", "b", "c", "d"), 1);

        Assert.True(view.LiftListItem());

        Assert.Equal(int.MaxValue, Assert.IsType<ListNode>(view.State.Doc.Blocks[2]).Start);
    }

    [AvaloniaFact]
    public void Retyping_part_of_a_list_numbered_from_the_top_of_the_range_saturates()
    {
        var view = InItem(Ordered(int.MaxValue - 1, "a", "b", "c", "d"), 1);

        Assert.True(view.ToggleList(ListKind.Unordered));

        Assert.Equal(int.MaxValue, Assert.IsType<ListNode>(view.State.Doc.Blocks[2]).Start);
    }

    [AvaloniaFact]
    public void Lifting_out_of_a_nested_list_numbered_from_the_top_of_the_range_saturates()
    {
        var nested = new ListNode(
            [ListItemNode.FromText("b"), ListItemNode.FromText("c"), ListItemNode.FromText("d")],
            ListKind.Ordered,
            int.MaxValue);
        var doc = new DocumentNode([
            new ListNode([new ListItemNode([ParagraphNode.FromText("a"), nested])], ListKind.Ordered),
        ]);

        // The caret in "b": into the outer list, its item, past "a", into the nested list and
        // its first item's paragraph.
        var outerItem = Assert.IsType<ListNode>(doc.Blocks[0]).Items[0];
        var position = 1 + 1 + outerItem.Blocks[0].NodeSize + 1 + 1 + 1;
        var view = new RichTextView { State = EditorState.Create(doc, TextSelection.Cursor(position)) };

        Assert.Equal("b", view.State.Doc.Resolve(position).Paragraph!.Content.Text);
        Assert.True(view.LiftListItem());

        var lifted = Assert.IsType<ListNode>(view.State.Doc.Blocks[0]).Items[1];
        Assert.Equal(int.MaxValue, Assert.IsType<ListNode>(lifted.Blocks[1]).Start);
    }
}
