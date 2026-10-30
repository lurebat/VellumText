using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// With the caret between two adjacent links, the link a dialog is shown and the link it edits
/// are the same one.
/// </summary>
/// <remarks>
/// <see cref="RichTextView.SelectionLink"/> preferred the character before the caret and the edit
/// preferred the one after, so Ctrl+K on <c>&lt;a&gt;x&lt;/a&gt;&lt;a&gt;y&lt;/a&gt;</c> opened the
/// dialog on the first link and rewrote — or removed — the second.
/// </remarks>
public class LinkJunctionTests
{
    private static readonly LinkMark A = new("https://a.example");
    private static readonly LinkMark B = new("https://b.example");
    private static readonly LinkMark C = new("https://c.example");

    private static RichTextView AtJunction()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                "aaabbb",
                [new ValueSpan<MarkSet>(0, 3, new MarkSet { Link = A }), new ValueSpan<MarkSet>(3, 3, new MarkSet { Link = B })],
                [])),
        ]);
        var view = new RichTextView { State = EditorState.Create(doc), MinHeight = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        view.MoveTo(1 + 3, extend: false);

        return view;
    }

    private static LinkMark? LinkOf(RichTextView view, int offset) =>
        ((ParagraphNode)view.State.Doc.Blocks[0]).Content.MarkAt(offset).Link;

    [AvaloniaFact]
    public void ApplyLink_edits_the_link_SelectionLink_reported()
    {
        var view = AtJunction();

        Assert.Equal(A, view.SelectionLink);
        Assert.True(view.ApplyLink(C));

        Assert.Equal(C, LinkOf(view, 0));
        Assert.Equal(B, LinkOf(view, 3));
    }

    [AvaloniaFact]
    public void RemoveLink_removes_the_link_SelectionLink_reported()
    {
        var view = AtJunction();

        Assert.Equal(A, view.SelectionLink);
        Assert.True(view.RemoveLink());

        Assert.Null(LinkOf(view, 0));
        Assert.Equal(B, LinkOf(view, 3));
    }

    /// <summary>The control: at the start of a link with plain text before it, that link is the one.</summary>
    [AvaloniaFact]
    public void At_the_start_of_a_link_the_link_after_the_caret_is_edited()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create("xx bbb", [new ValueSpan<MarkSet>(3, 3, new MarkSet { Link = B })], [])),
        ]);
        var view = new RichTextView { State = EditorState.Create(doc), MinHeight = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        view.MoveTo(1 + 3, extend: false);

        Assert.Equal(B, view.SelectionLink);
        Assert.True(view.ApplyLink(C));
        Assert.Equal(C, LinkOf(view, 3));
    }
}
