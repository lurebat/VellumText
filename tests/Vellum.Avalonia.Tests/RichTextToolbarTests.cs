using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// What the toolbar reports about the selection, and what its commands do to the document.
/// </summary>
/// <remarks>
/// Nothing here applies a template. The point of the control is that its state and commands are
/// separable from the buttons, so the tests exercise them the way markup would — through the
/// public properties and <c>ICommand.Execute</c> — and never depend on a theme being loaded.
/// </remarks>
public class RichTextToolbarTests
{
    [AvaloniaFact]
    public void ABarWithNoEditorIsInertRatherThanBroken()
    {
        var bar = new RichTextToolbar();

        Assert.Null(bar.Editor);
        Assert.False(bar.IsBold);
        Assert.Null(bar.ParagraphKind);
        Assert.False(bar.CanUndo);

        // The point: a bar built in markup before its editor is bound must survive being clicked.
        bar.ToggleStyleCommand.Execute(TextStyle.Bold);
        bar.UndoCommand.Execute(null);
        bar.SetParagraphKindCommand.Execute(ParagraphKind.Heading1);
    }

    /// <summary>
    /// The view does not exist until the editor is templated, which happens after the toolbar has
    /// been built and bound. A bar that read the view once would be attached to nothing forever.
    /// </summary>
    [AvaloniaFact]
    public void ABarBoundBeforeTheEditorIsTemplatedStillReachesTheView()
    {
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("hello")),
        };

        var bar = new RichTextToolbar { Editor = editor };

        Assert.Null(editor.View);

        Show(editor);

        Assert.NotNull(editor.View);

        bar.ToggleStyleCommand.Execute(TextStyle.Bold);

        Assert.True(bar.IsBold);
    }

    [AvaloniaFact]
    public void TheBarShowsWhatTheSelectionIsFormattedAs()
    {
        var (bar, view) = Bar("hello");

        Assert.False(bar.IsBold);
        Assert.False(bar.IsItalic);

        SelectAll(view);
        bar.ToggleStyleCommand.Execute(TextStyle.Bold);
        bar.ToggleStyleCommand.Execute(TextStyle.Italic);

        Assert.True(bar.IsBold);
        Assert.True(bar.IsItalic);
        Assert.False(bar.IsUnderline);
    }

    /// <summary>
    /// Driven from the document rather than from what was last clicked, which is what makes undo
    /// put the bar back and makes moving the caret into bold text light the bold button.
    /// </summary>
    [AvaloniaFact]
    public void UndoPutsTheBarBack()
    {
        var (bar, view) = Bar("hello");

        SelectAll(view);
        bar.ToggleStyleCommand.Execute(TextStyle.Bold);

        Assert.True(bar.IsBold);
        Assert.True(bar.CanUndo);

        bar.UndoCommand.Execute(null);

        Assert.False(bar.IsBold);
        Assert.False(bar.CanUndo);
        Assert.True(bar.CanRedo);
    }

    [AvaloniaFact]
    public void AHalfBoldSelectionReportsNotBold()
    {
        var (bar, view) = Bar("hello");

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[0].Start + 2, extend: true);
        bar.ToggleStyleCommand.Execute(TextStyle.Bold);

        Assert.True(bar.IsBold);

        SelectAll(view);

        Assert.False(bar.IsBold);
    }

    [AvaloniaFact]
    public void TheBarShowsWhichHeadingAndAlignmentTheParagraphIs()
    {
        var (bar, _) = Bar("hello");

        Assert.Equal(ParagraphKind.Body, bar.ParagraphKind);
        Assert.Equal(TextAlign.Default, bar.Align);

        bar.SetParagraphKindCommand.Execute(ParagraphKind.Heading2);
        bar.SetAlignCommand.Execute(TextAlign.Center);

        Assert.Equal(ParagraphKind.Heading2, bar.ParagraphKind);
        Assert.Equal(TextAlign.Center, bar.Align);
    }

    [AvaloniaFact]
    public void TheBarShowsWhichListTheCaretIsIn()
    {
        var (bar, _) = Bar("hello");

        Assert.Null(bar.ListKind);

        bar.ToggleListCommand.Execute(ListKind.Unordered);

        Assert.Equal(ListKind.Unordered, bar.ListKind);

        bar.ToggleListCommand.Execute(ListKind.Unordered);

        Assert.Null(bar.ListKind);
    }

    [AvaloniaFact]
    public void ColourAndHighlightAreReportedBack()
    {
        var (bar, view) = Bar("hello");

        SelectAll(view);

        Assert.Null(bar.SelectionForeground);

        bar.SetForegroundCommand.Execute(new Rgba(255, 0, 0, 255));
        bar.SetHighlightCommand.Execute(new Rgba(255, 255, 0, 255));

        Assert.Equal(new Rgba(255, 0, 0, 255), bar.SelectionForeground);
        Assert.Equal(new Rgba(255, 255, 0, 255), bar.SelectionHighlight);

        bar.SetForegroundCommand.Execute(null);

        Assert.Null(bar.SelectionForeground);
    }

    /// <summary>
    /// Markup hands sizes over as strings and view models as numbers. Both mean the same thing,
    /// so both are accepted rather than making every caller convert.
    /// </summary>
    [AvaloniaFact]
    public void AFontSizeArrivesAsEitherAStringOrANumber()
    {
        var (bar, view) = Bar("hello");

        SelectAll(view);

        bar.SetFontSizeCommand.Execute("18");
        Assert.Equal(18, bar.SelectionFontSize);

        bar.SetFontSizeCommand.Execute(24.0);
        Assert.Equal(24, bar.SelectionFontSize);

        bar.SetFontSizeCommand.Execute("not a size");
        Assert.Null(bar.SelectionFontSize);
    }

    [AvaloniaFact]
    public void AFontFamilyOfBlankMeansTheDocumentDefault()
    {
        var (bar, view) = Bar("hello");

        SelectAll(view);

        bar.SetFontFamilyCommand.Execute("Georgia");
        Assert.Equal("Georgia", bar.SelectionFontFamily);

        bar.SetFontFamilyCommand.Execute("   ");
        Assert.Null(bar.SelectionFontFamily);
    }

    /// <summary>The button that makes a link is the button that removes one.</summary>
    [AvaloniaFact]
    public void TheLinkCommandTogglesRatherThanOnlySetting()
    {
        var (bar, view) = Bar("hello");

        SelectAll(view);

        Assert.False(bar.IsLink);

        bar.SetLinkCommand.Execute("https://example.com/");

        Assert.True(bar.IsLink);

        bar.SetLinkCommand.Execute("https://example.com/");

        Assert.False(bar.IsLink);
    }

    /// <summary>
    /// A parameter of the wrong type is a markup mistake. Throwing from a click handler tears the
    /// application down, so nothing happening is the recoverable failure.
    /// </summary>
    [AvaloniaFact]
    public void ACommandGivenTheWrongKindOfParameterDoesNothingRatherThanThrow()
    {
        var (bar, view) = Bar("hello");
        var before = view.State.Doc;

        bar.SetParagraphKindCommand.Execute("Heading1");
        bar.ToggleStyleCommand.Execute(17);
        bar.SetAlignCommand.Execute(null);

        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void IndentAndOutdentReachTheParagraph()
    {
        var (bar, view) = Bar("hello");

        bar.IndentCommand.Execute(null);
        Assert.Equal(1, view.IndentLevelAt);

        bar.OutdentCommand.Execute(null);
        Assert.Equal(0, view.IndentLevelAt);
    }

    [AvaloniaFact]
    public void ClearingFormattingStripsTheSelection()
    {
        var (bar, view) = Bar("hello");

        SelectAll(view);
        bar.ToggleStyleCommand.Execute(TextStyle.Bold);
        bar.SetForegroundCommand.Execute(new Rgba(255, 0, 0, 255));

        Assert.True(bar.IsBold);

        bar.ClearFormattingCommand.Execute(null);

        Assert.False(bar.IsBold);
        Assert.Null(bar.SelectionForeground);
    }

    /// <summary>
    /// An unbound toolbar must be collectable, or every editor a long-lived document ever showed
    /// keeps its bar alive through the event it never unsubscribed from.
    /// </summary>
    /// <remarks>
    /// This is what the unsubscribe in <c>Attach</c> is for. No behavioural test can see it: the
    /// bar reads its values from the editor it is currently pointed at, so a stale subscription
    /// produces redundant work and correct answers. The cost is a leak, and the leak is the test.
    /// </remarks>
    [AvaloniaFact]
    public void AnUnboundToolbarIsNotKeptAliveByTheEditorItLeft()
    {
        var editor = Show(new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("hello")),
        });

        var weak = Bind(editor);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive, "the editor is still holding on to a toolbar nobody is using");

        // Kept in scope so the editor itself cannot be collected before the assertion, which
        // would make the test pass for the wrong reason.
        Assert.NotNull(editor.View);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static WeakReference Bind(RichTextEditor editor)
        {
            var bar = new RichTextToolbar { Editor = editor };

            Assert.False(bar.IsBold);

            bar.Editor = null;

            return new WeakReference(bar);
        }
    }

    /// <summary>
    /// Rebinding must stop the old editor driving the bar, or a window with two documents would
    /// show whichever was last touched rather than the one on screen.
    /// </summary>
    /// <remarks>
    /// Retemplating the abandoned editor is what makes this observable. A stale subscription that
    /// only heard about state would be harmless, because the bar reads its values from the view
    /// it is pointed at; a stale subscription that hears the old editor announce a new view
    /// repoints the bar at a document nobody asked for.
    /// </remarks>
    [AvaloniaFact]
    public void RebindingToAnotherEditorLeavesTheFirstOneBehind()
    {
        var (bar, first) = Bar("hello");
        var abandoned = bar.Editor!;

        SelectAll(first);
        bar.ToggleStyleCommand.Execute(TextStyle.Bold);

        Assert.True(bar.IsBold);

        var second = Show(new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("second")),
        });

        bar.Editor = second;

        Assert.False(bar.IsBold);

        Retemplate(abandoned);

        // The bar must still be formatting the editor it was pointed at.
        bar.ToggleStyleCommand.Execute(TextStyle.Italic);

        Assert.True(second.View!.SelectionMarks.Has(TextStyle.Italic));
        Assert.False(first.SelectionMarks.Has(TextStyle.Italic));
    }

    [AvaloniaFact]
    public void ClearingTheEditorLeavesTheBarInertRatherThanStale()
    {
        var (bar, view) = Bar("hello");

        SelectAll(view);
        bar.ToggleStyleCommand.Execute(TextStyle.Bold);

        Assert.True(bar.IsBold);

        bar.Editor = null;

        Assert.False(bar.IsBold);
        Assert.False(bar.CanUndo);
    }

    // ---- helpers ---------------------------------------------------------------------------

    private static (RichTextToolbar Bar, RichTextView View) Bar(string text)
    {
        var editor = Show(new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(text)),
        });

        return (new RichTextToolbar { Editor = editor }, editor.View!);
    }

    private static RichTextEditor Show(RichTextEditor editor)
    {
        var window = new Window { Width = 400, Height = 200, Content = editor };

        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));

        return editor;
    }

    /// <summary>Forces the editor to build a fresh view, as a restyled application would.</summary>
    private static void Retemplate(RichTextEditor editor)
    {
        editor.Template = new FuncControlTemplate<RichTextEditor>((_, scope) =>
        {
            var view = new RichTextView { Name = RichTextEditor.ViewPart };

            view.RegisterInNameScope(scope);

            return new ScrollViewer { Content = view };
        });

        editor.ApplyTemplate();
    }

    private static void SelectAll(RichTextView view)
    {
        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[^1].End, extend: true);
    }
}
