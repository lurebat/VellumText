using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Threading;
using VellumText;
using VellumText.Avalonia;
using Xunit;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The editing conventions a user expects and, until now, did not find.
/// </summary>
public class RichTextViewConventionTests
{
    private const double Width = 400;
    private const double Height = 300;

    [Theory]
    [InlineData("hello world", 2, "hello")]
    [InlineData("hello world", 8, "world")]
    // UAX #29 keeps an apostrophe inside a contraction; a letter-or-digit run splits it in three.
    [InlineData("don't stop", 2, "don't")]
    // And keeps a decimal number whole, where a letter-or-digit run gives "3" and "14".
    [InlineData("pi is 3.14 exactly", 8, "3.14")]
    [InlineData("snake_case here", 3, "snake_case")]
    public void WordAtFindsTheWholeWord(string text, int offset, string expected)
    {
        var (start, end) = TextBoundaries.WordAt(text, offset);

        Assert.Equal(expected, text[start..end]);
    }

    /// <remarks>
    /// The reason this is not hand-rolled. A four-person family emoji is one word and one grapheme
    /// cluster of eleven UTF-16 units joined by ZWJ; splitting it renders as a row of strangers.
    /// The same goes for a base letter and its combining marks.
    /// </remarks>
    [Theory]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466")]
    [InlineData("e\u0301\u0327a")]
    public void WordAtNeverSplitsAGraphemeCluster(string word)
    {
        for (var offset = 0; offset <= word.Length; offset++)
        {
            var (start, end) = TextBoundaries.WordAt(word + " tail", offset);

            if (start < word.Length)
            {
                Assert.Equal(word, (word + " tail")[start..end]);
            }
        }
    }

    [AvaloniaFact]
    public void DoubleClickSelectsAWordAndTripleClickSelectsTheParagraph()
    {
        var view = Shown(out var window, "hello world and some more", "a second paragraph");

        Click(view, window, At(view, 0, 2), clicks: 2);
        Assert.Equal("hello", view.SelectedText());

        Click(view, window, At(view, 0, 2), clicks: 3);
        Assert.Equal("hello world and some more", view.SelectedText());
    }

    /// <remarks>
    /// A fourth click is a new double-click, not a fourth-level selection: that is what every
    /// other editor does, and the alternative — staying on the paragraph — leaves a user who
    /// clicked once too often unable to get back to a word without pausing.
    /// </remarks>
    [AvaloniaFact]
    public void AFourthClickGoesBackToAWord()
    {
        var view = Shown(out var window, "hello world and some more");

        Click(view, window, At(view, 0, 2), clicks: 4);

        Assert.Equal("hello", view.SelectedText());
    }

