using System.Collections.Immutable;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;

namespace VellumText.Avalonia;

/// <summary>
/// Selecting everything, and moving text through the system clipboard.
/// </summary>
/// <remarks>
/// <para>
/// Plain text always works. The richer flavours architecture section 4.9 describes — HTML and RTF
/// — arrive by registration: this assembly deliberately does not reference the interop packages,
/// because an application that wants a text editor should not acquire an HTML parser and an RTF
/// reader to get one. Adding <c>HtmlFormat.Instance</c> and <c>RtfFormat.Instance</c> to
/// <see cref="Formats"/> is what turns them on, and nothing else changes.
/// </para>
/// <para>
/// A paste tries the registered formats in <see cref="RichTextClipboard.Preference"/>
/// order and stops at the first that yields a document, so a paste from a browser keeps its
/// formatting and a paste from a terminal still arrives as text.
/// </para>
/// <para>
/// The clipboard is asynchronous on every platform Avalonia supports, so these are tasks. The
/// keyboard and menu paths deliberately do not await them: a paste that has to wait on another
/// process must not freeze the caret.
/// </para>
/// </remarks>
public partial class RichTextView
{
    /// <summary>The interchange formats copy writes and paste reads, beyond plain text.</summary>
    /// <remarks>
    /// A format object usually implements both <see cref="IDocumentImporter"/> and
    /// <see cref="IDocumentExporter"/> — <c>HtmlFormat.Instance</c> and <c>RtfFormat.Instance</c>
    /// do — but either alone is accepted, so an application can offer a format it can read
    /// without claiming to write it. A format implementing neither is refused, loudly, at the
    /// point of registration rather than silently doing nothing at the point of use.
    /// </remarks>
    public ICollection<IDocumentFormat> Formats { get; } = new DocumentFormats();

