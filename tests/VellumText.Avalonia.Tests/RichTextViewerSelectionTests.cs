using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Media;
using VellumText;
using VellumText.Avalonia;
using VellumText.Interop.Html;
using Xunit;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Selection and copy on the read-only viewer.
/// </summary>
/// <remarks>
/// The driving consumer renders clips in this control, so "look but do not take" is the wrong
/// behaviour. These assert what a reader can now do, and — just as importantly — that nothing a
/// reader does can change the document.
/// </remarks>
public class RichTextViewerSelectionTests
{
    private const double Width = 400;
    private const double Height = 300;

    [AvaloniaFact]
    public void DraggingSelectsTheTextItPassesOver()
    {
        var viewer = Shown(out var window);

        window.MouseDown(At(viewer, 0, 2), MouseButton.Left);
        window.MouseMove(At(viewer, 0, 60));
        window.MouseUp(At(viewer, 0, 60), MouseButton.Left);

        Assert.True(viewer.SelectionEnd > viewer.SelectionStart);

        // The content, not merely that something is selected: an off-by-one origin would still
        // select a non-empty range, and a range of the wrong text is the bug worth catching.
        Assert.StartsWith("hello", viewer.SelectedText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void ShiftArrowExtendsTheSelectionOneClusterAtATime()
    {
        var viewer = Shown(out _);

        viewer.Focus();
        Key(viewer, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);
        Key(viewer, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);

        Assert.Equal("he", viewer.SelectedText());
    }

    /// <remarks>
    /// The reason the head moves through <c>ParagraphView</c> rather than by adding one to the
    /// position: a family emoji is a single ZWJ cluster of eleven UTF-16 units, and a caret that
    /// lands inside one renders a broken half-glyph.
    /// </remarks>
    [AvaloniaFact]
    public void ShiftArrowNeverStopsInsideAGraphemeCluster()
    {
        const string Family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";

        var viewer = Shown(out _, DocumentNode.FromParagraphs(Family + "x"));

        viewer.Focus();
        Key(viewer, global::Avalonia.Input.Key.Right, KeyModifiers.Shift);

        Assert.Equal(Family, viewer.SelectedText());
    }

    [AvaloniaFact]
    public void HomeAndEndReachBothEndsOfTheDocument()
    {
        var viewer = Shown(out _);

        viewer.Focus();
        Key(viewer, global::Avalonia.Input.Key.End, KeyModifiers.Shift);

        Assert.EndsWith("and a fourth", viewer.SelectedText(), StringComparison.Ordinal);

        Key(viewer, global::Avalonia.Input.Key.Home, KeyModifiers.None);

        Assert.Equal(string.Empty, viewer.SelectedText());
    }

    [AvaloniaFact]
    public void ControlASelectsEverythingAndEscapeDropsIt()
    {
        var viewer = Shown(out _);

        viewer.Focus();
        Key(viewer, global::Avalonia.Input.Key.A, KeyModifiers.Control);

        Assert.Contains("hello world", viewer.SelectedText(), StringComparison.Ordinal);
        Assert.Contains("and a fourth", viewer.SelectedText(), StringComparison.Ordinal);

        Key(viewer, global::Avalonia.Input.Key.Escape, KeyModifiers.None);

        Assert.Equal(string.Empty, viewer.SelectedText());
    }

    [AvaloniaFact]
    public void DoubleClickSelectsAWordAndTripleClickSelectsTheParagraph()
    {
        var viewer = Shown(out var window);

        Click(viewer, window, At(viewer, 0, 2), clicks: 2);

        Assert.Equal("hello", viewer.SelectedText());

        Click(viewer, window, At(viewer, 0, 2), clicks: 3);

        Assert.Equal(
            "hello world, this first paragraph is long enough to take more than one line",
            viewer.SelectedText());
    }

    /// <remarks>
    /// The selection is positions in the document that was replaced, so keeping it would highlight
    /// a span of the new one that nobody asked for — and, worse, copy it.
    /// </remarks>
    [AvaloniaFact]
    public void ReplacingTheDocumentDropsTheSelection()
    {
        var viewer = Shown(out _);

        viewer.SelectAll();
        Assert.NotEqual(string.Empty, viewer.SelectedText());

        viewer.Document = DocumentNode.FromParagraphs("something else entirely");

        Assert.Equal(string.Empty, viewer.SelectedText());
    }

    /// <remarks>
    /// The point of copying out of this control rather than retyping: a clip pasted into a word
    /// processor keeps its bold. Asserting on the payload's content, because a copy that writes
    /// the flavour but exports the empty document would satisfy any check of mere presence.
    /// </remarks>
    [AvaloniaFact]
    public void CopyCarriesTheRegisteredFlavoursNotJustText()
    {
        var viewer = Shown(out _, DocumentNode.FromParagraphs("hello world"));

        viewer.Formats.Add(HtmlFormat.Instance);
        viewer.SelectAll();

        using var data = viewer.BuildCopyData(viewer.SelectedText());
        var item = Assert.Single(data.Items);

        Assert.Equal("hello world", item.TryGetText());

        var html = item.TryGetValue(RichTextClipboard.FlavorFor("html")!.DataFormat);

        Assert.NotNull(html);
        Assert.Contains("hello world", System.Text.Encoding.UTF8.GetString((byte[])html!), StringComparison.Ordinal);
    }

    /// <remarks>
    /// Copying one paragraph out of a four-paragraph clip must not put the whole clip on the
    /// clipboard. This went wrong first time: the exporter was handed <c>Document</c> rather than
    /// the cut, and every flavour carried all four paragraphs while the plain text carried one.
    /// </remarks>
    [AvaloniaFact]
    public void CopyExportsOnlyTheSelection()
    {
        var viewer = Shown(out _);

        viewer.Formats.Add(HtmlFormat.Instance);
        viewer.SelectParagraphAt(1);

        using var data = viewer.BuildCopyData(viewer.SelectedText());
        var raw = Assert.Single(data.Items).TryGetValue(RichTextClipboard.FlavorFor("html")!.DataFormat);
        var html = System.Text.Encoding.UTF8.GetString((byte[])raw!);

        Assert.Contains("hello world", html, StringComparison.Ordinal);
        Assert.DoesNotContain("and a fourth", html, StringComparison.Ordinal);
    }

    /// <remarks>
    /// The read-only claim, checked rather than asserted in prose. If someone later gives the
    /// viewer an editing method this goes red, which is the entire point: the guarantee here is
    /// structural — there is no <c>Apply</c> to gate — so only the absence of the methods keeps
    /// it true.
    /// </remarks>
    [AvaloniaFact]
    public void TheViewerExposesNoWayToChangeTheDocument()
    {
        var members = typeof(RichTextViewer)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var editing in new[]
                 {
                     "InsertText", "Delete", "Backspace", "Undo", "Redo", "Cut", "PasteAsync",
                     "PasteText", "PasteImport", "InsertLineBreak", "ApplyLink", "RemoveLink",
                 })
        {
            Assert.DoesNotContain(editing, members);
        }
    }

    /// <remarks>
    /// Selection must not have dragged in the editor's per-instance cost, which is the measured
    /// reason the two controls exist separately at all. Two integers and a brush, not a blink
    /// timer and an input-method client.
    /// </remarks>
    [AvaloniaFact]
    public void TheViewerStillCostsLessThanTheEditor()
    {
        Assert.True(Allocated(() => new RichTextViewer()) < Allocated(() => new RichTextView()));
    }

    private static double Allocated(Func<object> make)
    {
        // Warm the type up so the measurement is the instance, not its static initialization.
        GC.KeepAlive(make());

        var before = GC.GetAllocatedBytesForCurrentThread();
        var made = make();
        var after = GC.GetAllocatedBytesForCurrentThread();

        GC.KeepAlive(made);

        return after - before;
    }

    private static void Key(RichTextViewer viewer, Key key, KeyModifiers modifiers) =>
        viewer.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });

    /// <summary>Synthesizes a multi-click, which the headless mouse helpers cannot do.</summary>
    private static void Click(RichTextViewer viewer, Window window, Point point, int clicks)
    {
        viewer.RaiseEvent(new PointerPressedEventArgs(
            viewer,
            new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true),
            window,
            point,
            0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None,
            clicks));
    }

    /// <summary>A point inside a block, in viewer coordinates.</summary>
    private static Point At(RichTextViewer viewer, int block, double x)
    {
        var y = 0.0;

        for (var i = 0; i < block; i++)
        {
            y += viewer.Blocks[i].Size.Height;
        }

        return new Point(x, y + (viewer.Blocks[block].Size.Height / 2));
    }

    private static RichTextViewer Shown(out Window window, DocumentNode? doc = null)
    {
        var viewer = new RichTextViewer { Document = doc ?? Document() };

        window = new Window { Content = viewer, Width = Width, Height = Height };
        window.Show();

        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        return viewer;
    }

    private static DocumentNode Document() =>
        DocumentNode.FromParagraphs(
            "hello world, this first paragraph is long enough to take more than one line",
            "a second paragraph, also long enough that it wraps across the available width",
            "a third one",
            "and a fourth");
}