    [AvaloniaFact]
    public void ShiftEnterInsertsASoftBreakWithinTheParagraph()
    {
        var view = Shown(out _, "hello world");

        view.Focus();
        view.MoveTo(6, extend: false);
        Key(view, global::Avalonia.Input.Key.Enter, KeyModifiers.Shift);

        // One paragraph still, with a newline inside it. Asserting the content rather than that
        // the document is valid: a document of two paragraphs is equally valid and equally wrong.
        Assert.Single(view.State.Doc.Blocks);
        Assert.Equal("hello\n world", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void ShiftEnterDoesNothingWhenReturnsAreRefused()
    {
        var view = Shown(out _, "hello world");

        view.AcceptsReturn = false;
        view.Focus();
        view.MoveTo(6, extend: false);
        Key(view, global::Avalonia.Input.Key.Enter, KeyModifiers.Shift);

        Assert.Equal("hello world", DocumentText.Of(view.State.Doc));
    }

    /// <remarks>
    /// The plain-text paste has to go through the same import seam as any other, or a host that
    /// listens for <c>ContentImported</c> to warn about lost formatting stops hearing about it.
    /// </remarks>
    [AvaloniaFact]
    public void PasteAsPlainTextGoesThroughTheImportSeam()
    {
        var view = Shown(out var window, "start");
        var imported = 0;

        view.ContentImported += (_, _) => imported++;

        var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;

        Dispatcher.UIThread.RunJobs();
        var payload = new global::Avalonia.Input.DataTransfer();
        var pastedItem = new global::Avalonia.Input.DataTransferItem();
        pastedItem.SetText("pasted");
        payload.Add(pastedItem);
        clipboard.SetDataAsync(payload).GetAwaiter().GetResult();

        view.Focus();
        view.MoveTo(1, extend: false);
        Key(view, global::Avalonia.Input.Key.V, KeyModifiers.Control | KeyModifiers.Shift);

        // async void handler: the paste completes on a later dispatcher turn.
        Dispatcher.UIThread.RunJobs();

        Assert.Contains("pasted", DocumentText.Of(view.State.Doc), StringComparison.Ordinal);
        Assert.Equal(1, imported);
    }

    /// <remarks>
    /// Drag-selecting past the bottom edge has to keep going, or selecting more than a screenful
    /// is impossible with a pointer.
    ///
    /// Two assertions, because the first version of this test made neither: it pumped the
    /// dispatcher and checked the offset had moved, which it had — but from
    /// <c>ScrollAnchorIntoView</c> on the pointer move itself, not from auto-scroll. Deleting the
    /// auto-scroll wiring entirely left it green. A <c>DispatcherTimer</c> does not fire under
    /// the headless job pump, so the timer is checked for being armed and its tick is driven by
    /// hand.
    /// </remarks>
    [AvaloniaFact]
    public void DraggingPastTheEdgeKeepsScrollingWhileThePointerIsHeldThere()
    {
        var view = Shown(out var window, Enumerable.Range(0, 60)
            .Select(i => $"paragraph number {i} with enough text to occupy a line")
            .ToArray());

        var scroll = (global::Avalonia.Controls.Primitives.IScrollable)view;

        window.MouseDown(At(view, 0, 2), MouseButton.Left);
        window.MouseMove(new Point(200, view.Bounds.Height + 40));

        Assert.True(view.AutoScrolling, "a drag past the bottom edge should arm the auto-scroll timer");

        // The pointer does not move again. Everything past this point is the timer's doing.
        var before = scroll.Offset.Y;

        view.AutoScrollTick();

        Assert.True(scroll.Offset.Y > before, $"a tick should scroll past {before}, stayed at {scroll.Offset.Y}");

        // And the selection grows with it, rather than staying where the pointer last hit-tested.
        Assert.Contains("paragraph number", view.SelectedText(), StringComparison.Ordinal);
    }

    /// <remarks>
    /// A timer still running after the button came up scrolls the document under a pointer that
    /// is standing still, which looks like the control has taken over the mouse.
    /// </remarks>
    [AvaloniaFact]
    public void ReleasingTheButtonStopsTheAutoScroll()
    {
        var view = Shown(out var window, Enumerable.Range(0, 60)
            .Select(i => $"paragraph number {i} with enough text to occupy a line")
            .ToArray());

        window.MouseDown(At(view, 0, 2), MouseButton.Left);
        window.MouseMove(new Point(200, view.Bounds.Height + 40));

        Assert.True(view.AutoScrolling);

        window.MouseUp(new Point(200, view.Bounds.Height + 40), MouseButton.Left);

        Assert.False(view.AutoScrolling);
    }

    private static void Key(RichTextView view, Key key, KeyModifiers modifiers) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });

    /// <summary>Synthesizes a multi-click, which the headless mouse helpers cannot do.</summary>
    private static void Click(RichTextView view, Window window, Point point, int clicks) =>
        view.RaiseEvent(new PointerPressedEventArgs(
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
            window,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clicks));

    private static Point At(RichTextView view, int block, double x)
    {
        var y = 0.0;

        for (var i = 0; i < block; i++)
        {
            y += view.Blocks[i].Size.Height;
        }

        return new Point(x, y + (view.Blocks[block].Size.Height / 2));
    }

    private static RichTextView Shown(out Window window, params string[] paragraphs)
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)) };

        window = new Window { Content = view, Width = Width, Height = Height };
        window.Show();

        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        return view;
    }
}
