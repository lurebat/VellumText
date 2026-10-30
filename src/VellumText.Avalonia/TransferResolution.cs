using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace VellumText.Avalonia;

/// <summary>What a paste or a drop resolved to: one of a document, an image, files or text.</summary>
/// <param name="Document">The richest flavour that imported to something.</param>
/// <param name="ImagePng">A bitmap the transfer carried, re-encoded as PNG.</param>
/// <param name="ImageFiles">Files among which at least one is an image.</param>
/// <param name="Text">Plain text, when nothing richer was there.</param>
internal sealed record TransferResolution(
    ImportResult? Document = null,
    byte[]? ImagePng = null,
    IReadOnlyList<IStorageItem>? ImageFiles = null,
    string? Text = null)
{
    /// <summary>A transfer that held nothing the editor can insert.</summary>
    public static TransferResolution Nothing { get; } = new();
}

/// <summary>The flavours of a transfer, read the way the transfer allows.</summary>
/// <remarks>
/// A paste reads the clipboard lazily — each flavour is a round trip to another process, and the
/// richest one usually settles it. A drop is read eagerly, inside the drop event, because
/// Avalonia's <see cref="DragEventArgs.DataTransfer"/> is not promised to outlive the handler on
/// every platform; the import that follows is asynchronous and runs after the handler returns.
/// </remarks>
internal abstract class TransferSource
{
    public abstract Task<byte[]?> BytesAsync(DataFormat<byte[]> format);

    public abstract Task<byte[]?> BitmapPngAsync();

    public abstract Task<IReadOnlyList<IStorageItem>?> FilesAsync();

    public abstract Task<string?> TextAsync();

    /// <summary>Reads an asynchronous transfer, such as the clipboard's, as it is asked.</summary>
    public static TransferSource From(IAsyncDataTransfer data) => new Lazy(data);

    /// <summary>Reads everything an import could want out of a drop, now.</summary>
    public static TransferSource Snapshot(IDataTransfer data, IEnumerable<IDocumentImporter> importers)
    {
        var flavours = new Dictionary<DataFormat<byte[]>, byte[]>();

        foreach (var importer in importers)
        {
            var format = RichTextClipboard.FlavorFor(importer.Format).DataFormat;

            if (data.TryGetValue(format) is { Length: > 0 } bytes)
            {
                flavours[format] = bytes;
            }
        }

        byte[]? png = null;

        if (data.TryGetBitmap() is { } bitmap)
        {
            using (bitmap)
            {
                png = Encode(bitmap);
            }
        }

        return new Snapshotted(flavours, png, data.TryGetFiles()?.ToArray(), data.TryGetText());
    }

    /// <summary>Re-encodes a bitmap as PNG, since a transfer hands over decoded pixels.</summary>
    internal static byte[]? Encode(Bitmap bitmap)
    {
        try
        {
            using var buffer = new MemoryStream();

            bitmap.Save(buffer, new PngBitmapEncoderOptions());

            return buffer.ToArray();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private sealed class Lazy(IAsyncDataTransfer data) : TransferSource
    {
        public override Task<byte[]?> BytesAsync(DataFormat<byte[]> format) => data.TryGetValueAsync(format);

        public override async Task<byte[]?> BitmapPngAsync()
        {
            if (await data.TryGetBitmapAsync().ConfigureAwait(true) is not { } bitmap)
            {
                return null;
            }

            using (bitmap)
            {
                return Encode(bitmap);
            }
        }

        public override async Task<IReadOnlyList<IStorageItem>?> FilesAsync() =>
            (await data.TryGetFilesAsync().ConfigureAwait(true))?.ToArray();

        public override Task<string?> TextAsync() => data.TryGetTextAsync();
    }

    private sealed class Snapshotted(
        Dictionary<DataFormat<byte[]>, byte[]> flavours,
        byte[]? png,
        IReadOnlyList<IStorageItem>? files,
        string? text) : TransferSource
    {
        public override Task<byte[]?> BytesAsync(DataFormat<byte[]> format) =>
            Task.FromResult(flavours.GetValueOrDefault(format));

        public override Task<byte[]?> BitmapPngAsync() => Task.FromResult(png);

        public override Task<IReadOnlyList<IStorageItem>?> FilesAsync() => Task.FromResult(files);

        public override Task<string?> TextAsync() => Task.FromResult(text);
    }
}
