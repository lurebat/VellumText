using global::Avalonia;
using global::Avalonia.Automation;
using global::Avalonia.Automation.Peers;
using global::Avalonia.Automation.Provider;
using global::Avalonia.Controls;
using global::Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

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

    /// <summary>
    /// A locked editor tells a screen reader it is locked.
    /// </summary>
    /// <remarks>
    /// <see cref="DocumentPresenter.SupportsTextInput"/> was written when the only thing it had to
    /// separate was an editor from a viewer, and its remarks said that an editor which could be
    /// locked was a feature nobody had asked for. <see cref="RichTextView.IsReadOnly"/> is that
    /// feature, so the answer had to start depending on it: a read-only editor that reports itself
    /// writable is the toolbar defect aimed at the one user who cannot see that nothing happened.
    /// </remarks>
    [AvaloniaFact]
    public void AReadOnlyEditorIsAReadOnlyDocument()
    {
        var editor = Editor(DocumentNode.FromParagraphs("hello"));

        editor.IsReadOnly = true;

        Assert.True(Value(Peer(editor)).IsReadOnly);
    }

    /// <summary>
    /// Locking an editor that is already on screen changes the answer.
    /// </summary>
    /// <remarks>
    /// Separate from the test above because a peer built after the property was set can be right
    /// for the wrong reason — reading the value once at construction would pass it and fail here.
    /// </remarks>
    [AvaloniaFact]
    public void LockingAnEditorAlreadyShownChangesWhatAutomationReports()
    {
        var editor = Editor(DocumentNode.FromParagraphs("hello"));
        var peer = Peer(editor);

        Assert.False(Value(peer).IsReadOnly);

        editor.IsReadOnly = true;

        Assert.True(Value(peer).IsReadOnly);
    }

    /// <summary>
    /// An automation client cannot write through a locked editor, or move its selection trying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IValueProvider.SetValue"/> is contractually required to throw
    /// <see cref="ElementNotEnabledException"/> on a read-only element, and the peer already did —
    /// but it asked <see cref="IValueProvider.IsReadOnly"/>, which asked
    /// <c>SupportsTextInput</c>, which an editor answered <see langword="true"/> whatever its
    /// state. The guard was correct and unreachable.
    /// </para>
    /// <para>
    /// Both halves are asserted because they failed for different reasons. Before the fix the call
    /// did not throw at all, and the <c>SelectAll</c> that runs before the paste moved the caret
    /// over the whole document — the same shape as a read-only Cut writing the clipboard before
    /// its delete declines. A refusal that still moves the user's selection is not a refusal.
    /// </para>
    /// </remarks>
    [AvaloniaFact]
    public void AutomationCannotWriteOrSelectThroughALockedEditor()
    {
        var editor = Editor(DocumentNode.FromParagraphs("hello"));

        editor.IsReadOnly = true;

        var before = editor.State.Doc;
        var selection = editor.State.Selection;

        Assert.Throws<ElementNotEnabledException>(() => Value(Peer(editor)).SetValue("replaced"));

        Assert.Same(before, editor.State.Doc);
        Assert.Equal(selection, editor.State.Selection);
    }

    /// <summary>
    /// The guard inside the automation write, reached directly rather than through the peer.
    /// </summary>
    /// <remarks>
    /// The peer refuses a locked editor before it calls this, so removing the guard leaves every
    /// test above green — it was mutation-checked and the mutant survived. That makes this the
    /// only test that defends it. It exists because <see cref="DocumentPresenter"/> is
    /// deliberately derivable and <c>SupportsTextInput</c> is virtual, so a subclass answering
    /// <see langword="true"/> while locked reaches the guard, as would a second internal caller.
    /// </remarks>
    [AvaloniaFact]
    public void TheAutomationWriteRefusesALockedEditorOnItsOwn()
    {
        var editor = Editor(DocumentNode.FromParagraphs("hello"));

        editor.IsReadOnly = true;

        var before = editor.State.Doc;
        var selection = editor.State.Selection;

        editor.SetTextFromAutomation("replaced");

        Assert.Same(before, editor.State.Doc);
        Assert.Equal(selection, editor.State.Selection);
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
    /// Both are focusable now that the viewer takes the keyboard to be copied from. The earlier
    /// version of this test asserted the viewer was not, which was correct while it refused focus
    /// outright: telling a screen reader focus can go somewhere it cannot is a trap. The trap is
    /// gone, and the peer must not now lie in the other direction — a reader who cannot find the
    /// viewer cannot select or copy from it either.
    /// </remarks>
    [AvaloniaFact]
    public void BothPresentersAreOfferedKeyboardFocus()
    {
        Assert.True(Peer(Editor(DocumentNode.FromParagraphs("hello"))).IsKeyboardFocusable());
        Assert.True(Peer(Viewer(DocumentNode.FromParagraphs("hello"))).IsKeyboardFocusable());
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
