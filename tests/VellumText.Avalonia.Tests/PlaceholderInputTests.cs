using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The embed placeholder U+FFFC arriving as typed, composed or replacement text is dropped rather
/// than thrown on.
/// </summary>
/// <remarks>
/// U+FFFC marks where an embed sits, and the model refuses text carrying one without a matching
/// embed. Paste already strips it — Word writes it for objects, and anything that has been through
/// a rich text model carries it — but typing, an IME commit and find-and-replace did not, so the
/// refusal escaped the input handler as an <see cref="ArgumentException"/> and took down any host
/// without a dispatcher handler.
/// </remarks>
public class PlaceholderInputTests
{
    private static (Window Window, RichTextView View) Shown(params string[] paragraphs)
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)) };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();
        view.Focus();

        return (window, view);
    }

    [AvaloniaFact]
    public void Text_input_carrying_the_placeholder_does_not_escape_the_handler()
    {
        var (window, view) = Shown("cat");

        view.MoveTo(1, extend: false);

        var thrown = Record.Exception(() => window.KeyTextInput("a\uFFFCb"));

        Assert.Null(thrown);
        Assert.Equal("abcat", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void InsertText_drops_the_placeholder()
    {
        var (_, view) = Shown("cat");

        view.MoveTo(1, extend: false);

        Assert.True(view.InsertText("a\uFFFCb"));
        Assert.Equal("abcat", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void Text_that_is_only_the_placeholder_changes_nothing()
    {
        var (_, view) = Shown("cat");
        var before = view.State.Doc;

        view.MoveTo(1, extend: false);

        Assert.False(view.InsertText("\uFFFC"));
        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void ReplaceAll_drops_the_placeholder_from_the_replacement()
    {
        var (_, view) = Shown("cat and cat");

        view.Find("cat");

        Assert.Equal(2, view.ReplaceAll("a\uFFFCb"));
        Assert.Equal("ab and ab", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void ReplaceCurrent_drops_the_placeholder_and_puts_the_caret_after_what_was_written()
    {
        var (_, view) = Shown("cat sat");

        view.Find("cat");
        Assert.True(view.FindNext());

        Assert.True(view.ReplaceCurrent("a\uFFFCb"));
        Assert.Equal("ab sat", DocumentText.Of(view.State.Doc));
        Assert.Equal(1 + 2, view.State.Selection.Head);
    }
}
