using System.Diagnostics;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.Threading;
using VellumText;
using VellumText.Avalonia;
using Xunit;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The read-only viewer.
/// </summary>
/// <remarks>
/// <para>
/// Two things are worth testing and one of them is not obvious. The obvious one is that the
/// viewer shows the document and refuses to be typed into. The other is that it lays the
/// document out <em>identically</em> to the editor — that is the entire justification for
/// extracting <c>DocumentPresenter</c> rather than writing a second renderer, and it is the
/// thing that would rot silently.
/// </para>
/// <para>
/// The cost test is here for the same reason. The viewer exists because an editor is expensive
/// per instance; a change that quietly gave the viewer an editor's apparatus back would leave
/// every other test passing and the control pointless.
/// </para>
/// </remarks>
[Collection(ProcessWideAllocation.Name)]
public class RichTextViewerTests
{
    private const double Width = 400;
    private const double Height = 300;

    [AvaloniaFact]
    public void ItShowsTheDocument()
    {
        var viewer = Shown(Document(), out _);

        Assert.Equal(4, viewer.Blocks.Count);
        Assert.Equal(4, viewer.Slots.Length);
    }

    [AvaloniaFact]
    public void ItTakesTheKeyboardWithoutBecomingATabStop()
    {
        var viewer = Shown(Document(), out _);

        // Focusable now, because Ctrl+C has to reach the control to copy at all. Still not a tab
        // stop, which is what the original version of this test was really defending: tabbing
        // into a viewer lands the reader somewhere that leads nowhere.
        Assert.True(viewer.Focusable);
        Assert.False(viewer.IsTabStop);

        viewer.Focus();

        Assert.True(viewer.IsFocused);
    }

    /// <remarks>
    /// The pointer says what a drag will do. Over text a drag now selects, so the I-beam is
    /// honest; off the content there is nothing to select and it goes back to the arrow. The
    /// earlier version of this test asserted the opposite and was right at the time — the viewer
    /// had no selection then, so an I-beam promised an insertion point that did not exist.
    /// </remarks>
    [AvaloniaFact]
    public void ItShowsATextCursorOnlyOverText()
    {
        var viewer = Shown(Document(), out var window);

        Assert.Same(DocumentPresenter.ArrowCursor, viewer.Cursor);

        window.MouseMove(new Point(4, viewer.Blocks[0].Size.Height / 2));
        Assert.Same(DocumentPresenter.TextCursor, viewer.Cursor);

        window.MouseMove(new Point(viewer.Bounds.Width - 1, viewer.Bounds.Height - 1));
        Assert.Same(DocumentPresenter.ArrowCursor, viewer.Cursor);
    }

    /// <remarks>
    /// Eagerly, before the next layout pass: a geometry question asked between the assignment
    /// and that pass would otherwise be answered about the document that was there before.
    /// </remarks>
    [AvaloniaFact]
    public void ChangingTheDocumentRebuildsTheBlocksImmediately()
    {
        var viewer = Shown(Document(), out _);

        Assert.Equal(4, viewer.Blocks.Count);

        viewer.Document = DocumentNode.FromParagraphs("just the one now");

        // Deliberately no layout pass here. That is the point.
        Assert.Single(viewer.Blocks);
    }

    /// <remarks>
    /// Separate from the test above because the two are independent and one hid the other:
    /// realizing eagerly makes <see cref="DocumentPresenter.Blocks"/> right without telling
    /// the parent that this control now wants a different amount of room. Driving the layout
    /// from the dispatcher rather than calling <c>Measure</c> by hand is what makes the
    /// difference visible — an uninvalidated control is simply skipped.
    /// </remarks>
    [AvaloniaFact]
    public void ChangingTheDocumentAsksTheParentForNewRoom()
    {
        var viewer = new RichTextViewer { Document = DocumentNode.FromParagraphs("one line") };
        var stack = new StackPanel { Children = { viewer } };
        var window = new Window { Content = stack, Width = Width, Height = Height };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var before = viewer.Bounds.Height;

        viewer.Document = DocumentNode.FromParagraphs(
            Enumerable.Range(0, 30).Select(i => $"paragraph {i}").ToArray());

        Dispatcher.UIThread.RunJobs();

        Assert.True(
            viewer.Bounds.Height > before * 2,
            $"height went {before} -> {viewer.Bounds.Height}; the parent was never told to re-measure");
    }

