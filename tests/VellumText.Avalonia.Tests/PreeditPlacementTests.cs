using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A composition over a selection is drawn where the commit will land: at the start of the
/// selection, in that paragraph.
/// </summary>
/// <remarks>
/// It was anchored in the paragraph holding the selection's head, at the start offset clamped into
/// that paragraph — so composing over "ello\nwo" drew the composed text at the start of "world"
/// while the commit replaced the selection from after the "h" of "hello". A real Windows IME could
/// not be driven synthetically in the audit; this is pinned headlessly, and a person at a physical
/// keyboard is still needed to confirm it with a real input method.
/// </remarks>
public class PreeditPlacementTests
{
    private static RichTextView Shown(params string[] paragraphs)
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return view;
    }

    [AvaloniaFact]
    public void A_preedit_over_a_cross_paragraph_selection_is_drawn_where_the_commit_lands()
    {
        var view = Shown("hello", "world");

        view.MoveTo(2, extend: false);
        view.MoveTo(10, extend: true);

        var from = view.State.Selection.From;

        Assert.True(view.SetPreedit("\u3042"));

        var holder = Assert.Single(view.Blocks.OfType<ParagraphView>(), b => b.Preedit is not null);

        Assert.Equal("hello", holder.Paragraph.Content.Text);
        Assert.Equal(from - 1, holder.Preedit!.Offset);
        Assert.True(view.IsComposing);
        Assert.Same(holder.Preedit, view.Preedit);

        view.SetPreedit(null);

        Assert.False(view.IsComposing);
        Assert.DoesNotContain(view.Blocks.OfType<ParagraphView>(), b => b.Preedit is not null);

        Assert.True(view.InsertText("\u3042"));
        Assert.Equal("h\u3042rld", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void The_caret_follows_the_composition_into_the_first_paragraph()
    {
        var view = Shown("hello", "world");

        view.MoveTo(2, extend: false);
        var firstLine = view.CaretRect();

        view.MoveTo(10, extend: true);
        Assert.True(view.SetPreedit("\u3042"));

        Assert.Equal(firstLine.Y, view.CaretRect().Y);
    }

    /// <summary>The control: within one paragraph the composition always sat at the selection's start.</summary>
    [AvaloniaFact]
    public void A_preedit_over_a_selection_in_one_paragraph_sits_at_its_start()
    {
        var view = Shown("hello world");

        view.MoveTo(3, extend: false);
        view.MoveTo(8, extend: true);

        Assert.True(view.SetPreedit("\u3042"));

        var holder = Assert.Single(view.Blocks.OfType<ParagraphView>(), b => b.Preedit is not null);

        Assert.Equal(2, holder.Preedit!.Offset);
    }
}
