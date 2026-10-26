using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A dropped image file is read only as far as the image store could accept it.
/// </summary>
/// <remarks>
/// The drop path copied the whole file into a growing <see cref="MemoryStream"/> and then copied
/// that again, before asking the store — whose default refuses anything over 4 MB. A 64 MB file
/// allocated 192 MB to be refused, and a 512 MB one 1.5 GB.
/// </remarks>
public class DropSizeTests
{
    /// <summary>A non-seekable stream of <paramref name="length"/> bytes starting with a PNG signature.</summary>
    private sealed class SizedStream(long length, byte[]? prefix = null) : Stream
    {
        private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        private readonly byte[] _prefix = prefix ?? PngSignature;
        private long _position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public long BytesRead => _position;

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - _position);

            Array.Clear(buffer, offset, n);

            for (var i = 0; i < n && _position + i < _prefix.Length; i++)
            {
                buffer[offset + i] = _prefix[_position + i];
            }

            _position += n;

            return n;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A storage file backed by a <see cref="SizedStream"/>.</summary>
    public class ProxyFile : DispatchProxy
    {
        private Stream? _stream;
        private ulong _size;

        internal static IStorageFile Of(Stream stream, ulong size)
        {
            var file = DispatchProxy.Create<IStorageFile, ProxyFile>();

            ((ProxyFile)(object)file)._stream = stream;
            ((ProxyFile)(object)file)._size = size;

            return file;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method?.Name switch
        {
            "get_Name" => "dropped.png",
            "get_Path" => new Uri("file:///dropped.png"),
            "get_CanBookmark" => false,
            "OpenReadAsync" => Task.FromResult(_stream!),
            "GetBasicPropertiesAsync" => Task.FromResult(new StorageItemProperties(_size, null, null)),
            "Dispose" => null,
            _ => null,
        };
    }

    private static (RichTextView View, List<int> Refused) Shown()
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("a")) };
        var window = new Window { Content = view, Width = 400, Height = 300 };
        var refused = new List<int>();

        window.Show();
        view.ImageRefused += (_, e) => refused.Add(e.ByteCount);

        return (view, refused);
    }

    private static async Task Drop(RichTextView view, IStorageFile file)
    {
        var data = new DataTransfer();
        var item = new DataTransferItem();

        item.SetFile(file);
        data.Add(item);

        view.RaiseEvent(new DragEventArgs(DragDrop.DropEvent, data, view, new Point(4, 4), KeyModifiers.None));

        await view.PendingDrop;
    }

    [AvaloniaFact]
    public async Task A_file_larger_than_the_store_accepts_is_not_read_whole()
    {
        const long size = 64L * 1024 * 1024;
        var (view, refused) = Shown();
        var stream = new SizedStream(size);

        GC.Collect();
        var before = GC.GetTotalAllocatedBytes(precise: true);

        await Drop(view, ProxyFile.Of(stream, size));

        var allocatedMb = (GC.GetTotalAllocatedBytes(precise: true) - before) / (1024 * 1024);

        Assert.Equal([(int)size], refused);
        Assert.True(allocatedMb < 20, $"refusing a 64 MB file allocated {allocatedMb} MB");
        Assert.True(stream.BytesRead <= DataUriImageStore.DefaultMaximumBytes + 128 * 1024, $"read {stream.BytesRead} bytes");
        Assert.Equal("a", DocumentText.Of(view.State.Doc));
    }

    /// <summary>The control: a file the store accepts is still read in full and inserted.</summary>
    [AvaloniaFact]
    public async Task A_file_within_the_limit_is_inserted()
    {
        var (view, refused) = Shown();
        var png = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
        var before = view.State.Doc;

        await Drop(view, ProxyFile.Of(new MemoryStream(png), (ulong)png.Length));

        Assert.Empty(refused);
        Assert.Contains(view.State.Doc.Blocks.OfType<ParagraphNode>(), p => p.Content.Embeds.Any());
    }

    /// <summary>A store with its own, larger limit is honoured rather than the default's.</summary>
    [AvaloniaFact]
    public async Task A_larger_store_limit_is_read_up_to()
    {
        const int size = 6 * 1024 * 1024;
        var (view, refused) = Shown();
        var stream = new SizedStream(size);
        var before = view.State.Doc;

        view.ImageStore = new DataUriImageStore(8 * 1024 * 1024);

        await Drop(view, ProxyFile.Of(stream, size));

        Assert.Empty(refused);
        Assert.Equal(size, stream.BytesRead);
    }
}