    /// <remarks>
    /// A viewer bound to "the selected history entry" with nothing selected is the ordinary
    /// case, not a bug, so null shows an empty document rather than throwing.
    /// </remarks>
    [AvaloniaFact]
    public void ANullDocumentIsAnEmptyOne()
    {
        var viewer = Shown(Document(), out var window);

        viewer.Document = null!;
        Layout(window);

        Assert.Same(DocumentNode.Empty, viewer.Document);
    }

    /// <remarks>
    /// The whole reason for the shared base. If these two ever disagree, a document moved from
    /// an editor into a viewer would reflow, and the viewer would stop being a preview of the
    /// thing it is previewing.
    /// </remarks>
    [AvaloniaFact]
    public void ItLaysTextOutExactlyAsTheEditorDoes()
    {
        var doc = Document();

        var viewer = Shown(doc, out _);
        var editor = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = editor, Width = Width, Height = Height };

        window.Show();
        Layout(window);

        Assert.Equal(editor.Blocks.Count, viewer.Blocks.Count);

        for (var i = 0; i < editor.Blocks.Count; i++)
        {
            Assert.Equal(editor.Blocks[i].Size, viewer.Blocks[i].Size);
            Assert.Equal(editor.BlockOrigin(i), viewer.BlockOrigin(i));
        }

        Assert.Equal(editor.Extent, viewer.Extent);

