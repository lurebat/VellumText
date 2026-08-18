using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Getting an image into the document from outside it.
/// </summary>
public class RichTextViewImageInsertTests
{
    private static byte[] Png(int width, int height)
    {
        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new global::Avalonia.PixelSize(width, height));
        using var buffer = new MemoryStream();

        bitmap.Save(buffer, new global::Avalonia.Media.Imaging.PngBitmapEncoderOptions());

        return buffer.ToArray();
    }

    private static RichTextView View() =>
        new() { State = EditorState.Create(DocumentNode.FromParagraphs("text")) };

    [AvaloniaFact]
    public void AnInsertedImageBecomesAnEmbedTheRendererCanResolve()
    {
        // The two halves have to agree: what the store writes, the default renderer must draw.
        var view = View();

        view.SelectAll();

        Assert.True(view.InsertImage(Png(4, 2), "image/png"));

        var embed = Assert.Single(
            view.State.Doc.Blocks.OfType<ParagraphNode>()
                .SelectMany(p => p.Content.Embeds)
                .OfType<ImageEmbed>());

        Assert.StartsWith("data:image/png;base64,", embed.Source);
        Assert.Equal(
            new global::Avalonia.Size(4, 2),
            view.Embeds.Measure(embed, new global::Avalonia.Media.TextFormatting.GenericTextRunProperties(
                new global::Avalonia.Media.Typeface(global::Avalonia.Media.FontFamily.Default), 12)));
    }

    [AvaloniaFact]
    public void AnInsertedImageIsOneUndoAway()
    {
        var view = View();
        var before = view.State.Doc;

        view.InsertImage(Png(2, 2), "image/png");

        Assert.NotEqual(before, view.State.Doc);
        Assert.True(view.Undo());
        Assert.Equal(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void AStoreThatRefusesLeavesTheDocumentAlone()
    {
        // A refusal is not a failure: better a document without the image than one with an embed
        // whose source resolves to nothing.
        var view = View();
        var before = view.State.Doc;

        view.ImageStore = new DataUriImageStore(maximumBytes: 4);

        Assert.False(view.InsertImage(Png(8, 8), "image/png"));
        Assert.Equal(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void ARefusedImageSaysSoRatherThanVanishing()
    {
        // Pasting a photograph into an editor whose store caps at four megabytes did nothing at
        // all - no image, no error, no clue. From the user's side that is indistinguishable from
        // an editor that ignores Ctrl+V.
        var view = View();
        var refused = new List<RichTextView.ImageRefusedEventArgs>();

        view.ImageStore = new DataUriImageStore(maximumBytes: 4);
        view.ImageRefused += (_, e) => refused.Add(e);

        var png = Png(8, 8);

        Assert.False(view.InsertImage(png, "image/png"));

        var report = Assert.Single(refused);

        Assert.Equal(png.Length, report.ByteCount);
        Assert.Equal("image/png", report.MediaType);
    }

    [AvaloniaFact]
    public void AnImageThatIsAcceptedRaisesNothing()
    {
        var view = View();
        var refused = 0;

        view.ImageRefused += (_, _) => refused++;

        Assert.True(view.InsertImage(Png(2, 2), "image/png"));
        Assert.Equal(0, refused);
    }

    [AvaloniaFact]
    public void AReadOnlyViewRefusesAnImage()
    {
        var view = View();
        var before = view.State.Doc;

        view.IsReadOnly = true;

        Assert.False(view.InsertImage(Png(2, 2), "image/png"));
        Assert.Equal(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void TheViewAdvertisesItselfAsADropTarget()
    {
        // Without this the platform never offers a drop at all, and the handlers never run.
        Assert.True(DragDrop.GetAllowDrop(View()));
    }

    /// <summary>
    /// A view that has opted out of drops, or is read-only, refuses one.
    /// </summary>
    /// <remarks>
    /// It has to raise the drop to mean anything. Setting the properties and then calling
    /// <see cref="RichTextView.InsertImage(byte[], string)"/> exercises the read-only check inside
    /// <c>Apply</c> and never reaches the drop guard at all, so the guard could be deleted and this
    /// stayed green — which is how a drop-refusing view was left able to accept drops.
    /// </remarks>
    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ADropIsRefusedWhenTheViewIsReadOnlyOrOptedOut(bool readOnly, bool optedOut)
    {
        var view = View();
        var before = view.State.Doc;

        Assert.True(view.AcceptsDrop);

        view.IsReadOnly = readOnly;
        view.AcceptsDrop = !optedOut;

        var drop = Drop(view, TextTransfer("dropped"));

        view.RaiseEvent(drop);

        Assert.Same(before, view.State.Doc);

        // Left unhandled and advertising nothing, so the host can offer the drop to something that
        // will take it instead of swallowing it into an editor that then ignores it.
        Assert.False(drop.Handled);
        Assert.Equal(DragDropEffects.None, drop.DragEffects);
    }

    [AvaloniaFact]
    public void AWillingViewTakesTheSameDrop()
    {
        // The companion the refusals need: without it they would all still pass if drops never
        // worked at all.
        var view = View();
        var drop = Drop(view, TextTransfer("dropped"));

        view.RaiseEvent(drop);

        Assert.Contains("dropped", DocumentText.Of(view.State.Doc), StringComparison.Ordinal);
        Assert.True(drop.Handled);
    }

    private static DragEventArgs Drop(RichTextView view, DataTransfer data) =>
        new(DragDrop.DropEvent, data, view, new global::Avalonia.Point(4, 4), KeyModifiers.None);

    private static DataTransfer TextTransfer(string text)
    {
        var item = new DataTransferItem();

        item.SetText(text);

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    [AvaloniaFact]
    public void TheImageStoreCannotBeSetToNull()
    {
        Assert.Throws<ArgumentNullException>(() => View().ImageStore = null!);
    }

    [AvaloniaFact]
    public void InsertImageChecksItsArguments()
    {
        Assert.Throws<ArgumentNullException>(() => View().InsertImage(Png(2, 2), null!));
    }

    /// <summary>An <see cref="IImageStore"/> that remembers what it was asked to keep.</summary>
    private sealed class RecordingStore : IImageStore
    {
        public List<int> Saved { get; } = [];

        public string? Save(ReadOnlySpan<byte> bytes, string mediaType)
        {
            Saved.Add(bytes.Length);

            return $"stored:{Saved.Count}";
        }
    }

    [AvaloniaFact]
    public void AReadOnlyViewLeavesNothingBehindInTheStore()
    {
        // The store was written first and the edit applied second, so an edit the document
        // refused left a file nothing references - and IImageStore has no way to take one back.
        var view = View();
        var store = new RecordingStore();

        view.ImageStore = store;
        view.IsReadOnly = true;

        Assert.False(view.InsertImage(Png(2, 2), "image/png"));
        Assert.Empty(store.Saved);
    }

    [AvaloniaFact]
    public void ARectangleOfCellsLeavesNothingBehindInTheStore()
    {
        // The same orphan by a different refusal: a rectangle spans whole cells, so there is no
        // one place an image could go and the paste is refused before it starts.
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("a")]),
                new TableCellNode([ParagraphNode.FromText("b")]),
            ])]),
        ]);

        var view = new RichTextView { State = EditorState.Create(doc) };
        var grid = TableGrid.Build(0, (TableNode)doc.Blocks[0]);
        var store = new RecordingStore();

        view.ImageStore = store;
        view.State = EditorState.Create(
            doc,
            CellSelection.Create(doc, grid.At(0, 0)!.Value.Pos, grid.At(0, 1)!.Value.Pos)!);

        Assert.False(view.InsertImage(Png(2, 2), "image/png"));
        Assert.Empty(store.Saved);
        Assert.Same(doc, view.State.Doc);
    }

    [AvaloniaFact]
    public void AnEditThatGoesThroughStoresTheImageExactlyOnce()
    {
        // Trying the edit before the store is written must not turn into writing it twice, nor
        // into pasting the probe's source instead of the one the store handed back.
        var view = View();
        var store = new RecordingStore();

        view.ImageStore = store;

        Assert.True(view.InsertImage(Png(2, 2), "image/png"));

        var length = Assert.Single(store.Saved);

        Assert.Equal(Png(2, 2).Length, length);

        var embed = Assert.Single(
            view.State.Doc.Blocks.OfType<ParagraphNode>()
                .SelectMany(p => p.Content.Embeds)
                .OfType<ImageEmbed>());

        Assert.Equal("stored:1", embed.Source);
    }

    [AvaloniaFact]
    public void OnlyImageFilesAreRecognised()
    {
        // Dropping a spreadsheet on a text editor should do nothing, not insert its bytes.
        foreach (var name in new[] { "a.png", "a.PNG", "a.jpg", "a.jpeg", "a.gif", "a.bmp", "a.webp" })
        {
            Assert.NotNull(RichTextView.MediaTypeOf(name));
        }

        foreach (var name in new[] { "a.xlsx", "a.txt", "a", "a.png.exe", "" })
        {
            Assert.Null(RichTextView.MediaTypeOf(name));
        }
    }
}
