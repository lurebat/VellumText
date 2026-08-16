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

    [AvaloniaFact]
    public void RecordingAnEditAnnouncesThatThereIsSomethingToUndo()
    {
        // A toolbar binds its enabled state to CanUndo. Without a notification the button is
        // wrong from the first edit until something unrelated happens to redraw it.
        var view = Ungrouped("hello");
        var raised = new List<string?>();

        view.PropertyChanged += (_, e) => raised.Add(e.Property.Name);

        Caret(view, 5);
        view.InsertText("!");

        Assert.Contains(nameof(RichTextView.CanUndo), raised);
    }

    [AvaloniaFact]
    public void UndoingAnnouncesBothFlags()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        var raised = new List<string?>();

        view.PropertyChanged += (_, e) => raised.Add(e.Property.Name);

        Assert.True(view.Undo());

        // Undoing the only edit empties the undo stack and fills the redo stack, so both change.
        Assert.Contains(nameof(RichTextView.CanUndo), raised);
        Assert.Contains(nameof(RichTextView.CanRedo), raised);
    }

    [AvaloniaFact]
    public void TheAnnouncedValueIsTheOneTheFlagThenReports()
    {
        var view = Ungrouped("hello");
        var seen = new List<bool>();

        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.CanUndoProperty)
            {
                seen.Add((bool)e.NewValue!);
            }
        };

        Caret(view, 5);
        view.InsertText("!");

        Assert.Equal([true], seen);
        Assert.True(view.CanUndo);
    }

    [AvaloniaFact]
    public void AListenerWokenByTheFlagSeesTheEditedDocument()
    {
        // The flag is announced after the state is installed, so the two never disagree.
        var view = Ungrouped("hello");
        string? textWhenTold = null;

        view.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.CanUndoProperty)
            {
                textWhenTold = Content(view);
            }
        };

        Caret(view, 5);
        view.InsertText("!");

        Assert.Equal("hello!", textWhenTold);
    }

    [AvaloniaFact]
    public void ASecondEditDoesNotReannounceAFlagThatDidNotChange()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        var raised = new List<string?>();

        view.PropertyChanged += (_, e) => raised.Add(e.Property.Name);

        view.InsertText("?");

        // CanUndo was already true; saying so again would churn every bound button.
        Assert.DoesNotContain(nameof(RichTextView.CanUndo), raised);
    }

    [AvaloniaFact]
    public void ReplacingTheHistoryAnnouncesTheFlagsItChanged()
    {
        var view = Ungrouped("hello");

        Caret(view, 5);
        view.InsertText("!");

        var raised = new List<string?>();

        view.PropertyChanged += (_, e) => raised.Add(e.Property.Name);

        view.History = History.Empty;

        Assert.Contains(nameof(RichTextView.CanUndo), raised);
        Assert.False(view.CanUndo);
    }
    // ---- loading a different document ---------------------------------------------------------

    [AvaloniaFact]
    public void LoadingADifferentDocumentDiscardsTheHistory()
    {
        // Measured before this existed: typing into a four-character clip, loading a
        // one-character clip and pressing Ctrl+Z threw
        // "Range [3, 4) is not inside the document". A clipboard history or a file list reaches
        // that by arrowing down and pressing a key.
        var view = Focused("aaaa");

        Caret(view, 3);
        view.InsertText("X");

        Assert.True(view.CanUndo);

        view.State = EditorState.Create(DocumentNode.FromParagraphs("b"));

        Assert.False(view.CanUndo);
        Assert.False(view.Undo());
        Assert.Equal("b", Content(view));
    }

    [AvaloniaFact]
    public void LoadingADifferentDocumentDiscardsTheRedoStackToo()
    {
        var view = Ungrouped("aaaa");

        Caret(view, 3);
        view.InsertText("X");
        view.Undo();

        Assert.True(view.CanRedo);

        view.State = EditorState.Create(DocumentNode.FromParagraphs("b"));

        Assert.False(view.CanRedo);
        Assert.False(view.Redo());
    }

    [AvaloniaFact]
    public void SettingOnlyTheSelectionKeepsTheHistory()
    {
        // The other half of the rule. Assigning a state carrying the same document is how
        // selection is set from outside, and it must not cost the user their undo stack.
        var view = Focused("hello");

        Caret(view, 3);
        view.InsertText("X");

        var doc = view.State.Doc;

        view.State = EditorState.Create(doc, TextSelection.Create(doc, 1, 3));

        Assert.True(view.CanUndo);
        Assert.True(view.Undo());
        Assert.Equal("hello", Content(view));
    }

    [AvaloniaFact]
    public void SelectAllKeepsTheHistory()
    {
        // SelectAll assigns State directly, so it goes through the same path as a host doing it.
        var view = Focused("hello");

        Caret(view, 3);
        view.InsertText("X");
        view.SelectAll();

        Assert.True(view.CanUndo);
    }

    [AvaloniaFact]
    public void EditingStillRecordsHistoryAfterTheChange()
    {
        // Apply installs a state with a different document every time, so a rule that keyed only
        // on the document changing would delete the history it had just recorded.
        var view = Ungrouped("abc");

        Caret(view, 1);
        view.InsertText("X");

        Assert.True(view.CanUndo);

        view.InsertText("Y");

        Assert.True(view.CanUndo);
        Assert.True(view.Undo());
        Assert.True(view.Undo());
        Assert.Equal("abc", Content(view));
    }

    [AvaloniaFact]
    public void UndoAndRedoSurviveTheirOwnStateChanges()
    {
        // Replay installs a state too, and undoing must not discard the redo it just created.
        var view = Ungrouped("abc");

        Caret(view, 1);
        view.InsertText("X");
        view.Undo();

        Assert.True(view.CanRedo);
        Assert.True(view.Redo());
        Assert.Equal("aXbc", Content(view));
    }

    [AvaloniaFact]
    public void TheUndoLimitSurvivesLoadingADifferentDocument()
    {
        // The limit is the host's configuration rather than the document's history.
        var view = Focused("aaaa");

        view.UndoLimit = 5;
        Caret(view, 3);
        view.InsertText("X");

        view.State = EditorState.Create(DocumentNode.FromParagraphs("b"));

        Assert.Equal(5, view.UndoLimit);
        Assert.Equal(5, view.History.Policy.Limit);
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
