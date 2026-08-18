using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Dragging files and content into the editor.
/// </summary>
/// <remarks>
/// <para>
/// A drop is a paste with a longer arm: it reads files off disk, so it awaits, so anything the
/// host does in the meantime happens underneath it. The paste path cannot be made to show that —
/// the headless clipboard completes synchronously and there is no await to interleave with — but
/// a dropped file is opened through <see cref="IStorageFile"/>, which the test supplies, and a
/// fake that changes the document from inside <c>OpenReadAsync</c> stages the race exactly.
/// </para>
/// <para>
/// Both defects these were written for are here: a drop landing in whatever document the host had
/// switched to while the file was being read, and an image written to the store before the edit it
/// belonged to was known to succeed, which left the store holding something nothing referenced.
/// </para>
/// </remarks>
public class RichTextViewDropTests
{
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize(width, height));
        using var buffer = new MemoryStream();

        bitmap.Save(buffer, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return buffer.ToArray();
    }

    private static RichTextView Shown(params string[] paragraphs)
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return view;
    }

    /// <summary>An <see cref="IImageStore"/> that remembers what it was asked to keep.</summary>
    private sealed class RecordingStore(int refuseOver = int.MaxValue) : IImageStore
    {
        public List<int> Saved { get; } = [];

        public string? Save(ReadOnlySpan<byte> bytes, string mediaType)
        {
            if (bytes.Length > refuseOver)
            {
                return null;
            }

            Saved.Add(bytes.Length);

            return $"stored:{Saved.Count}";
        }
    }

    /// <summary>A dropped file that never touches the disk.</summary>
    /// <remarks>
    /// <para>
    /// A proxy rather than a class, because <see cref="IStorageFile"/> carries a member no C#
    /// source can name — Avalonia's way of saying the interface is not for outside implementers.
    /// A generated proxy has no such trouble, and none of the drop path asks for more than the
    /// name and a stream.
    /// </para>
    /// <para>
    /// <c>opening</c> runs inside the await the drop path takes to read the file, which is the one
    /// moment the document can change out from under a drop that is already in flight.
    /// </para>
    /// </remarks>
    public class DroppedFile : DispatchProxy
    {
        private string _name = string.Empty;
        private byte[]? _bytes;
        private Action? _opening;

        public static IStorageFile Named(string name, byte[]? bytes, Action? opening = null)
        {
            var file = DispatchProxy.Create<IStorageFile, DroppedFile>();
            var proxy = (DroppedFile)file;

            proxy._name = name;
            proxy._bytes = bytes;
            proxy._opening = opening;

            return file;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name switch
            {
                "get_Name" => _name,
                "get_Path" => new Uri("file:///" + _name),
                "get_CanBookmark" => false,
                "OpenReadAsync" => Open(),
                _ => null,
            };

        private async Task<Stream> Open()
        {
            // Genuinely asynchronous, so the continuation is queued rather than run inline and the
            // hook below lands where a real file read's latency would.
            await Task.Yield();

            _opening?.Invoke();

            return _bytes is null
                ? throw new IOException("the volume went away")
                : new MemoryStream(_bytes);
        }
    }

    private static DataTransfer Files(params IStorageItem[] files)
    {
        var data = new DataTransfer();

        foreach (var file in files)
        {
            var item = new DataTransferItem();

            item.SetFile(file);
            data.Add(item);
        }

        return data;
    }

    private static DataTransfer Transfer(string format, string payload)
    {
        var flavour = RichTextClipboard.FlavorFor(format);
        var item = new DataTransferItem();

        item.Set(flavour.DataFormat, Encoding.UTF8.GetBytes(payload));

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    /// <summary>Raises a drop and runs it to completion.</summary>
    /// <remarks>
    /// <c>OnDrop</c> is <c>async void</c>, so raising the event only starts it. The dispatcher has
    /// to be pumped until the continuations behind each file read have run, or every assertion
    /// below would be made against a drop that had not happened yet — and would pass for a
    /// control that does nothing at all.
    /// </remarks>
    private static DragEventArgs Drop(RichTextView view, DataTransfer data)
    {
        var e = new DragEventArgs(DragDrop.DropEvent, data, view, new Point(4, 4), KeyModifiers.None);

        view.RaiseEvent(e);

        for (var i = 0; i < 64; i++)
        {
            Dispatcher.UIThread.RunJobs();
        }

        return e;
    }

    private static List<ImageEmbed> ImagesIn(RichTextView view) =>
        [.. view.State.Doc.Blocks.OfType<ParagraphNode>()
            .SelectMany(p => p.Content.Embeds)
            .OfType<ImageEmbed>()];

    [AvaloniaFact]
    public void AMultiFileDropInsertsEveryImageItCanRead()
    {
        // The control every test below needs: without it a drop path that silently did nothing
        // would satisfy all of them.
        var view = Shown("text");
        var store = new RecordingStore();

        view.ImageStore = store;

        Drop(view, Files(
            DroppedFile.Named("a.png", Png(2, 1)),
            DroppedFile.Named("b.png", Png(3, 1)),
            DroppedFile.Named("c.png", Png(4, 1))));

        Assert.Equal(3, ImagesIn(view).Count);
        Assert.Equal(3, store.Saved.Count);
    }

    [AvaloniaFact]
    public void AMultiFileDropKeepsGoingAfterOneFileIsRejected()
    {
        // Dropping a folder's worth of pictures where one is locked, on a volume that went away,
        // or is not an image at all must not cost the user the other five.
        var view = Shown("text");
        var store = new RecordingStore();

        view.ImageStore = store;

        Drop(view, Files(
            DroppedFile.Named("first.png", Png(2, 1)),
            DroppedFile.Named("gone.png", bytes: null),
            DroppedFile.Named("notes.txt", [1, 2, 3]),
            DroppedFile.Named("last.png", Png(4, 1))));

        // The two that could be read, and specifically the one after the failure: a drop that
        // stopped at the first bad file would leave one image and pass a count-only assertion
        // that said "some arrived".
        Assert.Equal(
            [Png(2, 1).Length, Png(4, 1).Length],
            store.Saved);

        Assert.Equal(["stored:1", "stored:2"], ImagesIn(view).Select(i => i.Source));
    }

    [AvaloniaFact]
    public void AMultiFileDropStopsWhenTheDocumentIsSwappedUnderneathIt()
    {
        // The defect: a drop of several files takes long enough for the host to switch documents,
        // and every file after that point landed in whichever one was showing when its read
        // finished. The user dropped pictures on one file and found them in another.
        var view = Shown("first document");
        var store = new RecordingStore();
        var swapped = EditorState.Create(DocumentNode.FromParagraphs("second document"));

        view.ImageStore = store;

        Drop(view, Files(
            DroppedFile.Named("before.png", Png(2, 1)),
            DroppedFile.Named("during.png", Png(3, 1), opening: () => view.State = swapped),
            DroppedFile.Named("after.png", Png(4, 1))));

        Assert.Same(swapped.Doc, view.State.Doc);
        Assert.Empty(ImagesIn(view));

        // The first file was inserted before the swap, into the document that is now gone, and it
        // is the only one the store was ever asked about.
        Assert.Equal([Png(2, 1).Length], store.Saved);
    }

    [AvaloniaFact]
    public void ASingleFileDropThatArrivesAfterTheSwapLandsNowhereAndStoresNothing()
    {
        var view = Shown("first document");
        var store = new RecordingStore();
        var swapped = EditorState.Create(DocumentNode.FromParagraphs("second document"));

        view.ImageStore = store;

        Drop(view, Files(DroppedFile.Named("late.png", Png(2, 1), opening: () => view.State = swapped)));

        Assert.Same(swapped.Doc, view.State.Doc);
        Assert.Empty(ImagesIn(view));

        // The other defect, and the reason the check comes before the save rather than after: a
        // store cannot be untold, so anything written for an edit that never happened stays.
        Assert.Empty(store.Saved);
    }

    [AvaloniaFact]
    public void ADropThatMovedTheCaretIsStillRefusedIfTheDocumentChanged()
    {
        // The origin is the document by reference, not its text: a host that replaced the document
        // with an identical one is still a host that switched files, and offsets alone cannot see
        // the difference.
        var view = Shown("text");
        var store = new RecordingStore();
        var identical = EditorState.Create(DocumentNode.FromParagraphs("text"));

        view.ImageStore = store;

        Drop(view, Files(DroppedFile.Named("late.png", Png(2, 1), opening: () => view.State = identical)));

        Assert.Empty(ImagesIn(view));
        Assert.Empty(store.Saved);
    }

    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ADropOntoAnUnwillingEditorNeverReachesTheStore(bool readOnly, bool optedOut)
    {
        var view = Shown("text");
        var store = new RecordingStore();
        var before = view.State.Doc;
        var reported = 0;

        view.ImageStore = store;
        view.ContentImported += (_, _) => reported++;
        view.IsReadOnly = readOnly;
        view.AcceptsDrop = !optedOut;

        var e = Drop(view, Files(DroppedFile.Named("a.png", Png(2, 1))));

        Assert.Same(before, view.State.Doc);
        Assert.Empty(store.Saved);
        Assert.Equal(0, reported);
        Assert.False(e.Handled);
        Assert.Equal(DragDropEffects.None, e.DragEffects);
    }

    [AvaloniaFact]
    public void ADroppedImageTheStoreRefusesSaysSoRatherThanVanishing()
    {
        var view = Shown("text");
        var refused = new List<RichTextView.ImageRefusedEventArgs>();
        var before = view.State.Doc;

        view.ImageStore = new RecordingStore(refuseOver: 4);
        view.ImageRefused += (_, e) => refused.Add(e);

        Drop(view, Files(DroppedFile.Named("big.png", Png(8, 8))));

        Assert.Same(before, view.State.Doc);

        var report = Assert.Single(refused);

        Assert.Equal(Png(8, 8).Length, report.ByteCount);
        Assert.Equal("image/png", report.MediaType);
    }

    [AvaloniaFact]
    public void ADropOfContentTheSanitiserRejectsBringsNothingAcrossAndSaysSo()
    {
        // A drop is the likeliest way hostile markup arrives: dragging a selection out of a
        // browser hands over that page's HTML verbatim.
        var view = Shown("text");
        var seen = new List<RichTextView.ContentImportedEventArgs>();

        view.Formats.Add(HtmlFormat.Instance);
        view.ContentImported += (_, e) => seen.Add(e);

        Drop(view, Transfer(
            "html",
            "<p>kept<script>alert(1)</script></p><p><a href='javascript:alert(2)'>link</a></p>"));

        var text = DocumentText.Of(view.State.Doc);

        Assert.Contains("kept", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text, StringComparison.Ordinal);

        // The link's text survives; its address does not, which is the part that matters and the
        // part that asserting on the text alone would miss.
        Assert.Contains("link", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            view.State.Doc.Blocks.OfType<ParagraphNode>()
                .SelectMany(p => Enumerable.Range(0, p.Content.Length).Select(i => p.Content.MarkAt(i)))
                .Select(m => m.Link?.Href),
            href => href is not null);

        var reported = Assert.Single(seen);

        Assert.True(reported.AnythingDropped);
        Assert.Contains(reported.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped);
    }

    [AvaloniaFact]
    public void ADropThatGoesReadOnlyMidFlightLeavesNothingInTheStore()
    {
        // The other defect: the store was written first and the edit applied second, so an edit
        // the document then refused left behind bytes nothing references, and IImageStore has no
        // way to take one back. Reading a file is exactly long enough for a host to lock the
        // editor, and unlike the read-only drop above this one is already past that guard.
        var view = Shown("text");
        var store = new RecordingStore();
        var before = view.State.Doc;

        view.ImageStore = store;

        Drop(view, Files(DroppedFile.Named("a.png", Png(2, 1), opening: () => view.IsReadOnly = true)));

        Assert.Same(before, view.State.Doc);
        Assert.Empty(store.Saved);
    }

    [AvaloniaFact]
    public void ADropWhoseWholePayloadIsRejectedStillSaysSoRatherThanLookingClean()
    {
        // A payload with nothing left after sanitising is the case where a host is most likely to
        // be told an import went fine: no text changed, so there is nothing to notice. It has to
        // report the loss anyway, or "paste succeeded, document unchanged" becomes indistinguishable
        // from a drop that was silently emptied. The document is not asserted reference-unchanged:
        // an import that sanitises down to an empty paragraph still pastes that paragraph, which is
        // the paste path's own behaviour and not this test's business.
        var view = Shown("text");
        var seen = new List<RichTextView.ContentImportedEventArgs>();

        view.Formats.Add(HtmlFormat.Instance);
        view.ContentImported += (_, e) => seen.Add(e);

        Drop(view, Transfer("html", "<script>alert(1)</script>"));

        Assert.Equal("text", DocumentText.Of(view.State.Doc));

        var reported = Assert.Single(seen);

        Assert.True(reported.AnythingDropped);
        Assert.Contains(reported.Diagnostics, d => d.Severity == DiagnosticSeverity.Dropped);
    }
}
