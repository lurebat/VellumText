using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Tests for typing, Backspace and Delete on <see cref="RichTextView"/>.
/// </summary>
public class RichTextViewEditingTests
{
    [AvaloniaFact]
    public void TypingInsertsAtTheCaretAndLeavesItAfterWhatWasTyped()
    {
        var view = Focused("helo");

        Caret(view, 3);
        Text(view, "l");

        Assert.Equal("hello", Content(view));
        Assert.Equal(view.BlockStart + 4, view.State.Selection.Head);
        Assert.True(view.State.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void TypingReplacesTheSelection()
    {
        var view = Focused("hello world");

        Select(view, 6, 11);
        Text(view, "there");

        Assert.Equal("hello there", Content(view));
        Assert.Equal(view.BlockStart + 11, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void TypingRelaysOutTheParagraph()
    {
        // The view has to notice the document changed, not just the selection. If it kept the
        // old layout the text would be right in the model and wrong on screen.
        var view = Focused("hello");
        var before = view.Block!.ContentSize;

        Caret(view, 5);
        Text(view, "!");

        Assert.Equal(before + 1, view.Block!.ContentSize);
        Assert.Equal("hello!", view.Block.Paragraph.Content.Text);
    }

    [AvaloniaFact]
    public void TypedTextInheritsTheFormattingToItsLeft()
    {
        var view = FocusedContent(
            InlineContent.FromText("boldplain")
                .ApplyMarks(0, 4, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold));

        Caret(view, 4);
        Text(view, "X");

        Assert.Equal("boldXplain", Content(view));
        Assert.True(MarkAt(view, 4).Has(TextStyle.Bold));
    }

    [AvaloniaFact]
    public void AStoredMarkBeatsWhatIsToTheLeft()
    {
        var view = Focused("plain");

        Caret(view, 5);

        view.State = view.State.Apply(
            view.State
                .Transaction()
                .SetStoredMarks(new MarkSet { Style = TextStyle.Italic })
                .SetSelection(view.State.Selection));

        Text(view, "X");

        Assert.True(MarkAt(view, 5).Has(TextStyle.Italic));
    }

    [AvaloniaFact]
    public void TypingClearsTheStoredMark()
    {
        var view = Focused("plain");

        Caret(view, 5);

        view.State = view.State.Apply(
            view.State
                .Transaction()
                .SetStoredMarks(new MarkSet { Style = TextStyle.Italic })
                .SetSelection(view.State.Selection));

        Text(view, "X");

        Assert.Null(view.State.StoredMarks);
    }

    [AvaloniaFact]
    public void BackspaceRemovesTheCharacterBeforeTheCaret()
    {
        var view = Focused("hello");

        Caret(view, 5);

        Assert.True(view.Backspace());
        Assert.Equal("hell", Content(view));
        Assert.Equal(view.BlockStart + 4, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void DeleteRemovesTheCharacterAfterTheCaret()
    {
        var view = Focused("hello");

        Caret(view, 0);

        Assert.True(view.Delete());
        Assert.Equal("ello", Content(view));
        Assert.Equal(view.BlockStart, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void BackspaceAndDeleteRemoveTheSelectionWhenThereIsOne()
    {
        var view = Focused("hello world");

        Select(view, 5, 11);

        Assert.True(view.Backspace());
        Assert.Equal("hello", Content(view));

        view = Focused("hello world");

        Select(view, 5, 11);

        Assert.True(view.Delete());
        Assert.Equal("hello", Content(view));
    }

    [AvaloniaFact]
    public void BackspaceAtTheStartAndDeleteAtTheEndDoNothing()
    {
        // There is one block, so there is nothing to join to. Applying an empty transaction
        // would still replace the state and reset the caret, which is why this returns false
        // rather than quietly succeeding.
        var view = Focused("hello");

        Caret(view, 0);

        Assert.False(view.Backspace());
        Assert.Equal("hello", Content(view));

        Caret(view, 5);

        Assert.False(view.Delete());
        Assert.Equal("hello", Content(view));
    }

    [AvaloniaFact]
    public void DeleteStepsOverAWholeClusterAndBackspacePeelsOneMarkOffIt()
    {
        // Increment 0 measured this difference and the behaviour was chosen deliberately: an
        // arrow key and Delete move by cluster, Backspace peels a combining mark.
        const string Text = "e\u0301x";

        var view = Focused(Text);

        Caret(view, 2);

        Assert.True(view.Backspace());
        Assert.Equal("ex", Content(view));

        view = Focused(Text);

        Caret(view, 0);

        Assert.True(view.Delete());
        Assert.Equal("x", Content(view));
    }

    [AvaloniaFact]
    public void DeletingAnEmojiRemovesTheWholeSequence()
    {
        var view = Focused("a\U0001F468\u200D\U0001F469\u200D\U0001F467b");

        Caret(view, 1);

        Assert.True(view.Delete());
        Assert.Equal("ab", Content(view));
    }

    [AvaloniaFact]
    public void TextInputArrivingAsAnEventIsInserted()
    {
        var view = Focused("hi");

        Caret(view, 2);
        view.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = "!",
            Source = view,
        });

        Assert.Equal("hi!", Content(view));
    }

    [AvaloniaFact]
    public void ControlCharactersAreNotInsertedAsText()
    {
        // Enter, Escape and Backspace reach OnTextInput as text on some backends. None of them
        // belong in the document, and Enter is a paragraph split, which is structure.
        var view = Focused("hi");

        Caret(view, 2);

        foreach (var control in new[] { "\r", "\n", "\u001b", "\b", "\t" })
        {
            var args = new TextInputEventArgs
            {
                RoutedEvent = InputElement.TextInputEvent,
                Text = control,
                Source = view,
            };

            view.RaiseEvent(args);

            Assert.False(args.Handled);
        }

        Assert.Equal("hi", Content(view));
    }

    [AvaloniaFact]
    public void TypingNothingChangesNothing()
    {
        var view = Focused("hi");

        Assert.False(view.InsertText(string.Empty));
        Assert.Equal("hi", Content(view));
    }

    [AvaloniaFact]
    public void BackspaceAndDeleteReachTheDocumentThroughTheirKeys()
    {
        var view = Focused("hello");

        Caret(view, 5);
        Key(view, global::Avalonia.Input.Key.Back);

        Assert.Equal("hell", Content(view));

        Caret(view, 0);
        Key(view, global::Avalonia.Input.Key.Delete);

        Assert.Equal("ell", Content(view));
    }

    [AvaloniaFact]
    public void ADeleteKeyThatDeletesNothingIsLeftForSomebodyElse()
    {
        var view = Focused("hello");

        Caret(view, 0);

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = global::Avalonia.Input.Key.Back,
            Source = view,
        };

        view.RaiseEvent(args);

        Assert.False(args.Handled);
    }

    [AvaloniaFact]
    public void AnEditForgetsTheVerticalGoalColumn()
    {
        var view = Focused("hello");

        Key(view, global::Avalonia.Input.Key.Down);

        Assert.NotNull(view.GoalX);

        Text(view, "x");

        Assert.Null(view.GoalX);
    }

    [AvaloniaFact]
    public void DeletingForgetsTheVerticalGoalColumnToo()
    {
        // A goal column kept across an edit belongs to a layout that no longer exists, so the
        // next Up or Down aims at a column the text has since moved out from under.
        var view = Focused("hello");

        Caret(view, 5);
        Key(view, global::Avalonia.Input.Key.Down);

        Assert.NotNull(view.GoalX);
        Assert.True(view.Backspace());
        Assert.Null(view.GoalX);

        Key(view, global::Avalonia.Input.Key.Down);

        Assert.NotNull(view.GoalX);
        Assert.True(view.Delete() || view.Backspace());
        Assert.Null(view.GoalX);
    }

    [AvaloniaFact]
    public void AnEditMakesTheCaretSolid()
    {
        var view = Focused("hello");

        Caret(view, 5);

        typeof(RichTextView)
            .GetField(
                "_caretVisible",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(view, false);

        Text(view, "x");

        Assert.True(view.IsCaretVisible);
    }

    [AvaloniaFact]
    public void ATransactionThatChangedNothingIsNotInstalled()
    {
        var view = Focused("hello");
        var before = view.State;

        Assert.False(view.Apply(view.State.Transaction()));
        Assert.Same(before, view.State);
    }

    private static string Bold(string text) => text;

    private static string Content(RichTextView view) =>
        ((ParagraphNode)view.State.Doc.Blocks[0]).Content.Text;

    private static MarkSet MarkAt(RichTextView view, int offset) =>
        ((ParagraphNode)view.State.Doc.Blocks[0]).Content.MarkAt(offset);

    private static RichTextView Focused(string text) =>
        FocusedDocument(DocumentNode.FromParagraphs(text));

    private static RichTextView FocusedContent(InlineContent content) =>
        FocusedDocument(new DocumentNode([new ParagraphNode(content)]));

    private static RichTextView FocusedDocument(DocumentNode document)
    {
        var window = new Window { Width = 400, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(document) };

        window.Content = view;
        window.Show();
        window.Measure(new Size(400, 200));
        window.Arrange(new Rect(0, 0, 400, 200));
        view.Focus();

        return view;
    }

    private static void Caret(RichTextView view, int offset) =>
        view.MoveTo(view.BlockStart + offset, extend: false);

    private static void Select(RichTextView view, int from, int to)
    {
        view.MoveTo(view.BlockStart + from, extend: false);
        view.MoveTo(view.BlockStart + to, extend: true);
    }

    private static void Text(RichTextView view, string text) => view.InsertText(text);

    private static void Key(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });
}
