using Avalonia.Headless.XUnit;
using Vellum.Avalonia;
using Vellum.Interop.Html;
using Vellum.Interop.Json;
using Vellum.Interop.Rtf;

namespace Vellum.Tests.Avalonia;

/// <summary>Every API the guide claims exists, compiled.</summary>
public class GuideCheckTests
{
    [AvaloniaFact]
    public void TheGuideCompiles()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromText("A heading"), ParagraphKind.Heading1),
            new ParagraphNode(InlineContent.Create(
                "Some bold text.",
                [new ValueSpan<MarkSet>(5, 4, new MarkSet { Style = TextStyle.Bold })],
                [])),
        ]);

        var editor = new RichTextEditor { State = EditorState.Create(doc) };
        var view = new RichTextView { State = EditorState.Create(doc) };

        _ = DocumentText.Of(doc);
        _ = DocumentPreview.Truncate(doc, 200);
        _ = DocumentPreview.IsLongerThan(doc, 200);

        view.ToggleStyle(TextStyle.Bold);
        view.SetForeground(new Rgba(200, 0, 0, 255));
        view.SetHighlight(null);
        view.SetFontFamily("Georgia");
        view.SetFontSize(13.5);
        view.SetLink(new LinkMark("https://example.com"));
        view.ClearFormatting();
        view.ToggleParagraphKind(ParagraphKind.Heading1);
        view.SetParagraphKind(ParagraphKind.Quote);
        view.SetAlign(TextAlign.Center);
        view.Indent();
        view.Outdent();
        view.ToggleList(ListKind.Unordered);
        view.Undo();
        view.Redo();

        _ = view.SelectionMarks.Style.HasFlag(TextStyle.Bold);
        _ = view.ParagraphKindAt;
        _ = view.AlignAt;
        _ = view.ListKindAt;
        _ = view.State.Selection as CellSelection;
        _ = view.MatchCount;
        _ = view.CurrentMatch;
        _ = view.Matches;
        _ = editor.CurrentMatchNumber;
        _ = editor.MatchCount;
        _ = view.ParagraphStyles;
        _ = view.TextStyles;
        _ = view.Embeds;

        view.Formats.Add(HtmlFormat.Instance);
        view.Formats.Add(RtfFormat.Instance);

        var result = HtmlFormat.Instance.Import("<p>x</p>");
        _ = result.AnythingDropped;
        foreach (var d in result.Diagnostics) { _ = d.ToString(); }
        _ = HtmlFormat.Instance.Export(result.Doc);
        _ = JsonFormat.Instance.Export(result.Doc);
        _ = JsonFormat.Instance.Import("{}").Doc;

        foreach (var flavour in RichTextClipboard.Flavors)
        {
            _ = flavour.Format; _ = flavour.DataFormat; _ = flavour.Encoding;
        }

        var payload = RichTextClipboard.Encode(HtmlFormat.Instance, doc);
        _ = RichTextClipboard.Decode(HtmlFormat.Instance, payload);

        view.Embeds = new BitmapEmbedRenderer { AllowLocalSources = true };
        view.ImageStore = new DataUriImageStore(maximumBytes: 1024 * 1024);
        view.InsertImage(new byte[] { 1 }, "image/png");

        view.Find("needle", new SearchOptions { MatchCase = true, WholeWord = false });
        view.FindNext();
        view.FindPrevious();
        view.ReplaceCurrent("thread");
        view.ReplaceAll("thread");
        view.ClearFind();

        _ = DocumentSearch.Find(doc, "x", SearchOptions.Default);

        editor.UndoLimit = 100;
        _ = editor.CanUndo;
        editor.IsSelectionToolbarEnabled = false;
        editor.IsReadOnly = false;
        editor.AcceptsReturn = true;
        editor.AcceptsTab = true;
        view.AcceptsDrop = true;

        _ = RichTextToolbar.FontFamilies;
        _ = RichTextToolbar.FontSizes;
        _ = RichTextToolbar.Palette;

        // Theming: every brush the guide names.
        _ = view.SelectionBrush; _ = view.CaretBrush; _ = view.FindHighlightBrush;
        _ = view.CurrentFindHighlightBrush; _ = view.TableBorderBrush; _ = view.TableHeaderBrush;
        _ = view.ImageHandleBrush; _ = view.ImageHandleBorderBrush; _ = view.TouchHandleBrush;
    }
}