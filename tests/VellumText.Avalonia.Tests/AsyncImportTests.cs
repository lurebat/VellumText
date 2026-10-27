using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Imports run off the UI thread and land only where they started (Phase 7, OD-2; F-10, F-41).
/// </summary>
/// <remarks>
/// <para>
/// The importer API became asynchronous so that a large paste stops freezing the window, and the
/// editor now always hands an importer to a background thread — even one whose own implementation
/// does its work before its first await, which is what <see cref="Inline"/> below is. That makes
/// the race between an import and the user (or the host) real rather than theoretical, and these
/// tests stage it with importers the test releases by hand.
/// </para>
/// </remarks>
public class AsyncImportTests
{
    private const string Png =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

    private static RichTextView Shown(DocumentNode doc, params IDocumentFormat[] formats)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        foreach (var format in formats)
        {
            view.Formats.Add(format);
        }

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));
        view.MoveTo(1, extend: false);

        return view;
    }

    private static RichTextView Shown(params IDocumentFormat[] formats) =>
        Shown(DocumentNode.FromParagraphs("hello"), formats);

    private static DataTransfer Transfer(string format, string payload)
    {
        var item = new DataTransferItem();

        item.Set(RichTextClipboard.FlavorFor(format).DataFormat, Encoding.UTF8.GetBytes(payload));

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    private static DataTransfer TextTransfer(string text)
    {
        var item = new DataTransferItem();

        item.SetText(text);

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    private static DataTransferItem BitmapItem()
    {
        var item = new DataTransferItem();

        item.SetBitmap(new Bitmap(new MemoryStream(Convert.FromBase64String(Png))));

        return item;
    }

    private static async Task Put(RichTextView view, DataTransfer data) =>
        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(data);

    private static async Task Drop(RichTextView view, DataTransfer data)
    {
        view.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, view, new Point(4, 4), KeyModifiers.None));

        await view.PendingDrop;
    }

    private static List<ImageEmbed> Images(RichTextView view) =>
        [.. view.State.Doc.Blocks.OfType<ParagraphNode>().SelectMany(p => p.Content.Embeds).OfType<ImageEmbed>()];

    /// <summary>
    /// An importer that does all its work synchronously before returning a completed task — the
    /// shape every synchronous parser wrapped in a task has, and the one a host is likeliest to
    /// write. Awaiting it alone would run it on the caller's thread.
    /// </summary>
    private sealed class Inline(string format = "inline") : IDocumentImporter
    {
        public int? ThreadId { get; private set; }

        public string Format => format;

        public string MediaType => "text/x-" + format;

        public Encoding? TextEncoding => Encoding.UTF8;

        public Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
        {
            ThreadId = Environment.CurrentManagedThreadId;

            using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);

            return Task.FromResult(new ImportResult(DocumentNode.FromParagraphs(reader.ReadToEnd())));
        }
    }

    /// <summary>An importer that waits for the test to release it, ignoring cancellation.</summary>
    /// <param name="format">Its format name.</param>
    /// <param name="lostAnImage">Whether its result says the sanitizer removed an image.</param>
    /// <param name="empty">Whether its result is an empty document, so the paste falls back to text.</param>
    private sealed class Gated(string format = "gated", bool lostAnImage = false, bool empty = false) : IDocumentImporter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Format => format;

        public string MediaType => "text/x-" + format;

        public Encoding? TextEncoding => Encoding.UTF8;

        public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
        {
            using var reader = new StreamReader(source, Encoding.UTF8, leaveOpen: true);
            var text = await reader.ReadToEndAsync(CancellationToken.None);

            Started.TrySetResult();
            await Release.Task;

            if (empty)
            {
                return new ImportResult(new DocumentNode([]));
            }

            return lostAnImage
                ? new ImportResult(
                    DocumentNode.FromParagraphs(text),
                    [new ImportDiagnostic(DiagnosticSeverity.Dropped, "remote image", "img")])
                : new ImportResult(DocumentNode.FromParagraphs(text));
        }
    }

    /// <summary>An importer that blocks its thread until released, as a slow parser does.</summary>
    private sealed class Blocking : IDocumentImporter
    {
        public ManualResetEventSlim Gate { get; } = new();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public volatile bool Finished;

        public string Format => "blocking";

        public string MediaType => "text/x-blocking";

        public Encoding? TextEncoding => Encoding.UTF8;

        public Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();

            // Bounded, so that an editor calling this on its own thread fails the test instead of
            // hanging it.
            Gate.Wait(TimeSpan.FromSeconds(5));
            Finished = true;

            return Task.FromResult(new ImportResult(DocumentNode.FromParagraphs("slow")));
        }
    }

    // ---- 7.2: always off the UI thread ------------------------------------------------------

    [AvaloniaFact]
    public async Task A_paste_runs_even_a_synchronous_importer_off_the_ui_thread()
    {
        var ui = Environment.CurrentManagedThreadId;
        var importer = new Inline();
        var view = Shown(importer);

        await Put(view, Transfer("inline", "pasted"));

        Assert.True(await view.PasteAsync());
        Assert.NotNull(importer.ThreadId);
        Assert.NotEqual(ui, importer.ThreadId);
    }

    [AvaloniaFact]
    public async Task A_drop_runs_the_importer_off_the_ui_thread_too()
    {
        var ui = Environment.CurrentManagedThreadId;
        var importer = new Inline();
        var view = Shown(importer);

        await Drop(view, Transfer("inline", "dropped"));

        Assert.Contains("dropped", DocumentText.Of(view.State.Doc), StringComparison.Ordinal);
        Assert.NotEqual(ui, importer.ThreadId);
    }

    /// <summary>
    /// The point of the change: while a slow import runs, the UI thread keeps processing work.
    /// Measured by running a dispatcher job while the importer is still blocked, not by time.
    /// </summary>
    [AvaloniaFact]
    public async Task The_ui_thread_keeps_working_while_an_import_runs()
    {
        var importer = new Blocking();
        var view = Shown(importer);

        await Put(view, Transfer("blocking", "x"));

        var paste = view.PasteAsync();

        await importer.Started.Task;

        var ranWhileBlocked = await Dispatcher.UIThread.InvokeAsync(() => !importer.Finished);

        importer.Gate.Set();

        Assert.True(await paste);
        Assert.True(ranWhileBlocked, "the UI thread only ran its job after the import had finished");
    }

    // ---- 7.2 and 7.3: an import lands only where it started -----------------------------------

    [AvaloniaFact]
    public async Task A_document_swapped_during_an_import_receives_nothing()
    {
        var importer = new Gated();
        var view = Shown(importer);
        var swapped = EditorState.Create(DocumentNode.FromParagraphs("another document"));

        await Put(view, Transfer("gated", "pasted"));

        var paste = view.PasteAsync();

        await importer.Started.Task;
        view.State = swapped;
        importer.Release.SetResult();

        Assert.False(await paste);
        Assert.Same(swapped.Doc, view.State.Doc);
    }

    [AvaloniaFact]
    public async Task A_caret_moved_during_an_import_receives_nothing()
    {
        // The same document: only the origin check stands between the result and the new caret.
        var importer = new Gated();
        var view = Shown(importer);

        await Put(view, Transfer("gated", "pasted"));

        var paste = view.PasteAsync();

        await importer.Started.Task;
        view.MoveTo(3, extend: false);

        var before = view.State.Doc;

        importer.Release.SetResult();

        Assert.False(await paste);
        Assert.Same(before, view.State.Doc);
    }

    /// <summary>
    /// The image path has no apply step of its own to re-check the origin, so the check before
    /// inserting is all that stands between a late image and a caret the user has since moved.
    /// </summary>
    [AvaloniaFact]
    public async Task A_caret_moved_during_an_import_receives_no_image_either()
    {
        var importer = new Gated(lostAnImage: true);
        var view = Shown(importer);
        var data = Transfer("gated", "caption");

        data.Add(BitmapItem());
        await Put(view, data);

        var paste = view.PasteAsync();

        await importer.Started.Task;
        view.MoveTo(3, extend: false);
        importer.Release.SetResult();

        Assert.False(await paste);
        Assert.Empty(Images(view));
    }

    /// <summary>
    /// The same for a drop: the import is judged against where the drop was aimed, not against
    /// wherever the caret is when the importer returns. The unmoved row is the control, showing
    /// this clip does land as an image when nothing moves.
    /// </summary>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_caret_moved_during_a_drop_import_receives_no_image_either(bool moved)
    {
        var importer = new Gated(lostAnImage: true);
        var view = Shown(importer);
        var data = Transfer("gated", "caption");

        data.Add(BitmapItem());
        view.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, view, new Point(4, 4), KeyModifiers.None));

        await importer.Started.Task;

        if (moved)
        {
            view.MoveTo(3, extend: false);
        }

        importer.Release.SetResult();
        await view.PendingDrop;

        Assert.Equal(moved ? 0 : 1, Images(view).Count);
    }

    [AvaloniaFact]
    public async Task A_drop_whose_document_is_swapped_mid_import_receives_nothing()
    {
        var importer = new Gated();
        var view = Shown(importer);
        var swapped = EditorState.Create(DocumentNode.FromParagraphs("another document"));

        view.RaiseEvent(new DragEventArgs(
            DragDrop.DropEvent, Transfer("gated", "dropped"), view, new Point(4, 4), KeyModifiers.None));

        await importer.Started.Task;
        view.State = swapped;
        importer.Release.SetResult();
        await view.PendingDrop;

        Assert.Same(swapped.Doc, view.State.Doc);
    }

    /// <summary>
    /// A control made read-only while an import runs does not ask the host about what it can no
    /// longer accept, whether the import resolved to a document or fell back to plain text.
    /// </summary>
    /// <remarks>
    /// Read-only is checked before the clipboard is read, but an import takes as long as its
    /// importer, and the host can set the property in between. The apply step would refuse the
    /// edit anyway; what these guards decide is whether a <c>ContentImporting</c> handler is asked
    /// to judge content that cannot land. The writable rows are the control: they show the same
    /// clip reaches the handler and lands, by the document path and by the plain-text path.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_control_made_read_only_during_an_import_is_not_asked_about_it(
        bool textFallback, bool readOnly)
    {
        var importer = new Gated(empty: textFallback);
        var view = Shown(importer);
        var before = view.State.Doc;
        var asked = 0;
        var data = Transfer("gated", "from the importer");
        var text = new DataTransferItem();

        text.SetText("plain text");
        data.Add(text);
        view.ContentImporting += (_, _) => asked++;
        await Put(view, data);

        var paste = view.PasteAsync();

        await importer.Started.Task;
        view.IsReadOnly = readOnly;
        importer.Release.SetResult();

        Assert.Equal(!readOnly, await paste);

        if (readOnly)
        {
            Assert.Equal(0, asked);
            Assert.Same(before, view.State.Doc);
        }
        else
        {
            Assert.Equal(1, asked);
            Assert.Contains(
                textFallback ? "plain text" : "from the importer",
                DocumentText.Of(view.State.Doc),
                StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task The_import_events_are_raised_on_the_ui_thread()
    {
        var view = Shown(new Inline());
        bool? importingOnUi = null;
        bool? importedOnUi = null;

        view.ContentImporting += (_, _) => importingOnUi = Dispatcher.UIThread.CheckAccess();
        view.ContentImported += (_, _) => importedOnUi = Dispatcher.UIThread.CheckAccess();

        await Put(view, Transfer("inline", "pasted"));

        Assert.True(await view.PasteAsync());
        Assert.True(importingOnUi);
        Assert.True(importedOnUi);
    }

    [AvaloniaFact]
    public async Task A_veto_still_refuses_an_import_that_ran_in_the_background()
    {
        var view = Shown(new Inline());
        var before = view.State.Doc;

        view.ContentImporting += (_, e) => e.Cancel = true;

        await Put(view, Transfer("inline", "pasted"));

        Assert.False(await view.PasteAsync());
        Assert.Same(before, view.State.Doc);
    }

    /// <summary>
    /// A handler is host code and can do anything — here, load another document. The content was
    /// judged against the state it was shown; it must not land in the one the handler installed.
    /// </summary>
    [AvaloniaFact]
    public async Task A_handler_that_changes_the_document_stops_the_import_landing_in_it()
    {
        var view = Shown(new Inline());
        var replacement = EditorState.Create(DocumentNode.FromParagraphs("replaced by the handler"));

        view.ContentImporting += (_, _) => view.State = replacement;

        await Put(view, Transfer("inline", "pasted"));

        Assert.False(await view.PasteAsync());
        Assert.Same(replacement.Doc, view.State.Doc);
    }

    [AvaloniaFact]
    public async Task A_retemplate_during_an_import_lands_nothing_and_throws_nothing()
    {
        var importer = new Gated();
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var window = new Window { Content = editor, Width = 400, Height = 300 };

        editor.Formats.Add(importer);
        window.Show();

        var old = editor.EnsureView();

        old.MoveTo(1, extend: false);
        await TopLevel.GetTopLevel(editor)!.Clipboard!.SetDataAsync(Transfer("gated", "pasted"));

        var before = editor.State.Doc;
        var paste = old.PasteAsync();

        await importer.Started.Task;

        // A new template is a new view; the import belongs to the one it started in, which is no
        // longer the editor's.
        editor.Template = RetemplateTests.Chrome();
        editor.ApplyTemplate();
        importer.Release.SetResult();

        Assert.False(await paste);
        Assert.NotSame(old, editor.View);
        Assert.Same(before, editor.State.Doc);
        Assert.Same(before, old.State.Doc);
    }

    // ---- 7.4: paste and drop resolve a transfer by one rule (F-10, OD-4b) ------------------

    /// <summary>
    /// A browser's "Copy image" puts an <c>img</c> pointing at the network on the clipboard beside
    /// the decoded bitmap. The sanitizer removes the remote image, and taking the rich flavour
    /// anyway pasted nothing while returning true. The image wins when the rich import lost one.
    /// </summary>
    [AvaloniaFact]
    public async Task Pasting_a_browser_copied_image_inserts_the_image()
    {
        var view = Shown(DocumentNode.FromParagraphs("x"), HtmlFormat.Instance);
        var data = Transfer("html", "<html><body><img src=\"https://example.com/cat.png\" alt=\"cat\"></body></html>");

        data.Add(BitmapItem());
        await Put(view, data);

        Assert.True(await view.PasteAsync());

        var image = Assert.Single(Images(view));

        Assert.StartsWith("data:", image.Source, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Dropping_a_browser_copied_image_inserts_the_image()
    {
        var view = Shown(DocumentNode.FromParagraphs("x"), HtmlFormat.Instance);
        var data = Transfer("html", "<img src=\"https://example.com/cat.png\" alt=\"cat\">");

        data.Add(BitmapItem());
        await Drop(view, data);

        Assert.Single(Images(view));
    }

    /// <summary>
    /// The other half of the rule: a rich flavour that lost nothing beats a bitmap — a spreadsheet
    /// copies a picture of its cells as well as the table, and the table is what was meant. Paste
    /// took the caption and drop the image; both take the caption now.
    /// </summary>
    [AvaloniaFact]
    public async Task Paste_and_drop_prefer_the_same_flavour_when_html_and_an_image_are_offered()
    {
        DataTransfer Both()
        {
            var data = Transfer("html", "<p><b>caption</b></p>");

            data.Add(BitmapItem());

            return data;
        }

        var pasted = Shown(DocumentNode.FromParagraphs("x"), HtmlFormat.Instance);

        await Put(pasted, Both());
        await pasted.PasteAsync();

        var dropped = Shown(DocumentNode.FromParagraphs("x"), HtmlFormat.Instance);

        await Drop(dropped, Both());

        // The same flavour, not the same position: a drop aims the caret at the pointer first.
        Assert.Contains("caption", DocumentText.Of(pasted.State.Doc), StringComparison.Ordinal);
        Assert.Contains("caption", DocumentText.Of(dropped.State.Doc), StringComparison.Ordinal);
        Assert.Empty(Images(pasted));
        Assert.Empty(Images(dropped));
    }

    /// <summary>
    /// Plain text goes through one seam for both gestures, so it lands with the formatting of where
    /// it lands: a drop used to build body paragraphs where a paste kept the heading.
    /// </summary>
    [AvaloniaFact]
    public async Task Pasting_and_dropping_the_same_text_into_an_empty_heading_agree()
    {
        static RichTextView Heading()
        {
            var view = Shown(new DocumentNode([new ParagraphNode(InlineContent.Empty, ParagraphKind.Heading1)]));

            view.MoveTo(1, extend: false);

            return view;
        }

        static string Shape(RichTextView view) =>
            string.Join(",", view.State.Doc.Blocks.OfType<ParagraphNode>().Select(p => $"{p.Kind}:'{p.Content.Text}'"));

        var pasted = Heading();

        await Put(pasted, TextTransfer("hello"));
        Assert.True(await pasted.PasteAsync());

        var dropped = Heading();

        await Drop(dropped, TextTransfer("hello"));

        Assert.Equal(Shape(pasted), Shape(dropped));
        Assert.Equal("Heading1:'hello'", Shape(dropped));
    }

    /// <summary>
    /// F-41: a host's <see cref="RichTextView.ContentImported"/> handler that throws after the
    /// paste landed was swallowed, and the paste reported false for an edit that happened. Only
    /// reading and decoding are guarded now; the host's own exception reaches the host.
    /// </summary>
    [AvaloniaFact]
    public async Task A_handler_exception_after_the_paste_landed_reaches_the_host()
    {
        var view = Shown();
        var before = view.State.Doc;

        view.ContentImported += (_, _) => throw new InvalidOperationException("host bug");

        await Put(view, TextTransfer("pasted"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => view.PasteAsync());
        Assert.NotSame(before, view.State.Doc);
        Assert.Contains("pasted", DocumentText.Of(view.State.Doc), StringComparison.Ordinal);
    }
}
