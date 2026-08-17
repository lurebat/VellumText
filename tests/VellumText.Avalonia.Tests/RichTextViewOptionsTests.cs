using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Tests for <c>IsReadOnly</c>, <c>AcceptsReturn</c>, <c>AcceptsTab</c> and <c>UndoLimit</c>.
/// </summary>
/// <remarks>
/// The read-only tests are deliberately a survey rather than a sample. The whole claim being made
/// is that one check covers every route into the document, and a test that only tried typing would
/// pass just as happily against a check bolted onto <c>InsertText</c> alone.
/// </remarks>
public class RichTextViewOptionsTests
{
    [AvaloniaFact]
    public void TheDefaultsAreThoseOfADocumentEditorNotATextBox()
    {
        var view = new RichTextView();

        Assert.False(view.IsReadOnly);
        Assert.True(view.AcceptsReturn);
        Assert.True(view.AcceptsTab);
        Assert.Equal(HistoryPolicy.Unlimited, view.UndoLimit);
    }

    // ---- IsReadOnly -------------------------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("type")]
    [InlineData("backspace")]
    [InlineData("delete")]
    [InlineData("split")]
    [InlineData("bold")]
    [InlineData("heading")]
    [InlineData("list")]
    [InlineData("indent")]
    [InlineData("align")]
    [InlineData("link")]
    [InlineData("paste")]
    [InlineData("outdent")]
    public void NoRouteIntoTheDocumentGetsPastReadOnly(string route)
    {
        var view = Focused("hello world");

        Select(view, 0, 5);

        var before = view.State.Doc;

        view.IsReadOnly = true;

        Assert.False(Run(view, route));
        Assert.Equal(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void EveryRouteInTheSurveyReallyDoesEditWhenReadOnlyIsOff()
    {
        // Without this the theory above would pass against a control where the commands simply
        // did nothing, which is the failure mode a read-only test is least able to notice.
        foreach (var route in new[]
        {
            "type", "backspace", "delete", "split", "bold", "heading",
            "list", "indent", "align", "link", "paste", "outdent",
        })
        {
            var view = Focused("hello world");

            Select(view, 0, 5);

            var before = view.State.Doc;

            Assert.True(Run(view, route), $"'{route}' changed nothing even when writable");
            Assert.NotEqual(before, view.State.Doc);
        }
    }

    [AvaloniaFact]
    public void ReadOnlyRefusesTheKeyboardToo()
    {
        var view = Focused("hello");

        Caret(view, 5);
        view.IsReadOnly = true;

        Press(view, Key.A);
        Press(view, Key.Back);
        Press(view, Key.Delete);
        Press(view, Key.Enter);
        Press(view, Key.B, KeyModifiers.Control);

        Assert.Equal("hello", Content(view));
    }

    [AvaloniaFact]
    public void ReadOnlyStillLetsTheCaretAndTheSelectionMove()
    {
        var view = Focused("hello world");

        view.IsReadOnly = true;

        Caret(view, 0);
        Press(view, Key.Right);

        Assert.Equal(view.BlockStart + 1, view.State.Selection.Head);

        view.SelectAll();

        Assert.False(view.State.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void ReadOnlyRefusesUndoAndSaysSo()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Assert.True(view.CanUndo);

        view.IsReadOnly = true;

        Assert.False(view.CanUndo);
        Assert.False(view.Undo());
        Assert.Equal("hello!", Content(view));

        // And it comes back, rather than the stack having been thrown away.
        view.IsReadOnly = false;

        Assert.True(view.CanUndo);
        Assert.True(view.Undo());
        Assert.Equal("hello", Content(view));
    }

    [AvaloniaFact]
    public void ReadOnlyRefusesRedoAndSaysSo()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");
        view.Undo();

        Assert.True(view.CanRedo);

        view.IsReadOnly = true;

        Assert.False(view.CanRedo);
        Assert.False(view.Redo());
        Assert.Equal("hello", Content(view));

        view.IsReadOnly = false;

        Assert.True(view.CanRedo);
        Assert.True(view.Redo());
        Assert.Equal("hello!", Content(view));
    }

    [AvaloniaFact]
    public void TurningReadOnlyOnTellsABoundButtonAboutIt()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        var raised = 0;

        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.CanUndoProperty)
            {
                raised++;
            }
        };

        view.IsReadOnly = true;

        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void AHostCanStillReplaceTheDocumentWhileReadOnly()
    {
        var view = Focused("hello");

        view.IsReadOnly = true;
        view.State = EditorState.Create(DocumentNode.FromParagraphs("replaced"));

        Assert.Equal("replaced", Content(view));
    }

    // ---- AcceptsReturn ----------------------------------------------------------------------

    [AvaloniaFact]
    public void EnterSplitsTheParagraphByDefault()
    {
        var view = Focused("hello");

        Caret(view, 2);
        Press(view, Key.Enter);

        Assert.Equal(2, view.State.Doc.Blocks.Length);
    }

