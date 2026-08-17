using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Enter, and the Backspace and Delete presses that land on a block boundary.
/// </summary>
public class RichTextViewSplitTests
{
    [AvaloniaFact]
    public void EnterInTheMiddleOfAParagraphMakesTwo()
    {
        var view = Shown("hello world");

        view.MoveTo(view.Slots[0].Start + 5, extend: false);
        Press(view, Key.Enter);

        Assert.Equal(2, view.Blocks.Count);
        Assert.Equal("hello", Text(view, 0));
        Assert.Equal(" world", Text(view, 1));
    }

    [AvaloniaFact]
    public void TheCaretLandsAtTheStartOfTheSecondHalf()
    {
        var view = Shown("hello world");

        view.MoveTo(view.Slots[0].Start + 5, extend: false);
        Press(view, Key.Enter);

        Assert.Equal(1, view.CaretBlockIndex);
        Assert.Equal(view.Slots[1].Start, view.State.Selection.Head);
        Assert.True(view.State.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void EnterAtTheEndOfAParagraphAddsAnEmptyOne()
    {
        var view = Shown("hello");

        view.MoveTo(view.Slots[0].End, extend: false);
        Press(view, Key.Enter);

        Assert.Equal(2, view.Blocks.Count);
        Assert.Equal("hello", Text(view, 0));
        Assert.Equal(string.Empty, Text(view, 1));
        Assert.Equal(1, view.CaretBlockIndex);
    }

    [AvaloniaFact]
    public void EnterAtTheStartOfAParagraphPushesItDown()
    {
        var view = Shown("hello");

        view.MoveTo(view.Slots[0].Start, extend: false);
        Press(view, Key.Enter);

        Assert.Equal(2, view.Blocks.Count);
        Assert.Equal(string.Empty, Text(view, 0));
        Assert.Equal("hello", Text(view, 1));
        Assert.Equal(view.Slots[1].Start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void EnterReplacesTheSelection()
    {
        var view = Shown("hello world");

        view.MoveTo(view.Slots[0].Start + 4, extend: false);
        view.MoveTo(view.Slots[0].Start + 7, extend: true);
        Press(view, Key.Enter);

        Assert.Equal(2, view.Blocks.Count);
        Assert.Equal("hell", Text(view, 0));
        Assert.Equal("orld", Text(view, 1));
    }

    [AvaloniaFact]
    public void EnterAcrossTwoParagraphsLeavesTwo()
    {
        var view = Shown("hello", "world");

        view.MoveTo(view.Slots[0].Start + 3, extend: false);
        view.MoveTo(view.Slots[1].Start + 3, extend: true);
        Press(view, Key.Enter);

        Assert.Equal(2, view.Blocks.Count);
        Assert.Equal("hel", Text(view, 0));
        Assert.Equal("ld", Text(view, 1));
    }

    [AvaloniaFact]
    public void BothHalvesKeepTheBlocksPresentation()
    {
        var view = Shown(new DocumentNode([
            ParagraphNode.FromText("a quotation")
                .WithKind(ParagraphKind.Quote)
                .WithAlign(TextAlign.Center)
                .WithIndentLevel(2),
        ]));

        view.MoveTo(view.Slots[0].Start + 2, extend: false);
        Press(view, Key.Enter);

        foreach (var index in new[] { 0, 1 })
        {
            var paragraph = (ParagraphNode)view.Slots[index].Node;

            Assert.Equal(ParagraphKind.Quote, paragraph.Kind);
            Assert.Equal(TextAlign.Center, paragraph.Align);
            Assert.Equal(2, paragraph.IndentLevel);
        }
    }

    [AvaloniaFact]
    public void ASplitIsUndoneInOneStep()
    {
        var view = Shown("hello world");

        view.MoveTo(view.Slots[0].Start + 5, extend: false);
        Press(view, Key.Enter);

        Assert.True(view.Undo());
        Assert.Single(view.Blocks);
        Assert.Equal("hello world", Text(view, 0));
    }

    [AvaloniaFact]
    public void EnterDoesNotAlsoArriveAsText()
    {
        // Some backends deliver Enter as "\r" through text input as well as as a key. If both
        // were acted on, one press would split twice.
        var view = Shown("hello");

        view.MoveTo(view.Slots[0].Start + 2, extend: false);
        view.RaiseEvent(new TextInputEventArgs
        {
            RoutedEvent = InputElement.TextInputEvent,
            Text = "\r",
            Source = view,
        });

        Assert.Single(view.Blocks);
        Assert.Equal("hello", Text(view, 0));
    }

    [AvaloniaFact]
    public void BackspaceAtTheStartOfAParagraphJoinsItToTheOneAbove()
    {
        var view = Shown("hello", "world");

        view.MoveTo(view.Slots[1].Start, extend: false);
        Press(view, Key.Back);

        Assert.Single(view.Blocks);
        Assert.Equal("helloworld", Text(view, 0));
        Assert.Equal(view.Slots[0].Start + 5, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void DeleteAtTheEndOfAParagraphJoinsTheOneBelowToIt()
    {
        var view = Shown("hello", "world");

        view.MoveTo(view.Slots[0].End, extend: false);
        Press(view, Key.Delete);

        Assert.Single(view.Blocks);
        Assert.Equal("helloworld", Text(view, 0));
        Assert.Equal(view.Slots[0].Start + 5, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void BackspaceAtTheStartOfTheDocumentDoesNothing()
    {
        var view = Shown("hello");
        var before = view.State;

        view.MoveTo(view.Slots[0].Start, extend: false);

        Assert.False(view.Backspace());
        Assert.Same(before.Doc, view.State.Doc);
    }

    [AvaloniaFact]
    public void DeleteAtTheEndOfTheDocumentDoesNothing()
    {
        var view = Shown("hello");
        var before = view.State;

        view.MoveTo(view.Slots[0].End, extend: false);

        Assert.False(view.Delete());
        Assert.Same(before.Doc, view.State.Doc);
    }

    [AvaloniaFact]
    public void BackspaceBelowARuleRemovesTheRule()
    {
        var view = RuleDocument();

        view.MoveTo(view.Slots[2].Start, extend: false);
        Press(view, Key.Back);

        Assert.Equal(2, view.Blocks.Count);
        Assert.DoesNotContain(view.Slots, slot => slot.Node is RuleNode);
        Assert.Equal("above", Text(view, 0));
        Assert.Equal("below", Text(view, 1));
        Assert.Equal(view.Slots[1].Start, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void DeleteAboveARuleRemovesTheRule()
    {
        var view = RuleDocument();

        view.MoveTo(view.Slots[0].End, extend: false);
        Press(view, Key.Delete);

        Assert.Equal(2, view.Blocks.Count);
        Assert.DoesNotContain(view.Slots, slot => slot.Node is RuleNode);
        Assert.Equal(view.Slots[0].End, view.State.Selection.Head);
    }

    [AvaloniaFact]
    public void JoiningRelaysOutOnlyWhatChanged()
    {
        var view = Shown("first", "second", "third", "fourth");
        var before = view.Blocks.ToArray();

        view.MoveTo(view.Slots[2].Start, extend: false);
        Press(view, Key.Back);

        Assert.Equal(3, view.Blocks.Count);
        Assert.Same(before[0], view.Blocks[0]);
        Assert.Same(before[3], view.Blocks[2]);
    }

    [AvaloniaFact]
    public void ASelectionSpanningBlocksIsDeletedWhole()
    {
        var view = Shown("first", "second", "third");

        view.MoveTo(view.Slots[0].Start + 2, extend: false);
        view.MoveTo(view.Slots[2].Start + 2, extend: true);

        Assert.True(view.Backspace());
        Assert.Single(view.Blocks);
        Assert.Equal("fiird", Text(view, 0));
    }

    private static string Text(RichTextView view, int index) =>
        ((ParagraphNode)view.Slots[index].Node).Content.Text;

    private static RichTextView RuleDocument() => Shown(new DocumentNode([
        ParagraphNode.FromText("above"),
        RuleNode.Instance,
        ParagraphNode.FromText("below"),
    ]));

    private static void Press(RichTextView view, Key key) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            Source = view,
        });

    private static RichTextView Shown(params string[] paragraphs) =>
        Shown(DocumentNode.FromParagraphs(paragraphs));

    private static RichTextView Shown(DocumentNode doc)
    {
        var root = new Window { Width = 400, Height = 400 };
        var view = new RichTextView { State = EditorState.Create(doc) };

        root.Content = view;
        root.Show();
        view.Focus();

        view.Measure(new Size(400, 400));
        view.Arrange(new Rect(0, 0, 400, 400));

        return view;
    }
}
