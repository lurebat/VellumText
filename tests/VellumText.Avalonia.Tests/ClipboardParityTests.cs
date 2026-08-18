using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// That the shared clipboard helper and the controls that use it agree.
/// </summary>
/// <remarks>
/// Both controls once built their own clipboard payload beside <see cref="RichTextClipboard"/>'s,
/// and the copies drifted: what a paste tolerated a drop did not, because only one of the two
/// remembered that the embed placeholder cannot be put in a paragraph. These are the cases where
/// the two paths must give the same answer.
/// </remarks>
public class ClipboardParityTests
{
    private static RichTextView Shown(params string[] paragraphs)
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return view;
    }

    private static DataTransfer TextTransfer(string text)
    {
        var item = new DataTransferItem();

        item.SetText(text);

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    /// <summary>Text carrying the reserved embed placeholder, which no paragraph may contain.</summary>
    private const string WithPlaceholder = "before\ufffcafter";

    [AvaloniaFact]
    public void Reading_plain_text_survives_the_reserved_placeholder()
    {
        // U+FFFC is the object-replacement character the model reserves for embeds, so building a
        // paragraph out of it throws - into the broad handler this method has, which returned null
        // and lost the whole clip. The view's own paste path had already learned this; the shared
        // helper had not, so a clip a paste accepted was a clip a drop silently dropped.
        var result = RichTextClipboard.Read(TextTransfer(WithPlaceholder), []);

        Assert.NotNull(result);
        Assert.Equal("beforeafter", DocumentText.Of(result.Doc).Trim());
    }

    [AvaloniaFact]
    public async Task Reading_plain_text_asynchronously_survives_it_too()
    {
        // The async overload is the paste path's, and had the same defect. Its own test because it
        // is its own copy of the fallback.
        var result = await RichTextClipboard.ReadAsync(TextTransfer(WithPlaceholder), []);

        Assert.NotNull(result);
        Assert.Equal("beforeafter", DocumentText.Of(result.Doc).Trim());
    }

    [AvaloniaFact]
    public void A_drop_of_text_carrying_the_placeholder_still_lands()
    {
        // The user-visible half of the same defect: this text dropped nothing at all.
        var view = Shown("hello");

        view.RaiseEvent(new DragEventArgs(
            DragDrop.DropEvent,
            TextTransfer(WithPlaceholder),
            view,
            new Point(4, 4),
            KeyModifiers.None));

        var text = DocumentText.Of(view.State.Doc);

        Assert.Contains("beforeafter", text);
        Assert.DoesNotContain('\ufffc', text);
    }

    [AvaloniaFact]
    public async Task A_paste_of_the_same_text_lands_the_same_way()
    {
        // The parity assertion. Without it the test above passes just as well against a pair that
        // agreed by both being broken.
        var view = Shown("hello");

        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(TextTransfer(WithPlaceholder));

        Assert.True(await view.PasteAsync());

        var text = DocumentText.Of(view.State.Doc);

        Assert.Contains("beforeafter", text);
        Assert.DoesNotContain('\ufffc', text);
    }

    [AvaloniaFact]
    public void Both_paths_normalize_the_same_line_breaks()
    {
        // The paste path used to normalize CRLF and CR only. A drop already went through
        // ReplaceLineEndings, which also takes the form feed, NEL and the Unicode line and
        // paragraph separators - so the same clip became six paragraphs through a drop and three
        // through a paste, with raw control characters in the middle of them.
        const string Mixed = "a\r\nb\rc\nd\u000ce\u2028f\u0085g";

        var dropped = RichTextClipboard.Read(TextTransfer(Mixed), []);

        Assert.NotNull(dropped);
        Assert.Equal(7, dropped.Doc.Blocks.Length);

        var view = Shown("hello");

        view.SelectAll();

        Assert.True(view.PasteText(Mixed));
        Assert.Equal(7, view.State.Doc.Blocks.Length);
        Assert.Equal("a b c d e f g", DocumentText.Of(view.State.Doc).Trim().Replace('\n', ' '));
    }

    /// <summary>An exporter that answers, so a copy has a flavour beyond text to carry.</summary>
    private sealed class Loud : IDocumentExporter
    {
        public string Format => "loud";

        public string MediaType => "text/loud";

        public Encoding? TextEncoding => Encoding.UTF8;

        public string Export(DocumentNode doc) => DocumentText.Of(doc).ToUpperInvariant();

        public void Export(Stream destination, DocumentNode doc) =>
            DocumentPayload.WriteAllText(destination, Encoding.UTF8, Export(doc));
    }

    [AvaloniaFact]
    public void The_editor_and_the_viewer_build_the_same_payload_as_the_helper()
    {
        // Three copies of one decision is two chances to drift. They are one call now, and this is
        // what says so: the same selection, through all three, produces the same flavours.
        var view = Shown("hello world");

        view.Formats.Add(new Loud());
        view.SelectAll();

        var viewer = new RichTextViewer { Document = view.State.Doc };

        viewer.Formats.Add(new Loud());
        viewer.SelectAll();

        var flavour = RichTextClipboard.FlavorFor("loud").DataFormat;

        var fromView = Assert.Single(view.BuildCopyData(view.SelectedText()).Items);
        var fromViewer = Assert.Single(viewer.BuildCopyData(viewer.SelectedText()).Items);
        var fromHelper = Assert.Single(
            RichTextClipboard.Build(view.SelectedDocument(), [new Loud()]).Items);

        Assert.NotNull(fromView.TryGetValue(flavour));
        Assert.Equal(
            Encoding.UTF8.GetString(fromHelper.TryGetValue(flavour)!),
            Encoding.UTF8.GetString(fromView.TryGetValue(flavour)!));
        Assert.Equal(
            Encoding.UTF8.GetString(fromHelper.TryGetValue(flavour)!),
            Encoding.UTF8.GetString(fromViewer.TryGetValue(flavour)!));
    }

    [AvaloniaFact]
    public void The_viewer_copies_the_selection_rather_than_the_document()
    {
        // Asserting only that the three agree would pass just as well against three that agreed on
        // copying everything. This is the part a reader of a copy actually cares about.
        var viewer = new RichTextViewer { Document = DocumentNode.FromParagraphs("hello", "world") };

        viewer.Formats.Add(new Loud());

        var window = new Window { Content = viewer, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        Assert.True(viewer.SelectParagraphAt(1));

        var flavour = RichTextClipboard.FlavorFor("loud").DataFormat;
        var item = Assert.Single(viewer.BuildCopyData(viewer.SelectedText()).Items);
        var payload = Encoding.UTF8.GetString(item.TryGetValue(flavour)!);

        Assert.Contains("HELLO", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("WORLD", payload, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void A_copy_across_cells_puts_exactly_what_is_selected_on_the_clipboard()
    {
        // A rectangle is the shape where the copied document is rebuilt rather than cut, so it is
        // where the payload's plain text is most easily made to disagree with what the control
        // shows highlighted. Pasting into a plain text editor has to give back what was selected.
        var doc = new DocumentNode([
            new TableNode([new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("a")]),
                new TableCellNode([ParagraphNode.FromText("b")]),
            ])]),
        ]);

        var view = Shown();
        var grid = TableGrid.Build(0, (TableNode)doc.Blocks[0]);

        view.State = EditorState.Create(
            doc,
            CellSelection.Create(doc, grid.At(0, 0)!.Value.Pos, grid.At(0, 1)!.Value.Pos)!);

        var item = Assert.Single(view.BuildCopyData(view.SelectedText()).Items);

        Assert.Equal("a\nb", view.SelectedText());
        Assert.Equal(view.SelectedText(), item.TryGetText());
    }
}