    [AvaloniaFact]
    public void EnterIsLeftUnhandledWhenReturnIsNotAccepted()
    {
        var view = Focused("hello");

        Caret(view, 2);
        view.AcceptsReturn = false;

        var e = KeyArgs(view, Key.Enter);

        view.RaiseEvent(e);

        Assert.Single(view.State.Doc.Blocks);

        // Unhandled, so a dialog's default button still gets the key.
        Assert.False(e.Handled);
    }

    [AvaloniaFact]
    public void PastingSeveralParagraphsStillMakesSeveralWhenReturnIsNotAccepted()
    {
        // AcceptsReturn is about the Enter key, not about what a document may contain. Flattening
        // pasted content would quietly destroy structure the host had asked to insert.
        var view = Focused("hello");

        Caret(view, 5);
        view.AcceptsReturn = false;
        view.PasteDocument(new DocumentNode(
        [
            ParagraphNode.FromText("one"),
            ParagraphNode.FromText("two"),
        ]));

        Assert.True(view.State.Doc.Blocks.Length > 1);
    }

    // ---- AcceptsTab -------------------------------------------------------------------------

    [AvaloniaFact]
    public void TabIndentsByDefault()
    {
        var (view, button) = Neighbours();

        Caret(view, 2);
        Press(view, Key.Tab);

        Assert.NotEqual(0, ((ParagraphNode)view.State.Doc.Blocks[0]).IndentLevel);
        Assert.False(button.IsFocused);
    }

    [AvaloniaFact]
    public void TabMovesFocusOnWhenTabIsNotAccepted()
    {
        // The point of the property, stated as what a user sees: the key leaves the editor. It is
        // asserted through focus and not through e.Handled, because the event bubbles to the
        // window and focus navigation is what marks it handled - after the view has declined it.
        var (view, button) = Neighbours();

        Caret(view, 2);
        view.AcceptsTab = false;

        Press(view, Key.Tab);

        Assert.Equal(0, ((ParagraphNode)view.State.Doc.Blocks[0]).IndentLevel);
        Assert.True(button.IsFocused);
    }

    [AvaloniaFact]
    public void TabMovesFocusOnFromInsideAListToo()
    {
        // The one place the default behaviour argues hardest for consuming the key. A host that
        // turned the property off still gets the key back, or "off" would mean "off except here".
        var (view, button) = Neighbours();

        Caret(view, 2);
        view.ToggleList(ListKind.Unordered);
        view.AcceptsTab = false;

        Press(view, Key.Tab);

        Assert.True(button.IsFocused);
    }

    // ---- UndoLimit --------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheUndoStackStopsGrowingAtTheLimit()
    {
        var view = Ungrouped("hello");

        view.UndoLimit = 3;

        Caret(view, 5);

        for (var i = 0; i < 8; i++)
        {
            view.InsertText("x");
        }

        Assert.Equal(3, view.History.UndoDepth);

        while (view.Undo())
        {
            // Down to the bottom of what survived.
        }

        Assert.Equal("helloxxxxx", Content(view));
    }

