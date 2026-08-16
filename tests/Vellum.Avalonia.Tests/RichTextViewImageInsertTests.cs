using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

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

    [AvaloniaFact]
    public void ADropIsRefusedWhenTheViewIsReadOnlyOrOptedOut()
    {
        var view = View();

        Assert.True(view.AcceptsDrop);

        view.IsReadOnly = true;

        Assert.False(view.InsertImage(Png(2, 2), "image/png"));
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
