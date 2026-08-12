using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Tests for undo and redo on <see cref="RichTextView"/>.
/// </summary>
public class RichTextViewHistoryTests
{
    [AvaloniaFact]
    public void ThereIsNothingToUndoOrRedoToBeginWith()
    {
        var view = Focused("hello");

        Assert.False(view.CanUndo);
        Assert.False(view.CanRedo);
        Assert.False(view.Undo());
        Assert.False(view.Redo());
        Assert.Equal("hello", Content(view));
    }

    [AvaloniaFact]
    public void TypingIsUndoneAndRedone()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Assert.Equal("hello!", Content(view));
        Assert.True(view.CanUndo);
        Assert.True(view.Undo());
        Assert.Equal("hello", Content(view));
        Assert.True(view.CanRedo);
        Assert.True(view.Redo());
        Assert.Equal("hello!", Content(view));
    }

    [AvaloniaFact]
    public void UndoRestoresTheSelectionTheEditStartedFrom()
    {
        var view = Ungrouped("hello world");

        Select(view, 6, 11);
        view.InsertText("there");

        Assert.Equal(view.BlockStart + 11, view.State.Selection.Head);
        Assert.True(view.Undo());
        Assert.Equal(view.BlockStart + 6, view.State.Selection.From);
        Assert.Equal(view.BlockStart + 11, view.State.Selection.To);
    }

    [AvaloniaFact]
    public void DeletingIsUndoneAndRedone()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);

        Assert.True(view.Backspace());
        Assert.Equal("hell", Content(view));
        Assert.True(view.Undo());
        Assert.Equal("hello", Content(view));
        Assert.True(view.Redo());
        Assert.Equal("hell", Content(view));
    }

    [AvaloniaFact]
    public void AWholeRunOfTypingUndoesAtOnce()
    {
        // The default policy groups adjacent typing inside its window, so Ctrl+Z takes back a
        // word rather than a letter. This is the behaviour, not an accident of timing.
        var view = Focused("");

        Caret(view, 0);

        foreach (var c in "hello")
        {
            view.InsertText(c.ToString());
        }

        Assert.Equal("hello", Content(view));
        Assert.True(view.Undo());
        Assert.Equal(string.Empty, Content(view));
        Assert.False(view.CanUndo);
    }

    [AvaloniaFact]
    public void TypingAndDeletingDoNotGroupTogether()
    {
        var view = Focused("");

        Caret(view, 0);
        view.InsertText("ab");

        Assert.True(view.Backspace());
        Assert.Equal("a", Content(view));
        Assert.True(view.Undo());
        Assert.Equal("ab", Content(view));
        Assert.True(view.Undo());
        Assert.Equal(string.Empty, Content(view));
    }

    [AvaloniaFact]
    public void AnEditAfterAnUndoDiscardsTheRedoStack()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Assert.True(view.Undo());
        Assert.True(view.CanRedo);

        view.InsertText("?");

        Assert.False(view.CanRedo);
        Assert.Equal("hello?", Content(view));
    }

    [AvaloniaFact]
    public void MovingTheCaretIsNotAnEdit()
    {
        var view = Focused("hello");

        Caret(view, 3);

        Assert.False(view.CanUndo);
    }

    [AvaloniaFact]
    public void ATransactionThatChangedNothingIsNotRecorded()
    {
        var view = Focused("hello");

        Assert.False(view.Apply(view.State.Transaction()));
        Assert.False(view.CanUndo);
    }

    [AvaloniaFact]
    public void UndoDoesNotUndoItself()
    {
        // Replaying an event must not push a fresh event of its own, or Ctrl+Z alternates
        // between two documents forever.
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Assert.True(view.Undo());
        Assert.Equal(0, view.History.UndoDepth);
        Assert.Equal(1, view.History.RedoDepth);
    }

    [AvaloniaFact]
    public void UndoAndRedoReachTheDocumentThroughTheirKeys()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Key(view, global::Avalonia.Input.Key.Z, KeyModifiers.Control);

        Assert.Equal("hello", Content(view));

        Key(view, global::Avalonia.Input.Key.Y, KeyModifiers.Control);

        Assert.Equal("hello!", Content(view));

        Key(view, global::Avalonia.Input.Key.Z, KeyModifiers.Control);

        Assert.Equal("hello", Content(view));

        Key(view, global::Avalonia.Input.Key.Z, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal("hello!", Content(view));
    }

    [AvaloniaFact]
    public void AnUndoKeyWithNothingToUndoIsLeftForSomebodyElse()
    {
        var view = Focused("hello");

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = global::Avalonia.Input.Key.Z,
            KeyModifiers = KeyModifiers.Control,
            Source = view,
        };

        view.RaiseEvent(args);

        Assert.False(args.Handled);
    }

    [AvaloniaFact]
    public void AReplayForgetsTheVerticalGoalColumn()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Key(view, global::Avalonia.Input.Key.Down);

        Assert.NotNull(view.GoalX);
        Assert.True(view.Undo());
        Assert.Null(view.GoalX);
    }

    [AvaloniaFact]
    public void AReplayRelaysOutTheParagraph()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Assert.Equal("hello!", view.Block!.Paragraph.Content.Text);
        Assert.True(view.Undo());
        Assert.Equal("hello", view.Block!.Paragraph.Content.Text);
    }

    [AvaloniaFact]
    public void SettingStateDirectlyIsNotUndoable()
    {
        // A host swapping the document is not an edit. Recording it would let Ctrl+Z bring back
        // a document the host had already discarded.
        var view = Focused("hello");

        view.State = EditorState.Create(DocumentNode.FromParagraphs("something else"));

        Assert.False(view.CanUndo);
        Assert.Equal("something else", Content(view));
    }

    [AvaloniaFact]
    public void ReplacingTheHistoryDiscardsBothStacks()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        Assert.True(view.CanUndo);

        view.History = History.Empty;

        Assert.False(view.CanUndo);
        Assert.False(view.CanRedo);
        Assert.Equal("hello!", Content(view));
    }

    [AvaloniaFact]
    public void TheHistoryCannotBeSetToNull()
    {
        var view = Focused("hello");

        Assert.Throws<ArgumentNullException>(() => view.History = null!);
    }

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

    private static void Caret(RichTextView view, int offset) =>
        view.MoveTo(view.BlockStart + offset, extend: false);

    private static void Select(RichTextView view, int from, int to)
    {
        view.MoveTo(view.BlockStart + from, extend: false);
        view.MoveTo(view.BlockStart + to, extend: true);
    }

    private static void Key(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });
}