    /// <summary>What an import had to compromise on, on its way into the document.</summary>
    /// <param name="diagnostics">Everything the importer could not do faithfully.</param>
    public sealed class ContentImportedEventArgs(ImmutableArray<ImportDiagnostic> diagnostics)
        : EventArgs
    {
        /// <summary>Everything the importer could not do faithfully, in the order it happened.</summary>
        public ImmutableArray<ImportDiagnostic> Diagnostics { get; } = diagnostics;

        /// <summary>Whether anything was removed for safety rather than merely downgraded.</summary>
        public bool AnythingDropped =>
            Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Dropped);
    }

    /// <summary>Raised once content from outside has been imported into the document.</summary>
    /// <remarks>
    /// <para>
    /// Every importer already says, item by item, where it gave ground — a <c>&lt;blink&gt;</c>
    /// dropped, a colour that could not be represented, an image over the store's limit, a table
    /// flattened. Every paste and every drop computed that list and discarded it, so the content
    /// changed on the way in and the application had no way to know, let alone say so. This is the
    /// same bargain <see cref="ImageRefused"/> makes: a host that does not subscribe gets exactly
    /// the old behaviour.
    /// </para>
    /// <para>
    /// <b>A notification, not a veto.</b> The content is in the document by the time this is
    /// raised and there is nothing here to cancel; interception before the fact is a separate
    /// thing and is deliberately not this. It is also raised for an import that had nothing to
    /// report, with an empty list — a host that puts up "2 things were changed" after one paste
    /// needs the next, clean paste to take that notice down, and it can only do that if the clean
    /// paste tells it anything at all.
    /// </para>
    /// </remarks>
    public event EventHandler<ContentImportedEventArgs>? ContentImported;

    /// <summary>Content from outside, before any of it has been inserted.</summary>
    /// <param name="content">The blocks that are about to replace the selection.</param>
    /// <param name="diagnostics">Everything the importer could not do faithfully.</param>
    public sealed class ContentImportingEventArgs(
        DocumentNode content,
        ImmutableArray<ImportDiagnostic> diagnostics)
        : CancelEventArgs
    {
        /// <summary>The document that is about to be inserted, exactly as it would arrive.</summary>
        /// <remarks>
        /// Not the bytes on the clipboard: what the importer made of them, after every drop and
        /// downgrade the diagnostics describe. A policy that inspected the source would be
        /// inspecting something else — the point is to vet what will actually be in the document.
        /// </remarks>
        public DocumentNode Content { get; } = content;

        /// <summary>Everything the importer could not do faithfully, in the order it happened.</summary>
        /// <remarks>Empty for plain text, which has no importer and so nothing to compromise on.</remarks>
        public ImmutableArray<ImportDiagnostic> Diagnostics { get; } = diagnostics;

        /// <summary>Whether anything was removed for safety rather than merely downgraded.</summary>
        public bool AnythingDropped =>
            Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Dropped);
    }

    /// <summary>Raised before content from outside is inserted, so a host can refuse it.</summary>
    /// <remarks>
    /// <para>
    /// The veto <see cref="ContentImported"/> deliberately is not. Set <c>Cancel</c> and the paste
    /// or drop does nothing at all: no edit, no undo entry, no <see cref="ContentImported"/> — a
    /// refusal is indistinguishable from a paste of nothing, which is what it is. The host is the
    /// one refusing, so it already knows; nothing is reported back to it.
    /// </para>
    /// <para>
    /// <b>Raised only for content that would otherwise land.</b> A read-only control, a rectangle
    /// selection, an empty clip: none of them reach a policy that may be slow, and none of them
    /// have to be understood by a host writing one.
    /// </para>
    /// <para>
    /// <b>A handler that throws loses the paste, not the document.</b> Nothing has been applied
    /// when this runs, so there is no half-inserted state to unwind. On the keyboard, menu and drop
    /// paths the exception meets the same handler a failing clipboard does and the paste is simply
    /// dropped; a host calling <see cref="PasteImport(ImportResult)"/> itself sees its own exception, because
    /// swallowing it there would hide the host's bug from the host.
    /// </para>
    /// <para>
    /// Images are not routed through this. <c>IImageStore.Save</c> returning null is already an
    /// inspect-and-veto for image bytes, and <see cref="ImageRefused"/> already reports it — a
    /// second way to say no would be a second thing to keep in step.
    /// </para>
    /// </remarks>
    public event EventHandler<ContentImportingEventArgs>? ContentImporting;

    /// <summary>Asks the host whether content from outside may be inserted.</summary>
    /// <remarks>
    /// The choke point, called from the two seams that insert imported content and from nowhere
    /// else, for the reason <see cref="PasteImport(ImportResult)"/> gives: a gate at each call site is a gate
    /// the next call site will forget.
    /// </remarks>
    /// <param name="content">What would be inserted.</param>
    /// <param name="diagnostics">What the import gave up.</param>
    /// <param name="gesture">
    /// Whether this is a paste the user started, where a policy that throws refuses the paste
    /// rather than reaching the window: nothing has been applied yet, so "this paste did not
    /// happen" is exactly true, and a bug in a host's policy should cost one paste, not the
    /// window. A host calling <see cref="PasteImport(ImportResult)"/> itself gets the exception.
    /// </param>
    private bool ContentAllowed(
        DocumentNode content, ImmutableArray<ImportDiagnostic> diagnostics, bool gesture = false)
    {
        if (ContentImporting is not { } handlers)
        {
            return true;
        }

        var refusal = new ContentImportingEventArgs(content, diagnostics);

        try
        {
            handlers(this, refusal);
        }
        catch (Exception) when (gesture)
        {
            return false;
        }

        return !refusal.Cancel;
    }

    /// <summary>Replaces the selection with an imported document, and says what the import cost.</summary>
    /// <param name="result">What an importer produced, diagnostics and all.</param>
    /// <returns><see langword="true"/> if the document changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// The single seam every import goes through — paste, drop, and a host importing a file of its
    /// own — so that <see cref="ContentImporting"/> and <see cref="ContentImported"/> cannot be
    /// raised by two of the three and forgotten by the last. Nothing is reported when the paste was
    /// refused, because then nothing arrived to explain.
    /// </remarks>
    public bool PasteImport(ImportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return PasteImport(result, CurrentOrigin());
    }

    /// <summary>
    /// <see cref="PasteImport(ImportResult)"/> for an import that started at
    /// <paramref name="origin"/>, which a <see cref="ContentImporting"/> handler may have moved.
    /// </summary>
    internal bool PasteImport(ImportResult result, EditOrigin origin, bool gesture = false)
    {
        // Asked before anything is applied, and only when there is something to ask about: a
        // policy is only shown content that would otherwise be in the document a moment later.
        if (!WouldPaste(result.Doc) || !ContentAllowed(result.Doc, result.Diagnostics, gesture))
        {
            return false;
        }

        // A handler is host code and can do anything, including load another document. The
        // content was judged against the state it was shown; it does not land in a different one.
        if (!StillAt(origin) || !PasteDocument(result.Doc))
        {
            return false;
        }

        RaiseContentImported(result.Diagnostics);

        return true;
    }

    /// <summary>Tells the host what an import that has already landed had to give up.</summary>
    private void RaiseContentImported(ImmutableArray<ImportDiagnostic> diagnostics) =>
        ContentImported?.Invoke(this, new ContentImportedEventArgs(diagnostics));

    /// <summary>Selects the whole document.</summary>
    /// <returns><see langword="true"/> if the selection changed.</returns>
    public bool SelectAll()
    {
        var from = NearestTextPosition(0, forward: true);
        var to = NearestTextPosition(_state.Doc.ContentSize, forward: false);

        if (_state.Selection is TextSelection current && current.From == from && current.To == to)
        {
            return false;
        }

        _goalX = null;
        State = EditorState.Create(_state.Doc, TextSelection.Create(_state.Doc, from, to));
        ResetCaretBlink();

        return true;
    }

    /// <summary>The plain text covered by the selection, empty when the selection is a caret.</summary>
    public string SelectedText()
    {
        var selection = _state.Selection;

        if (selection.IsEmpty)
        {
            return string.Empty;
        }

        if (selection is CellSelection rectangle)
        {
            return DocumentText.Of(CellDocument(rectangle).Blocks);
        }

        return DocumentText.Of(Slice.Cut(_state.Doc, selection.From, selection.To).Content);
    }

    /// <summary>Puts the selection on the clipboard, in every registered format.</summary>
    /// <returns><see langword="true"/> if anything was written.</returns>
    public async Task<bool> CopyAsync()
    {
        var text = SelectedText();

        if (text.Length == 0)
        {
            return false;
        }

        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        // Through the shared helper, which swallows a clipboard another process is holding open.
        // Every caller of this starts it without awaiting it, so an exception out of here would
        // reach no handler at all.
        return await RichTextClipboard
            .WriteAsync(clipboard, SelectedDocument(), Formats.OfType<IDocumentExporter>(), text)
            .ConfigureAwait(true);
    }

    /// <summary>
    /// The clipboard payload for the current selection: plain text, plus a flavour for each
    /// registered exporter that has one.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="CopyAsync"/> because it is the whole of what a copy decides, and
    /// the only part of it a test can look at: what a real clipboard does with the payload
    /// afterwards is the platform's business, not this control's. The document the flavours
    /// describe is the selection, not the whole document — copying a paragraph out of the middle
    /// must not paste the document back in.
    /// </remarks>
    internal DataTransfer BuildCopyData(string text) =>
        RichTextClipboard.Build(SelectedDocument(), Formats.OfType<IDocumentExporter>(), text);

    /// <summary>The selection as a document in its own right.</summary>
    internal DocumentNode SelectedDocument()
    {
        var selection = _state.Selection;

        // Cutting the range a rectangle bounds would take every cell between its first and its
        // last, which for a column selection is the whole table. The cells are rebuilt instead.
        if (selection is CellSelection rectangle)
        {
            return CellDocument(rectangle);
        }

        var slice = Slice.Cut(_state.Doc, selection.From, selection.To);

        return new DocumentNode(slice.Content.OfType<BlockNode>());
    }

    /// <summary>Puts the selection on the clipboard and removes it from the document.</summary>
    /// <returns><see langword="true"/> if anything was cut.</returns>
    public async Task<bool> CutAsync()
    {
        // The range is settled before the await, and the delete refuses if the selection moved
        // while the clipboard was busy. Awaiting first and deleting "the selection" afterwards
        // cuts whatever the user happened to select in the meantime.
        var origin = CurrentOrigin();

        // Read-only is checked here and not only in the delete. The copy runs first, so gating
        // the delete alone left a read-only cut writing the clipboard and leaving the document
        // alone -- half of the operation, with nothing to tell the user which half.
        if (IsReadOnly || _state.Selection.IsEmpty || !await CopyAsync().ConfigureAwait(true))
        {
            return false;
        }

        return DeleteCutRange(origin);
    }

    /// <summary>
    /// Removes the range a cut was started on, refusing if the selection moved meanwhile.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="CutAsync"/> because it is the only part of a cut that can be
    /// tested: the headless clipboard completes synchronously, so no test can drive the await
    /// this exists to defend.
    /// </para>
    /// <para>
    /// The same <see cref="StillAt"/> test a paste uses, document included. Comparing offsets
    /// alone let a cut delete from a document the host had swapped in during the await, whenever
    /// its selection happened to sit at the same numbers.
    /// </para>
    /// </remarks>
    internal bool DeleteCutRange(EditOrigin origin) => StillAt(origin) && DeleteSelection();

    /// <summary>The document and selection an asynchronous edit was started against.</summary>
    /// <remarks>
    /// The document is held by reference deliberately. Documents are immutable, so a different
    /// object is a different document, and comparing offsets alone would let a paste land in a
    /// document that had been swapped underneath it for another with a selection at the same
    /// numbers — which is exactly the case a clipboard manager or a tabbed host produces.
    /// </remarks>
    internal readonly record struct EditOrigin(DocumentNode Doc, int From, int To);

    /// <summary>Where an edit starting now would land.</summary>
    internal EditOrigin CurrentOrigin() => new(_state.Doc, _state.Selection.From, _state.Selection.To);

    /// <summary>Whether the document and selection are still the ones an edit began on.</summary>
    /// <param name="origin">What <see cref="CurrentOrigin"/> returned before the first await.</param>
    /// <remarks>
    /// Separate and internal for the same reason <see cref="DeleteCutRange"/> is: the headless
    /// clipboard completes synchronously, so no test can drive the await this exists to defend.
    /// The decision can be tested even though the race cannot be staged.
    /// </remarks>
    internal bool StillAt(EditOrigin origin) =>
        ReferenceEquals(_state.Doc, origin.Doc)
        && _state.Selection.From == origin.From
        && _state.Selection.To == origin.To;

    /// <summary>Replaces the selection with the clipboard's richest readable flavour.</summary>
    /// <returns><see langword="true"/> if the document changed.</returns>
    /// <remarks>
    /// <para>
    /// Ctrl+V starts this without awaiting it, so everything after the first await runs at a
    /// moment the caller no longer controls. The origin is settled before that await and checked
    /// again at each point the document would be changed: a paste that arrives to find a
    /// different document, or a moved caret, is dropped rather than applied somewhere the user
    /// did not ask for. This is the guard <see cref="DeleteCutRange"/> already gives a cut.
    /// </para>
    /// <para>
    /// The importers run on a background thread (see <see cref="IDocumentImporter"/>) and the
    /// result is applied back here, on the UI thread, behind the same origin check. A newer paste
    /// or drop, or the host loading another document, cancels an import still running.
    /// </para>
    /// <para>
    /// Only reading and decoding are guarded. An exception from a <see cref="ContentImporting"/>
    /// or <see cref="ContentImported"/> handler is the host's own and reaches it: swallowing one
    /// after the paste had landed returned <see langword="false"/> for an edit that happened.
    /// </para>
    /// </remarks>
    public async Task<bool> PasteAsync()
    {
        if (!CanPasteHere() || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        var origin = CurrentOrigin();
        var cancellation = BeginImport();
        TransferResolution resolution;

        try
        {
            using var data = await clipboard.TryGetDataAsync().ConfigureAwait(true);

            if (data is null)
            {
                return false;
            }

            resolution = await RichTextClipboard
                .ResolveAsync(TransferSource.From(data), ReadableFormats(), cancellation)
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            // A clipboard owned by another process can fail for reasons none of which are this
            // control's business, and a cancelled import is a paste that was superseded. Losing a
            // paste is bad; taking the application down is worse.
            return false;
        }

        return await Insert(resolution, origin, cancellation).ConfigureAwait(true);
    }

    /// <summary>Inserts what a paste or a drop resolved to, if the editor is still where it was.</summary>
    /// <remarks>
    /// Two checks, because they answer different questions. The token says whether anything has
    /// since abandoned this import — a newer paste, the host loading a document, the view being
    /// retemplated away — and an importer that ignores cancellation still hands back a result.
    /// The origin says whether the document and selection are the ones it started against.
    /// </remarks>
    private async Task<bool> Insert(
        TransferResolution resolution, EditOrigin origin, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested || !StillAt(origin))
        {
            return false;
        }

        return resolution switch
        {
            { Document: { } document } => PasteImport(document, origin, gesture: true),
            { ImagePng: { } png } => InsertImage(png, "image/png"),
            { ImageFiles: { } files } => await InsertImageFiles(files, origin).ConfigureAwait(true),
            { Text: { } text } => PasteImportedText(text, origin, gesture: true),
            _ => false,
        };
    }

    private CancellationTokenSource? _importCancellation;

    /// <summary>Starts an import, cancelling any still running.</summary>
    /// <remarks>
    /// One at a time: a second Ctrl+V while a large first one decodes means the user wants the
    /// second, and the first's result would be refused by its origin check anyway.
    /// </remarks>
    private CancellationToken BeginImport()
    {
        CancelImport();

        _importCancellation = new CancellationTokenSource();

        return _importCancellation.Token;
    }

    /// <summary>Cancels an import still running, if there is one.</summary>
    /// <remarks>
    /// Internal for the editor shell, which abandons the old view's import when a new template
    /// replaces it.
    /// </remarks>
    internal void CancelImport()
    {
        if (_importCancellation is { } running)
        {
            _importCancellation = null;
            running.Cancel();
            running.Dispose();
        }
    }

    /// <summary>Replaces the selection with the clipboard's plain text, ignoring richer flavours.</summary>
    /// <returns><see langword="true"/> if the document changed.</returns>
    /// <remarks>
    /// Ctrl+Shift+V. Not <see cref="PasteAsync"/> with the importers skipped: the point of the
    /// gesture is that the text arrives with the formatting of where it lands, so it goes through
    /// <see cref="PasteText"/>, which is the same path a plain-text-only clipboard already takes
    /// and which normalizes line endings and removes the reserved embed placeholder. Reported as
    /// an import for the same reason that path is — it is still content from outside.
    /// </remarks>
    public async Task<bool> PasteTextAsync()
    {
        if (!CanPasteHere() || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return false;
        }

        var origin = CurrentOrigin();
        string? text;

        try
        {
            using var data = await clipboard.TryGetDataAsync().ConfigureAwait(true);

            if (data is null)
            {
                return false;
            }

            text = await data.TryGetTextAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Same reasoning as PasteAsync: a clipboard owned by another process can fail for
            // reasons that are not this control's business. Only the read is guarded.
            return false;
        }

        return text is { Length: > 0 } && StillAt(origin) && PasteImportedText(text, origin, gesture: true);
    }

    /// <summary>Whether a paste could land at all, asked before anything is read.</summary>
    /// <remarks>
    /// Both conditions were already refused at apply time, but only after the clipboard had been
    /// read and every registered importer had parsed it: a read-only viewer was exactly as exposed
    /// to hostile clipboard content as an editor, for a paste that could never land. The apply-time
    /// checks stay, because the state can change across the awaits.
    /// </remarks>
    private bool CanPasteHere() => !IsReadOnly && Rectangle is null;

    /// <summary>The registered importers, richest first.</summary>
    /// <remarks>
    /// Internal because the order is the whole behaviour: an application that copies from Word
    /// gets RTF, HTML and plain text at once, and taking whichever answers first would silently
    /// paste the poorest of the three.
    /// </remarks>
    internal IEnumerable<IDocumentImporter> ReadableFormats() =>
        RichTextClipboard.InPreferenceOrder(Formats.OfType<IDocumentImporter>());

    /// <summary>Pastes plain text that arrived from outside, and reports it as an import.</summary>
    /// <remarks>
    /// Not routed through <see cref="PasteImport(ImportResult)"/>, which would have to build a document out of
    /// the lines first: <see cref="PasteText"/> normalizes the line breaks and takes out the
    /// reserved embed placeholder, and <c>DocumentNode.FromParagraphs</c> does neither — it throws
    /// on the placeholder, into the broad handler above, which is how a whole clip once pasted
    /// nothing. There are no diagnostics because there is no importer; plain text is still content
    /// from outside, and a host clearing a previous notice needs to hear about it.
    /// </remarks>
    private bool PasteImportedText(string text, EditOrigin origin, bool gesture)
    {
        if (TextSlice(text) is not { } slice || !WouldPasteSlice(slice))
        {
            return false;
        }

        // Shown as the document it will become rather than as the string it arrived as, so a
        // policy written once works the same on both seams.
        if (!ContentAllowed(new DocumentNode(TextParagraphs(text)), [], gesture))
        {
            return false;
        }

        if (!StillAt(origin) || !PasteText(text))
        {
            return false;
        }

        RaiseContentImported([]);

        return true;
    }

    /// <summary>Replaces the selection with <paramref name="doc"/>'s blocks.</summary>
    /// <param name="doc">The document to insert.</param>
    /// <returns><see langword="true"/> if the document changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="doc"/> is <see langword="null"/>.</exception>
    public bool PasteDocument(DocumentNode doc)
    {
        ArgumentNullException.ThrowIfNull(doc);

        return PasteTransaction(doc) is { } transaction && Apply(transaction);
    }

    /// <summary>Whether pasting <paramref name="doc"/> would change the document.</summary>
    /// <remarks>
    /// Building a transaction does not install one — <see cref="Apply"/> is the only thing that
    /// does — so this answers the question without an edit and without an undo entry. It exists
    /// for the one caller with an irreversible side effect to arrange first: see
    /// <see cref="InsertImage"/>.
    /// </remarks>
    internal bool WouldPaste(DocumentNode doc) =>
        !IsReadOnly && PasteTransaction(doc) is { ChangedDoc: true };

    /// <summary>Whether pasting <paramref name="slice"/> would change the document.</summary>
    /// <remarks>The <see cref="WouldPaste"/> question for the plain-text seam, which builds a
    /// slice directly rather than going through <see cref="PasteTransaction"/>.</remarks>
    private bool WouldPasteSlice(Slice slice) =>
        !IsReadOnly
        && SliceTransaction(slice, _state.Selection.From, _state.Selection.To) is { ChangedDoc: true };

    /// <summary>The transaction pasting <paramref name="doc"/>'s blocks would apply, or null.</summary>
    private Transaction? PasteTransaction(DocumentNode doc)
    {
        if (doc.Blocks.IsEmpty || Rectangle is not null)
        {
            return null;
        }

        // Open ends are what make the first and last pasted paragraphs merge into the paragraph
        // the caret sits in, instead of pushing it apart — but only a paragraph can be entered
        // that way. Content that begins or ends with something else gets an empty open paragraph
        // at that end, which does two jobs at once: it splits the host paragraph so a table or a
        // list has somewhere to land, and it carries the text on that side of the caret back out.
        // Without it the step is simply refused, and the paste is lost in silence.
        var blocks = doc.Blocks;

        if (blocks[0] is not ParagraphNode)
        {
            blocks = blocks.Insert(0, Empty);
        }

        if (blocks[^1] is not ParagraphNode)
        {
            blocks = blocks.Add(Empty);
        }

        // Merging is right when there is something to merge with: pasting a word into a sentence
        // must not bring the source paragraph's heading style along. Into an empty paragraph there
        // is nothing to preserve, and merging would instead throw the pasted block's own kind
        // away — pasting a heading and getting body text. So that case replaces the host outright,
        // with the original blocks: the padding above exists only to open a slice, and a closed
        // one would keep it as a stray empty paragraph.
        return EmptyHostBlock() is { } host
            ? SliceTransaction(new Slice([.. doc.Blocks], 0, 0), host.From, host.To)
            : SliceTransaction(
                new Slice([.. blocks], 1, 1), _state.Selection.From, _state.Selection.To);
    }

    /// <summary>
    /// The range of the block the selection sits in, when that block is an empty paragraph and so
    /// has nothing worth merging into.
    /// </summary>
    private (int From, int To)? EmptyHostBlock()
    {
        var selection = _state.Selection;

        if (!selection.IsEmpty)
        {
            return null;
        }

        var at = _state.Doc.Resolve(selection.From);

        if (at.Depth < 1 || at.NodeAt(at.Depth) is not ParagraphNode { Content.Text.Length: 0 })
        {
            return null;
        }

        return (at.Before(at.Depth), at.After(at.Depth));
    }

    private static ParagraphNode Empty => new(InlineContent.Empty);

    /// <summary>Replaces the selection with <paramref name="text"/>, honouring line breaks.</summary>
    /// <param name="text">The text to insert.</param>
    /// <returns><see langword="true"/> if the document changed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public bool PasteText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return TextSlice(text) is { } slice && PasteSlice(slice);
    }

    /// <summary>The paragraphs <paramref name="text"/> would become.</summary>
    /// <remarks>
    /// Shared with <see cref="PasteImportedText"/> so that what a host is shown and what lands
    /// cannot drift, and built out of <see cref="RichTextClipboard.Lines"/> so that a paste and a
    /// drop of one clip cannot either: line endings normalized the same way, and the reserved
    /// placeholder taken out by the same rule.
    /// </remarks>
    private static ImmutableArray<BlockNode> TextParagraphs(string text) =>
        RichTextClipboard.Lines(text)
            .Select(line => (BlockNode)new ParagraphNode(InlineContent.FromText(line)))
            .ToImmutableArray();

    /// <summary>The slice pasting <paramref name="text"/> would insert, or null if there is none.</summary>
    private Slice? TextSlice(string text)
    {
        if (text.Length == 0 || Rectangle is not null)
        {
            return null;
        }

        // One transaction, so that one undo takes the paste back. Splitting and inserting line by
        // line reads more simply and is wrong: it leaves the user pressing Ctrl+Z once per line.
        // Open on both ends is what makes the first and last pasted paragraphs merge into the
        // paragraph the caret was sitting in, rather than pushing it apart.
        return new Slice(TextParagraphs(text).CastArray<Node>(), 1, 1);
    }

    /// <summary>Replaces the selection with <paramref name="slice"/>.</summary>
    /// <remarks>
    /// One transaction, so that one undo takes the paste back. Splitting and inserting block by
    /// block reads more simply and is wrong: it leaves the user pressing Ctrl+Z once per block.
    /// </remarks>
    private bool PasteSlice(Slice slice) =>
        SliceTransaction(slice, _state.Selection.From, _state.Selection.To) is { } transaction
            && Apply(transaction);

    /// <summary>
    /// The transaction replacing <paramref name="from"/>..<paramref name="to"/> with
    /// <paramref name="slice"/> would apply, or null if it cannot be built at all.
    /// </summary>
    private Transaction? SliceTransaction(Slice slice, int from, int to)
    {
        var transaction = _state.Transaction().As(TransactionKind.Paste);

        // A range whose ends are at different depths is not one replacement, so the removal has
        // to happen first and the slice go into the caret it leaves. Doing this only when the
        // depths differ keeps the ordinary paste one step, which is what makes its open edges
        // join to what surrounds them rather than landing as separate blocks.
        if (from < to && _state.Doc.Resolve(from).Depth != _state.Doc.Resolve(to).Depth)
        {
            transaction.DeleteRange(from, to);

            if (!transaction.Failures.IsEmpty)
            {
                return null;
            }

            to = from;
        }

        transaction
            .Replace(from, to, slice)
            .SetSelection(TextSelection.Cursor(from + slice.Size))
            .SetStoredMarks(null);

        return transaction;
    }

    /// <summary>Removes the selection, leaving a caret where it was.</summary>
    /// <returns><see langword="true"/> if the document changed.</returns>
    public bool DeleteSelection()
    {
        var selection = _state.Selection;

        if (selection.IsEmpty)
        {
            return false;
        }

        // Emptying the cells, not removing them: deleting the range a rectangle bounds takes
        // whole cells out of their rows, and for a column selection takes cells beside it too.
        if (selection is CellSelection rectangle)
        {
            return ClearCells(rectangle);
        }

        var transaction = _state.Transaction().As(TransactionKind.Structure);

        transaction
            .DeleteRange(selection.From, selection.To)
            .SetSelection(TextSelection.Cursor(selection.From))
            .SetStoredMarks(null);

        return Apply(transaction);
    }
}