using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What a view model can bind to and observe, per architecture 1.1 AC13.
/// </summary>
/// <remarks>
/// <para>
/// The gap these close: a view model could reach the editor's <c>State</c>, which is the whole
/// bundle, and nothing else. Asking "has the document changed?" meant holding the previous
/// <c>EditorState</c> and comparing its <c>Doc</c> by hand, and there was no property a
/// <c>{Binding}</c> could name at all.
/// </para>
/// <para>
/// These drive the binding rather than the property, deliberately. A property that answers
/// correctly when a test calls its getter and cannot be bound is exactly the defect: it passes an
/// existence check and fails the developer.
/// </para>
/// </remarks>
public class RichTextBindingTests
{
    /// <summary>A view model, as plain as one gets: the target of a one-way-to-source binding.</summary>
    private sealed class Recorder
    {
        private DocumentNode? _doc;

        /// <summary>How many times the binding has written a document here.</summary>
        public int Writes { get; private set; }

        public DocumentNode? Doc
        {
            get => _doc;
            set
            {
                _doc = value;
                Writes++;
            }
        }

        public Selection? Selection { get; set; }
    }

    private static RichTextEditor Templated(string text)
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs(text)) };
        var window = new Window { Width = 400, Height = 200, Content = editor };

        window.Show();

        return editor;
    }

    [AvaloniaFact]
    public void AnEditReachesAViewModelThroughAnOrdinaryBinding()
    {
        var editor = Templated("hello");
        var model = new Recorder();

        // No Mode given, which is the case that matters: a developer writes Document="{Binding Doc}"
        // and the property's own default has to be the direction it actually has.
        editor.Bind(RichTextEditor.DocumentProperty, new Binding(nameof(Recorder.Doc)) { Source = model });

        editor.EnsureView().SelectAll();
        Assert.True(editor.EnsureView().InsertText("!"));

        Assert.NotNull(model.Doc);
        Assert.Equal("!", DocumentText.Of(model.Doc).Trim());
        Assert.Same(editor.Document, model.Doc);
    }

    [AvaloniaFact]
    public void ADocumentLoadedBeforeTemplatingIsStillAnnounced()
    {
        // A view model that loads in its constructor loads before OnApplyTemplate has run. The two
        // events are raised from the shell's own state rather than relayed from a view that does
        // not exist yet, so this is the case that would break if that changed. Deliberately never
        // touches EnsureView: that would build the view and hide the case this is here for.
        var editor = new RichTextEditor();
        var model = new Recorder();
        var documents = 0;

        editor.Bind(RichTextEditor.DocumentProperty, new Binding(nameof(Recorder.Doc)) { Source = model });
        editor.DocumentChanged += (_, _) => documents++;

        editor.State = EditorState.Create(DocumentNode.FromParagraphs("already here"));

        Assert.Equal(1, documents);
        Assert.Same(editor.Document, model.Doc);
        Assert.Equal("already here", DocumentText.Of(editor.Document).Trim());
    }

    [AvaloniaFact]
    public void AViewModelSubscribedBeforeTemplatingHearsTheFirstEdit()
    {
        // The constructor-time case RichTextEditorForwardingTests covers for the setup surfaces.
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("already here")),
        };

        var documents = 0;
        var selections = 0;

        editor.DocumentChanged += (_, _) => documents++;
        editor.SelectionChanged += (_, _) => selections++;

        Assert.Equal("already here", DocumentText.Of(editor.Document).Trim());

        Assert.True(editor.EnsureView().InsertText("!"));

        Assert.Equal(1, documents);
        Assert.Equal(1, selections);
    }

    [AvaloniaFact]
    public void ABindingCarriesChangesAndNotTheValueThatWasAlreadyThere()
    {
        // Measured against Avalonia 12.1.1, and the reason the two events exist at all: a
        // OneWayToSource binding attached to a control that already holds a document writes
        // nothing until something changes. A view model that only bound would hold null through
        // an entire session in which the user changed nothing, and save null.
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("already here")),
        };

        var model = new Recorder();

        editor.Bind(RichTextEditor.DocumentProperty, new Binding(nameof(Recorder.Doc)) { Source = model });

        Assert.Null(model.Doc);

        // The property itself answers, which is what a handler of either event reads.
        Assert.Equal("already here", DocumentText.Of(editor.Document).Trim());

        // And the binding is genuinely attached: the positive control for the assertion above,
        // which would otherwise pass just as well against a binding that never worked at all.
        Assert.True(editor.EnsureView().InsertText("!"));
        Assert.NotNull(model.Doc);
    }

    [AvaloniaFact]
    public void MovingTheCaretAnnouncesTheSelectionAndNotTheDocument()
    {
        var editor = Templated("hello");
        var model = new Recorder();
        var documents = 0;
        var selections = 0;

        editor.Bind(RichTextEditor.DocumentProperty, new Binding(nameof(Recorder.Doc)) { Source = model });
        editor.Bind(RichTextEditor.SelectionProperty, new Binding(nameof(Recorder.Selection)) { Source = model });
        editor.DocumentChanged += (_, _) => documents++;
        editor.SelectionChanged += (_, _) => selections++;

        Assert.True(editor.EnsureView().SelectAll());

        Assert.Equal(0, documents);
        Assert.Equal(0, model.Writes);
        Assert.Equal(1, selections);
        Assert.Equal(editor.Selection, model.Selection);

        // The positive control. Without it this passes just as well against a fixture whose
        // document binding was never attached at all, which is what it is trying to rule out.
        Assert.True(editor.EnsureView().InsertText("x"));
        Assert.Equal(1, documents);
        Assert.Equal(1, model.Writes);
    }

    [AvaloniaFact]
    public void OneEditIsAnnouncedOnce()
    {
        // The shell relays the view's state into its own, and the view is told the shell's state
        // back. A pair that echoed would announce every keystroke twice, and a view model that
        // saves on change would write the file twice.
        var editor = Templated("hello");
        var model = new Recorder();
        var documents = 0;

        editor.Bind(RichTextEditor.DocumentProperty, new Binding(nameof(Recorder.Doc)) { Source = model });
        editor.DocumentChanged += (_, _) => documents++;

        Assert.True(editor.EnsureView().InsertText("x"));

        Assert.Equal(1, model.Writes);
        Assert.Equal(1, documents);
    }

    [AvaloniaFact]
    public void TheViewAnnouncesTheSameTwoThingsTheShellDoes()
    {
        var editor = Templated("hello");
        var view = editor.EnsureView();
        var model = new Recorder();
        var documents = 0;
        var selections = 0;

        view.Bind(RichTextView.DocumentProperty, new Binding(nameof(Recorder.Doc)) { Source = model });
        view.Bind(RichTextView.SelectionProperty, new Binding(nameof(Recorder.Selection)) { Source = model });
        view.DocumentChanged += (_, _) => documents++;
        view.SelectionChanged += (_, _) => selections++;

        Assert.True(view.InsertText("x"));

        Assert.Same(view.Document, model.Doc);
        Assert.Same(view.State.Doc, view.Document);
        Assert.Equal(view.State.Selection, view.Selection);
        Assert.Equal(view.Selection, model.Selection);
        Assert.Equal(1, documents);
        Assert.Equal(1, selections);
    }

    [AvaloniaFact]
    public void ADocumentAssignedFromOutsideIsRefusedRatherThanHalfHonoured()
    {
        // The decision recorded in §4.6: one-way out. Reconciling a document assigned from outside
        // against edits in flight and a history recorded over a document that is no longer there is
        // a design with real hazards. Refusing it loudly is the honest half of saying so - a
        // property that accepted the write and dropped it would be the silent one.
        var editor = Templated("hello");

        Assert.Throws<ArgumentException>(
            () => editor.SetValue(RichTextEditor.DocumentProperty, DocumentNode.FromParagraphs("other")));
        Assert.Throws<ArgumentException>(
            () => editor.EnsureView().SetValue(RichTextView.DocumentProperty, DocumentNode.FromParagraphs("other")));

        Assert.Equal("hello", DocumentText.Of(editor.Document).Trim());
    }

    [AvaloniaFact]
    public void LoadingADocumentThroughStateIsAnnouncedToo()
    {
        // The supported direction in: assign State. A view model that both loads and observes has
        // to hear its own load, or it will believe the editor still holds what was there before.
        var editor = Templated("hello");
        var model = new Recorder();
        var documents = 0;

        editor.Bind(RichTextEditor.DocumentProperty, new Binding(nameof(Recorder.Doc)) { Source = model });
        editor.DocumentChanged += (_, _) => documents++;

        editor.State = EditorState.Create(DocumentNode.FromParagraphs("loaded"));

        Assert.Equal("loaded", DocumentText.Of(model.Doc!).Trim());
        Assert.Equal(1, documents);
    }

    [AvaloniaFact]
    public void ArmingFormattingAtACaretAnnouncesNeitherHalf()
    {
        // Ctrl+B at a collapsed caret installs a new state that changes neither the document nor
        // the selection - only the formatting the next character will get. A view model that saves
        // on DocumentChanged would otherwise write the file for a change no reader could see.
        // (This has to be a state that really is new: an equal EditorState is a record, so
        // assigning one is dropped by Avalonia before it reaches the announcement at all.)
        var editor = Templated("hello");
        var view = editor.EnsureView();
        var documents = 0;
        var selections = 0;

        editor.DocumentChanged += (_, _) => documents++;
        editor.SelectionChanged += (_, _) => selections++;

        var before = editor.State;
        Assert.True(view.ToggleStyle(TextStyle.Bold));
        Assert.NotSame(before, editor.State);

        Assert.Equal(0, documents);
        Assert.Equal(0, selections);

        // Positive control: the same fixture does hear a real change.
        Assert.True(view.InsertText("x"));
        Assert.Equal(1, documents);
        Assert.Equal(1, selections);
    }

    [AvaloniaFact]
    public void UndoIsAnnouncedLikeAnyOtherChange()
    {
        // Undo does not go through the typing path, so it is the case a relay hung off the wrong
        // seam would miss - and a view model's dirty flag is exactly what depends on hearing it.
        var editor = Templated("hello");
        var documents = 0;

        Assert.True(editor.EnsureView().InsertText("x"));

        editor.DocumentChanged += (_, _) => documents++;

        Assert.True(editor.Undo());
        Assert.Equal(1, documents);
        Assert.Equal("hello", DocumentText.Of(editor.Document).Trim());
    }
}
