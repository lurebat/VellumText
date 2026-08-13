using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Automation.Peers;
using global::Avalonia.Automation.Provider;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// What a screen reader is told about a document, per architecture 10.3 P7.
/// </summary>
/// <remarks>
/// <para>
/// Every assertion goes through <see cref="ControlAutomationPeer.CreatePeerForElement"/> rather
/// than through the peer's constructor, because that is the path the platform takes: a peer that
/// is correct but never reached by <c>OnCreateAutomationPeer</c> announces nothing at all, and
/// that failure is invisible to a test that news the peer up itself.
/// </para>
/// <para>
/// There is deliberately nothing here about caret or selection. Avalonia 12 has no text pattern
/// to say it on, and a test asserting a caret announcement would have to invent an interface
/// nobody consumes.
/// </para>
/// </remarks>
public class DocumentPresenterAutomationPeerTests
{
    [AvaloniaFact]
    public void TheEditorIsAWritableDocument()
    {
        var peer = Peer(Editor(DocumentNode.FromParagraphs("hello")));

        Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
        Assert.False(Value(peer).IsReadOnly);
    }

    [AvaloniaFact]
    public void TheViewerIsAReadOnlyDocument()
    {
        var peer = Peer(Viewer(DocumentNode.FromParagraphs("hello")));

        Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
        Assert.True(Value(peer).IsReadOnly);
    }

    [AvaloniaFact]
    public void TheValueIsTheDocumentsPlainText()
    {
        var peer = Peer(Viewer(DocumentNode.FromParagraphs("first", "second")));

        Assert.Equal("first\nsecond", Value(peer).Value);
    }

    /// <remarks>
    /// The presenter is not a leaf that merely labels something else, and an assistive technology
    /// filtering to the content view would otherwise skip the entire document.
    /// </remarks>
    [AvaloniaFact]
    public void TheDocumentIsInTheContentView()
    {
        Assert.True(Peer(Viewer(DocumentNode.FromParagraphs("hello"))).IsContentElement());
    }

    /// <remarks>
    /// Telling a screen reader that a viewer can take focus sends the user's focus somewhere it
    /// cannot go, and they cannot get back.
    /// </remarks>
    [AvaloniaFact]
    public void OnlyTheEditorIsOfferedKeyboardFocus()
    {
        Assert.True(Peer(Editor(DocumentNode.FromParagraphs("hello"))).IsKeyboardFocusable());
        Assert.False(Peer(Viewer(DocumentNode.FromParagraphs("hello"))).IsKeyboardFocusable());
    }

    [AvaloniaFact]
    public void EditingChangesTheValue()
    {
        var editor = Editor(DocumentNode.FromParagraphs("hello"));
        var peer = Peer(editor);

        Assert.Equal("hello", Value(peer).Value);

        editor.MoveTo(editor.Slots[0].End, extend: false);
        editor.InsertText("!");

        Assert.Equal("hello!", Value(peer).Value);
    }

    /// <remarks>
    /// A screen reader that has to poll to notice a change reads the document out stale. This is
    /// the only signal the platform gives us, so it has to fire.
    /// </remarks>
    [AvaloniaFact]
    public void EditingAnnouncesTheNewValue()
    {
        var editor = Editor(DocumentNode.FromParagraphs("hello"));
        var peer = Peer(editor);
        var changes = new List<AutomationPropertyChangedEventArgs>();

        // Reading first, so the peer has a value to compare against rather than announcing the
        // document it started with.
        Assert.Equal("hello", Value(peer).Value);

        peer.PropertyChanged += (_, e) => changes.Add(e);

        editor.MoveTo(editor.Slots[0].End, extend: false);
        editor.InsertText("!");

        var change = Assert.Single(changes, c => c.Property == ValuePatternIdentifiers.ValueProperty);

        Assert.Equal("hello", change.OldValue);
        Assert.Equal("hello!", change.NewValue);
    }

    /// <remarks>
    /// The peer watches every property because the two presenters carry their document
    /// differently. That is only affordable if it stays quiet about changes that are not changes
    /// to the text — a formatting command produces a whole new document, and announcing it would
    /// make a screen reader re-read the page every time the user pressed Bold.
    /// </remarks>
    [AvaloniaFact]
    public void ChangingOnlyTheFormattingAnnouncesNothing()
    {
        var editor = Editor(DocumentNode.FromParagraphs("hello"));
        var peer = Peer(editor);
        var changes = new List<AutomationPropertyChangedEventArgs>();

        Assert.Equal("hello", Value(peer).Value);

        peer.PropertyChanged += (_, e) => changes.Add(e);

        editor.SelectAll();
        editor.Width = 321;

        var before = editor.State.Doc;

        Assert.True(editor.ToggleStyle(TextStyle.Bold));

        // The document really was replaced -- this is not a test that nothing happened.
        Assert.NotSame(before, editor.State.Doc);
        Assert.Equal("hello", Value(peer).Value);
        Assert.Empty(changes);
    }

    /// <remarks>
    /// Destructive and nonetheless the contract: this is how every UI-automation framework fills
    /// a field, and an editor that refused it could not be driven by one.
    /// </remarks>
    [AvaloniaFact]
    public void SettingTheValueReplacesTheWholeDocument()
    {
        var editor = Editor(DocumentNode.FromParagraphs("first", "second"));
        var peer = Peer(editor);

        Value(peer).SetValue("replaced\nentirely");

        Assert.Equal("replaced\nentirely", Value(peer).Value);
    }

    /// <remarks>
    /// Selecting is not an edit, so the replacement is one transaction. Two undos to take back one
    /// automation call would be a bug a user meets only after being confused by it.
    /// </remarks>
    [AvaloniaFact]
    public void OneUndoTakesBackASetValue()
    {
        var editor = Editor(DocumentNode.FromParagraphs("first", "second"));
        var peer = Peer(editor);

        Value(peer).SetValue("replaced");

        Assert.True(editor.Undo());
        Assert.Equal("first\nsecond", Value(peer).Value);
    }

    /// <remarks>
    /// Empty is a deletion, not a paste of nothing. <c>PasteText</c> treats empty text as no
    /// change at all, which would leave the document standing.
    /// </remarks>
    [AvaloniaFact]
    public void SettingAnEmptyValueClearsTheDocument()
    {
        var editor = Editor(DocumentNode.FromParagraphs("first", "second"));
        var peer = Peer(editor);

        Value(peer).SetValue(string.Empty);

        Assert.Equal(string.Empty, Value(peer).Value);
    }

    [AvaloniaFact]
    public void SettingTheValueOfAViewerIsRefused()
    {
        var peer = Peer(Viewer(DocumentNode.FromParagraphs("hello")));

        Assert.Throws<ElementNotEnabledException>(() => Value(peer).SetValue("nope"));
        Assert.Equal("hello", Value(peer).Value);
    }

    private static IValueProvider Value(AutomationPeer peer) =>
        Assert.IsAssignableFrom<IValueProvider>(peer);

    /// <summary>The peer the platform would reach, not one the test built.</summary>
    private static AutomationPeer Peer(Control control) =>
        Assert.IsType<DocumentPresenterAutomationPeer>(
            ControlAutomationPeer.CreatePeerForElement(control));

    private static RichTextView Editor(DocumentNode doc) =>
        Shown(new RichTextView { State = EditorState.Create(doc) });

    private static RichTextViewer Viewer(DocumentNode doc) =>
        Shown(new RichTextViewer { Document = doc });

    private static T Shown<T>(T control)
        where T : Control
    {
        var window = new Window { Content = control, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return control;
    }
}
