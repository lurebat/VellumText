using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using VellumText;
using VellumText.Avalonia;
using Xunit;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Making, editing, removing and following a hyperlink.
/// </summary>
/// <remarks>
/// The model carried <c>LinkMark</c> and both importers produced links long before there was any
/// way for a user to reach one. These cover the gestures — and, most importantly, that activating
/// a link notifies the host rather than opening anything.
/// </remarks>
public class RichTextViewLinkTests
{
    private const double Width = 400;
    private const double Height = 300;

    private static readonly LinkMark Example = new("https://example.com");

    [AvaloniaFact]
    public void ControlClickRaisesLinkActivatedWithTheLink()
    {
        var view = Linked(out var window);
        LinkMark? raised = null;

        view.LinkActivated += (_, e) => raised = e.Link;

        Press(view, window, At(view, 4), KeyModifiers.Control);

        Assert.NotNull(raised);
        Assert.Equal("https://example.com", raised!.Href);
    }

    /// <remarks>
    /// The whole security argument for this feature. A control library that opens a URL because a
    /// document said so hands every pasted clip the ability to start a process. If someone later
    /// adds a <c>Process.Start</c> to activation, this is what should stop them — the event must
    /// be the <em>only</em> thing activation does.
    /// </remarks>
    [AvaloniaFact]
    public void ActivatingALinkWithNoHandlerDoesNothingAtAll()
    {
        var view = Linked(out var window);
        var before = view.State.Doc;

        // No LinkActivated handler subscribed at all.
        Press(view, window, At(view, 4), KeyModifiers.Control);

        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void APlainClickOnALinkDoesNotActivateIt()
    {
        var view = Linked(out var window);
        var raised = 0;

        view.LinkActivated += (_, _) => raised++;

        Press(view, window, At(view, 4), KeyModifiers.None);

        Assert.Equal(0, raised);
    }

    [AvaloniaFact]
    public void ClickingTextThatIsNotALinkDoesNotActivateAnything()
    {
        var view = Linked(out var window);
        var raised = 0;

        view.LinkActivated += (_, _) => raised++;

        // "plain" sits after the linked run.
        Press(view, window, At(view, 60), KeyModifiers.Control);

        Assert.Equal(0, raised);
    }

    [AvaloniaFact]
    public void ApplyLinkPutsALinkOnTheSelection()
    {
        var view = Shown(out _, DocumentNode.FromParagraphs("hello world"));

        view.MoveTo(1, extend: false);
        view.MoveTo(6, extend: true);

        Assert.True(view.ApplyLink(Example));

        Assert.Equal("https://example.com", view.LinkAt(1)?.Href);
        Assert.Equal("https://example.com", view.LinkAt(5)?.Href);

        // And nowhere else: a link that leaked past the selection is the bug worth catching.
        Assert.Null(view.LinkAt(6));
    }

    /// <remarks>
    /// The asymmetry this exists for: a user removes a link with the caret somewhere in the
    /// middle of it, not by selecting its text exactly first. <c>SetLink(null)</c> on a caret only
    /// disarms the stored marks and leaves the document alone.
    /// </remarks>
    [AvaloniaFact]
    public void RemoveLinkWorksFromACaretInsideTheLink()
    {
        var view = Linked(out _);

        view.MoveTo(4, extend: false);

        Assert.True(view.RemoveLink());

        Assert.Null(view.LinkAt(1));
        Assert.Null(view.LinkAt(4));

        // The text is untouched — this removes the mark, not the words.
        Assert.Contains("a link here", DocumentText.Of(view.State.Doc), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ApplyLinkFromACaretRetargetsTheWholeLink()
    {
        var view = Linked(out _);

        view.MoveTo(4, extend: false);

        Assert.True(view.ApplyLink(new LinkMark("https://elsewhere.test")));

        Assert.Equal("https://elsewhere.test", view.LinkAt(1)?.Href);
        Assert.Equal("https://elsewhere.test", view.LinkAt(10)?.Href);
    }

    [AvaloniaFact]
    public void ControlKAsksTheHostForALinkAndOffersTheOneAlreadyThere()
    {
        var view = Linked(out _);
        var asked = 0;
        LinkMark? offered = null;

        view.LinkRequested += (_, e) =>
        {
            asked++;
            offered = e.Existing;
        };

        view.Focus();
        view.MoveTo(4, extend: false);
        Key(view, global::Avalonia.Input.Key.K, KeyModifiers.Control);

        Assert.Equal(1, asked);
        Assert.Equal("https://example.com", offered?.Href);
    }

    [AvaloniaFact]
    public void ControlShiftKRemovesTheLink()
    {
        var view = Linked(out _);

        view.Focus();
        view.MoveTo(4, extend: false);
        Key(view, global::Avalonia.Input.Key.K, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Null(view.LinkAt(4));
    }

    /// <remarks>
    /// The href, not the title alone. A link titled "your invoice" that points somewhere else is
    /// the whole of phishing, and the tooltip is the only place a reader can see where it goes.
    /// </remarks>
    [AvaloniaFact]
    public void HoveringALinkShowsWhereItGoes()
    {
        var view = Linked(out var window);

        window.MouseMove(At(view, 4));

        var tip = ToolTip.GetTip(view) as string;

        Assert.NotNull(tip);
        Assert.Contains("https://example.com", tip!, StringComparison.Ordinal);

        window.MouseMove(At(view, 60));

        Assert.Null(ToolTip.GetTip(view));
    }

    private static void Key(RichTextView view, Key key, KeyModifiers modifiers) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });

    private static void Press(RichTextView view, Window window, Point point, KeyModifiers modifiers) =>
        view.RaiseEvent(new PointerPressedEventArgs(
            view,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
            window,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            modifiers,
            1));

    /// <summary>A point on the first line, at a given x.</summary>
    private static Point At(RichTextView view, double x) =>
        new(x, view.Blocks[0].Size.Height / 2);

    private static RichTextView Linked(out Window window)
    {
        var view = Shown(out window, DocumentNode.FromParagraphs("a link here and then plain text after it"));

        view.MoveTo(1, extend: false);
        view.MoveTo(12, extend: true);
        view.ApplyLink(Example);
        view.MoveTo(1, extend: false);

        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        return view;
    }

    private static RichTextView Shown(out Window window, DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };

        window = new Window { Content = view, Width = Width, Height = Height };
        window.Show();

        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        return view;
    }
}