        // Including the caret gutter, which the viewer has no use for and pays for anyway. This
        // is the assertion that catches someone reclaiming that pixel for the viewer alone.
        Assert.Equal(editor.DesiredSize, viewer.DesiredSize);
    }

    /// <remarks>
    /// Virtualization is inherited, not reimplemented, so this asserts the inheritance actually
    /// happened rather than that the algorithm is correct — which the editor's own tests cover.
    /// </remarks>
    [AvaloniaFact]
    public void ItVirtualizesLikeTheEditor()
    {
        var many = DocumentNode.FromParagraphs(
            Enumerable.Range(0, 400).Select(i => $"paragraph number {i}").ToArray());

        var viewer = new RichTextViewer { Document = many };
        var scroller = new ScrollViewer { Content = viewer };
        var window = new Window { Content = scroller, Width = Width, Height = Height };

        window.Show();
        Layout(window);

        var realized = viewer.RealizedBlocks;
        var count = realized.End.Value - realized.Start.Value;

        Assert.Equal(400, viewer.Blocks.Count);
        Assert.True(count > 0, "nothing was realized at all, so this proves nothing");
        Assert.True(
            count < 100,
            $"realized {realized.Start.Value}..{realized.End.Value} of 400, which is not virtualizing");
    }

    /// <summary>
    /// The claim the control exists to make, asserted so it cannot quietly stop being true.
    /// </summary>
    /// <remarks>
    /// Measured on this machine at roughly 20 KiB for an editor against 1 KiB for a viewer,
    /// before either lays anything out. The bound is deliberately loose — a factor of four,
    /// against a measured factor of twenty — because this is a guard against the viewer growing
    /// a blink timer and a context menu, not a benchmark.
    /// </remarks>
    [AvaloniaFact]
    public void AViewerCostsFarLessToConstructThanAnEditor()
    {
        // Warm both, so the numbers are allocation and not first-call JIT.
        for (var i = 0; i < 20; i++)
        {
            _ = new RichTextView();
            _ = new RichTextViewer();
        }

        var editor = Allocated(() => new RichTextView());
        var viewer = Allocated(() => new RichTextViewer());

        Assert.True(
            viewer * 4 < editor,
            $"a viewer allocates {viewer / 1024.0:F1} KiB and an editor {editor / 1024.0:F1} KiB; "
            + "the viewer has grown an editor's apparatus");
    }

    private const int Runs = 100;

    /// <summary>Bytes allocated by one call, averaged over <see cref="Runs"/>.</summary>
    private static double Allocated(Func<object> make)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var kept = new List<object>(Runs);

        for (var i = 0; i < Runs; i++)
        {
            kept.Add(make());
        }

        var bytes = GC.GetTotalAllocatedBytes(precise: true) - before;

        // Held until after the reading so nothing is collected mid-run and undercounts.
        Assert.Equal(Runs, kept.Count);

        return bytes / (double)Runs;
    }

    /// <summary>
    /// The font properties are settable directly, not only inherited from a parent.
    /// </summary>
    /// <remarks>
    /// A viewer is a leaf inside someone else's data template, where there is often nothing above
    /// it carrying a font. Without these it could only be styled by wrapping it in something that
    /// sets <c>TextElement.FontSize</c>, which the XAML compiler does not hint at — it just fails
    /// to resolve the property.
    /// </remarks>
    [AvaloniaFact]
    public void ItTakesAFontDirectly()
    {
        var viewer = new RichTextViewer { Document = Document(), FontSize = 30 };
        var window = new Window { Content = viewer, Width = Width, Height = Height };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var big = viewer.Extent.Height;

        viewer.FontSize = 10;
        Dispatcher.UIThread.RunJobs();

        Assert.True(
            viewer.Extent.Height < big,
            $"height stayed at {big} when the font shrank, so FontSize did nothing");
    }

    /// <remarks>
    /// <c>AddOwner</c> is documented to return the same property instance rather than a new one,
    /// and the base leans on that: it decides whether to rebuild its text styles by looking the
    /// changed property up in a set of <see cref="TextElement"/> properties. If these were
    /// distinct, setting <c>FontSize</c> on the control would never match that set and a font
    /// change would silently not repaint.
    /// </remarks>
    [AvaloniaFact]
    public void TheFontPropertiesAreTheTextElementOnes()
    {
        Assert.Same(TextElement.FontSizeProperty, DocumentPresenter.FontSizeProperty);
        Assert.Same(TextElement.ForegroundProperty, DocumentPresenter.ForegroundProperty);
        Assert.Same(TextElement.FontFamilyProperty, DocumentPresenter.FontFamilyProperty);
    }

    /// <remarks>
    /// Separate from the measure test because a document can change without changing size, and
    /// then invalidating the measure achieves nothing — Avalonia has no reason to repaint a
    /// control whose layout it believes is still correct. A colour-only edit is the clean case:
    /// identical geometry, different pixels. Asserted on a real rendered frame, because there is
    /// no other honest way to ask whether something was actually drawn.
    /// </remarks>
    [AvaloniaFact]
    public void ChangingOnlyTheColourStillRepaints()
    {
        var viewer = new RichTextViewer { Document = Coloured(255, 0, 0) };
        var window = new Window { Content = viewer, Width = Width, Height = Height };

        window.Show();
        Dispatcher.UIThread.RunJobs();

        var before = Pixels(window);
        var size = viewer.Extent;

        viewer.Document = Coloured(0, 0, 255);
        Dispatcher.UIThread.RunJobs();

        // If these differed, a measure invalidation alone would have forced the repaint and the
        // test would prove nothing about painting.
        Assert.Equal(size, viewer.Extent);
        Assert.False(
            before.SequenceEqual(Pixels(window)),
            "the frame is identical after recolouring the text, so nothing repainted");
    }

    /// <remarks>
    /// Assigning a document to a control that has never been measured must not lay it out. There
    /// is no width to lay it out against yet, so it would be measured at the fallback width and
    /// immediately thrown away by the first real measure — wasted work in exactly the case the
    /// control exists to make cheap, a list building many previews before any of them is shown.
    /// </remarks>
    [AvaloniaFact]
    public void AssigningADocumentBeforeTheFirstMeasureLaysNothingOut()
    {
        var viewer = new RichTextViewer();

        viewer.Document = Document();

        Assert.Empty(viewer.Blocks);
    }

    private static DocumentNode Coloured(byte r, byte g, byte b) =>
        new([
            new ParagraphNode(InlineContent.Create(
                "some text whose colour is the only thing that changes",
                [new ValueSpan<MarkSet>(0, 53, new MarkSet { Foreground = Rgba.FromRgb(r, g, b) })],
                [])),
        ]);

    private static byte[] Pixels(Window window)
    {
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");
        using var stream = new MemoryStream();

        frame.Save(stream, new PngBitmapEncoderOptions());

        return stream.ToArray();
    }

    private static RichTextViewer Shown(DocumentNode doc, out Window window)
    {
        var viewer = new RichTextViewer { Document = doc };

        window = new Window { Content = viewer, Width = Width, Height = Height };
        window.Show();

        Layout(window);

        return viewer;
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));
    }

    private static DocumentNode Document() =>
        DocumentNode.FromParagraphs(
            "hello world, this first paragraph is long enough to take more than one line",
            "a second paragraph, also long enough that it wraps across the available width",
            "a third one",
            "and a fourth");
}