    [AvaloniaFact]
    public void LoweringTheLimitTrimsTheStackAtOnce()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);

        for (var i = 0; i < 6; i++)
        {
            view.InsertText("x");
        }

        Assert.Equal(6, view.History.UndoDepth);

        view.UndoLimit = 2;

        Assert.Equal(2, view.History.UndoDepth);
    }

    [AvaloniaFact]
    public void ALimitOfZeroTurnsUndoOff()
    {
        var view = Ungrouped("hello");

        view.UndoLimit = 0;

        Caret(view, 5);
        view.InsertText("!");

        Assert.False(view.CanUndo);
        Assert.Equal("hello!", Content(view));
    }

    [AvaloniaFact]
    public void ANegativeLimitMeansUnlimitedRatherThanThrowing()
    {
        var view = new RichTextView { UndoLimit = -99 };

        Assert.Equal(HistoryPolicy.Unlimited, view.UndoLimit);
    }

    [AvaloniaFact]
    public void AssigningAHistoryAdoptsTheLimitItsPolicyCarries()
    {
        // Otherwise UndoLimit would report a bound the history was not keeping to.
        var view = Focused("hello");

        view.UndoLimit = 10;
        view.History = History.With(new HistoryPolicy(TimeSpan.Zero, Enabled: false, Limit: 4));

        Assert.Equal(4, view.UndoLimit);
        Assert.Equal(4, view.History.Policy.Limit);

        Caret(view, 5);

        for (var i = 0; i < 9; i++)
        {
            view.InsertText("x");
        }

        Assert.Equal(4, view.History.UndoDepth);
    }

    [AvaloniaFact]
    public void AssigningAHistoryKeepsTheVeryObjectItWasGiven()
    {
        var view = Focused("hello");
        var history = History.With(new HistoryPolicy(TimeSpan.Zero, Enabled: false, Limit: 4));

        view.History = history;

        Assert.Same(history, view.History);
    }

    // ---- The shell forwards all four ---------------------------------------------------------

    [AvaloniaTheory]
    [InlineData("readonly")]
    [InlineData("return")]
    [InlineData("tab")]
    [InlineData("undolimit")]
    public void TheEditorPushesEachPropertyIntoItsView(string which)
    {
        var editor = TemplatedEditor();
        var view = editor.View!;

        switch (which)
        {
            case "readonly":
                editor.IsReadOnly = true;
                Assert.True(view.IsReadOnly);
                break;

            case "return":
                editor.AcceptsReturn = false;
                Assert.False(view.AcceptsReturn);
                break;

            case "tab":
                editor.AcceptsTab = false;
                Assert.False(view.AcceptsTab);
                break;

            case "undolimit":
                editor.UndoLimit = 7;
                Assert.Equal(7, view.UndoLimit);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(which));
        }
    }

    [AvaloniaFact]
    public void APropertySetBeforeTemplatingStillReachesTheView()
    {
        var editor = new RichTextEditor { IsReadOnly = true, AcceptsTab = false };
        var window = Shown(editor);

        Assert.True(editor.View!.IsReadOnly);
        Assert.False(editor.View!.AcceptsTab);

        GC.KeepAlive(window);
    }

    [AvaloniaFact]
    public void AnUntouchedEditorDoesNotOverwriteAHistoryPolicySetThroughHistory()
    {
        // The shell must not push its own default UndoLimit over a limit the host configured by
        // handing the control a History, which is the older of the two ways to say it.
        var editor = TemplatedEditor();

        editor.History = History.With(new HistoryPolicy(TimeSpan.Zero, Enabled: false, Limit: 5));

        Assert.Equal(5, editor.View!.History.Policy.Limit);
    }

    [AvaloniaFact]
    public void AHistoryAssignedToTheShellWinsOverAnEarlierUndoLimit()
    {
        var editor = TemplatedEditor();

        editor.UndoLimit = 10;
        editor.History = History.With(new HistoryPolicy(TimeSpan.Zero, Enabled: false, Limit: 4));

        Assert.Equal(4, editor.UndoLimit);

        // And an unrelated property change must not push the stale 10 back over it.
        editor.IsReadOnly = true;
        editor.IsReadOnly = false;

        Assert.Equal(4, editor.View!.History.Policy.Limit);
    }

    [AvaloniaFact]
    public void AHistorySetBeforeTemplatingAlsoWinsOverAnEarlierUndoLimit()
    {
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("hello")),
            UndoLimit = 10,
        };

        editor.History = History.With(new HistoryPolicy(TimeSpan.Zero, Enabled: false, Limit: 4));

        var window = Shown(editor);

        Assert.Equal(4, editor.View!.History.Policy.Limit);

        GC.KeepAlive(window);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static bool Run(RichTextView view, string route) => route switch
    {
        "type" => view.InsertText("z"),
        "backspace" => view.Backspace(),
        "delete" => view.Delete(),
        "split" => view.Split(),
        "bold" => view.ToggleStyle(TextStyle.Bold),
        "heading" => view.SetParagraphKind(ParagraphKind.Heading1),
        "list" => view.ToggleList(ListKind.Unordered),
        "indent" => view.Indent(),
        "align" => view.SetAlign(TextAlign.Center),
        "link" => view.SetLink(new LinkMark("https://example.com")),
        "paste" => view.PasteText("pasted"),
        "outdent" => view.SetIndentLevel(3),
        _ => throw new ArgumentOutOfRangeException(nameof(route)),
    };

    private static string Content(RichTextView view) =>
        ((ParagraphNode)view.State.Doc.Blocks[0]).Content.Text;

    private static RichTextView Focused(string text)
    {
        var window = new Window { Width = 400, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(text)) };

        window.Content = view;
        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));
        view.Focus();

        return view;
    }

    /// <summary>A view whose history never groups, so each edit is its own undo step.</summary>
    private static RichTextView Ungrouped(string text)
    {
        var view = Focused(text);

        view.History = History.With(HistoryPolicy.Never);

        return view;
    }

    /// <summary>A focused view with a focusable neighbour, so Tab has somewhere to go.</summary>
    private static (RichTextView View, Button Button) Neighbours()
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var button = new Button { Content = "next" };
        var stack = new StackPanel();

        stack.Children.Add(view);
        stack.Children.Add(button);

        var window = new Window { Width = 400, Height = 200, Content = stack };

        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));
        view.Focus();

        return (view, button);
    }

    private static RichTextEditor TemplatedEditor()
    {
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("hello")),
        };

        Shown(editor);

        return editor;
    }

    private static Window Shown(Control content)
    {
        var window = new Window { Width = 400, Height = 200, Content = content };

        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));

        return window;
    }

    private static void Caret(RichTextView view, int offset) =>
        view.MoveTo(view.BlockStart + offset, extend: false);

    private static void Select(RichTextView view, int from, int to)
    {
        view.MoveTo(view.BlockStart + from, extend: false);
        view.MoveTo(view.BlockStart + to, extend: true);
    }

    private static KeyEventArgs KeyArgs(RichTextView view, Key key,
        KeyModifiers modifiers = KeyModifiers.None) => new()
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        };

    private static void Press(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(KeyArgs(view, key, modifiers));
}
