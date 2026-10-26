using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Find-and-replace keeps the formatting and link of the text it replaces.
/// </summary>
/// <remarks>
/// The replacement was written as plain text, so a bold word came back plain and a linked word
/// lost its address with no warning — while typing over the same selection kept both. The marks
/// of a match's first character are what the replacement takes, which is what typing over it does.
/// </remarks>
public class ReplaceKeepsMarksTests
{
    private static RichTextView Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc), MinHeight = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();

        return view;
    }

    private static InlineContent Content(RichTextView view) =>
        ((ParagraphNode)view.State.Doc.Blocks[0]).Content;

    [AvaloniaFact]
    public void Replacing_a_bold_match_keeps_it_bold()
    {
        var bold = new MarkSet { Style = TextStyle.Bold };
        var view = Shown(new DocumentNode([
            new ParagraphNode(InlineContent.Create("cat sat", [new ValueSpan<MarkSet>(0, 7, bold)], [])),
        ]));

        view.Find("cat");
        Assert.True(view.FindNext());
        Assert.True(view.ReplaceCurrent("dog"));

        Assert.Equal("dog sat", Content(view).Text);
        Assert.All(Enumerable.Range(0, 3), i => Assert.True(Content(view).MarkAt(i).Has(TextStyle.Bold), $"character {i}"));
    }

    [AvaloniaFact]
    public void Replacing_a_linked_match_keeps_the_link()
    {
        var link = new LinkMark("https://example.com");
        var view = Shown(new DocumentNode([
            new ParagraphNode(InlineContent.Create("see docs here", [new ValueSpan<MarkSet>(4, 4, new MarkSet { Link = link })], [])),
        ]));

        view.Find("docs");
        Assert.Equal(1, view.ReplaceAll("guide"));

        Assert.Equal("see guide here", Content(view).Text);
        Assert.All(Enumerable.Range(4, 5), i => Assert.Equal(link, Content(view).MarkAt(i).Link));
        Assert.Null(Content(view).MarkAt(3).Link);
        Assert.Null(Content(view).MarkAt(9).Link);
    }

    [AvaloniaFact]
    public void Each_match_keeps_its_own_formatting_in_ReplaceAll()
    {
        var bold = new MarkSet { Style = TextStyle.Bold };
        var italic = new MarkSet { Style = TextStyle.Italic };
        var view = Shown(new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                "cat cat cat",
                [new ValueSpan<MarkSet>(0, 3, bold), new ValueSpan<MarkSet>(8, 3, italic)],
                [])),
        ]));

        view.Find("cat");
        Assert.Equal(3, view.ReplaceAll("dogs"));

        var content = Content(view);

        Assert.Equal("dogs dogs dogs", content.Text);
        Assert.True(content.MarkAt(0).Has(TextStyle.Bold));
        Assert.Equal(MarkSet.Empty, content.MarkAt(5));
        Assert.True(content.MarkAt(10).Has(TextStyle.Italic));
    }

    /// <summary>The control: replacing plain text still writes plain text.</summary>
    [AvaloniaFact]
    public void Replacing_plain_text_writes_plain_text()
    {
        var view = Shown(DocumentNode.FromParagraphs("cat sat"));

        view.Find("cat");
        Assert.True(view.FindNext());
        Assert.True(view.ReplaceCurrent("dog"));

        Assert.Equal(MarkSet.Empty, Content(view).MarkAt(0));
    }
}
