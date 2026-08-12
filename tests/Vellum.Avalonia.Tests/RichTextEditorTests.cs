using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Tests for the public <see cref="RichTextEditor"/> shell.
/// </summary>
public class RichTextEditorTests
{
    [AvaloniaFact]
    public void TheDefaultTemplateProducesAScrolledView()
    {
        var editor = Shown("hello");

        Assert.NotNull(editor.View);

        var scroller = Assert.IsType<ScrollViewer>(editor.GetVisualChildren().Single());

        Assert.Same(editor.View, scroller.Content);
    }

    [AvaloniaFact]
    public void TheStateSetBeforeTemplatingReachesTheView()
    {
        // Order matters: the view has a default state of its own, and applying the template
        // after the application has set one must not throw that away.
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("early")),
        };

        var window = new Window { Width = 400, Height = 200, Content = editor };

        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));

        Assert.Equal("early", Text(editor.View!.State));
    }

    [AvaloniaFact]
    public void SettingTheStateOnTheShellReachesTheView()
    {
        var editor = Shown("hello");

        editor.State = EditorState.Create(DocumentNode.FromParagraphs("later"));

        Assert.Equal("later", Text(editor.View!.State));
    }

    [AvaloniaFact]
    public void AnEditInTheViewReachesTheShell()
    {
        var editor = Shown("hello");

        editor.View!.Focus();
        editor.View.MoveTo(editor.View.BlockStart + 5, extend: false);
        editor.View.InsertText("!");

        Assert.Equal("hello!", Text(editor.State));
    }

    [AvaloniaFact]
    public void TheShellReplaysTheViewsHistory()
    {
        var editor = Shown("hello");

        editor.View!.Focus();
        editor.View.MoveTo(editor.View.BlockStart + 5, extend: false);
        editor.View.InsertText("!");

        Assert.True(editor.CanUndo);
        Assert.True(editor.Undo());
        Assert.Equal("hello", Text(editor.State));
        Assert.True(editor.CanRedo);
        Assert.True(editor.Redo());
        Assert.Equal("hello!", Text(editor.State));
    }

    [AvaloniaFact]
    public void AnUntemplatedShellReplaysNothingRatherThanThrowing()
    {
        var editor = new RichTextEditor();

        Assert.Null(editor.View);
        Assert.Null(editor.History);
        Assert.False(editor.CanUndo);
        Assert.False(editor.CanRedo);
        Assert.False(editor.Undo());
        Assert.False(editor.Redo());
    }

    [AvaloniaFact]
    public void TheHistoryPolicyCanBeReplacedThroughTheShell()
    {
        var editor = Shown("hello");

        editor.History = History.With(HistoryPolicy.Never);

        Assert.Same(editor.History, editor.View!.History);
    }

    [AvaloniaFact]
    public void TheShellIsNotATabStopOfItsOwn()
    {
        // Two focusable elements in a row would put a tab stop in front of the editor that
        // does nothing when you reach it.
        var editor = Shown("hello");

        Assert.False(editor.Focusable);
        Assert.True(editor.View!.Focusable);
    }

    [AvaloniaFact]
    public void ATemplateWithoutTheViewPartIsRefused()
    {
        var editor = new RichTextEditor
        {
            Template = new FuncControlTemplate<RichTextEditor>((_, _) => new Border()),
        };

        var window = new Window { Width = 400, Height = 200, Content = editor };

        // Show runs a layout pass of its own, so the template is applied — and refused — there
        // rather than at the explicit Measure below.
        Assert.Throws<InvalidOperationException>(() =>
        {
            window.Show();
            window.Measure(new Size(400, 200));
        });
    }

    private static string Text(EditorState state) =>
        ((ParagraphNode)state.Doc.Blocks[0]).Content.Text;

    private static RichTextEditor Shown(string text)
    {
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(text)),
        };

        var window = new Window { Width = 400, Height = 200, Content = editor };

        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));

        return editor;
    }
}
