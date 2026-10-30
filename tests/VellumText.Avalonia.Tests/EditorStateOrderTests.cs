using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// When a host sets <see cref="RichTextEditor.State"/>, its change events fire after the view
/// already holds the new state.
/// </summary>
/// <remarks>
/// The shell raised <c>DocumentChanged</c> and <c>SelectionChanged</c> before pushing the state into
/// its view. A handler that read the view saw the old document, and one that touched the view's
/// selection — select-all on load, for instance — sent the old document back to the shell, so
/// both reverted to the clip the host had just replaced.
/// </remarks>
public class EditorStateOrderTests
{
    private static RichTextEditor Shown(string text)
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs(text)) };
        var window = new Window { Width = 400, Height = 300, Content = editor };

        window.Show();
        editor.EnsureView();

        return editor;
    }

    [AvaloniaFact]
    public void DocumentChanged_sees_the_new_document_in_the_view()
    {
        var editor = Shown("old");
        DocumentNode? seen = null;
        string? selected = null;

        editor.DocumentChanged += (_, _) =>
        {
            seen = editor.View!.Document;
            selected = editor.View!.SelectedText();
        };

        var fresh = DocumentNode.FromParagraphs("brand new");

        editor.State = EditorState.Create(fresh, TextSelection.Create(fresh, 1, 6));

        Assert.Same(fresh, seen);
        Assert.Equal("brand", selected);
    }

    [AvaloniaFact]
    public void SelectionChanged_sees_the_new_selection_in_the_view()
    {
        var editor = Shown("hello world");
        Selection? seen = null;

        editor.SelectionChanged += (_, _) => seen = editor.View!.State.Selection;

        var doc = editor.Document;
        var selection = TextSelection.Create(doc, 1, 6);

        editor.State = EditorState.Create(doc, selection);

        Assert.Equal(selection, seen);
    }

    [AvaloniaFact]
    public void Selecting_all_on_load_keeps_the_loaded_document()
    {
        var editor = Shown("old clip");

        editor.DocumentChanged += (_, _) => editor.EnsureView().SelectAll();

        var fresh = DocumentNode.FromParagraphs("new clip");

        editor.State = EditorState.Create(fresh);

        Assert.Same(fresh, editor.Document);
        Assert.Same(fresh, editor.View!.Document);
        Assert.Equal("new clip", editor.View!.SelectedText());
    }
}
