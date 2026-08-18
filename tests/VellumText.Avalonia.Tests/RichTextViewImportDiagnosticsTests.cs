using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What the host is told about the compromises an import made on the way in.
/// </summary>
/// <remarks>
/// Every importer says, item by item, where it gave ground — a <c>&lt;blink&gt;</c> dropped, a
/// colour that could not be represented, a table flattened. The clipboard and drop paths computed
/// all of it and threw it away, so content changed silently on the way in and the application had
/// no way to know, let alone tell the user. These tests are what say each of those paths reports.
/// </remarks>
public class RichTextViewImportDiagnosticsTests
{
    private static readonly ImportDiagnostic[] Compromises =
    [
        new(DiagnosticSeverity.Dropped, "Script was removed.", "script"),
        new(DiagnosticSeverity.Downgraded, "A colour was approximated.", "color"),
    ];

    /// <summary>An importer that always has something to complain about.</summary>
    private sealed class Chatty : IDocumentImporter, IDocumentExporter
    {
        public string Format => "chatty";

        public string MediaType => "text/chatty";

        public Encoding? TextEncoding => Encoding.UTF8;

        public string Export(DocumentNode doc) => DocumentText.Of(doc);

        public void Export(Stream destination, DocumentNode doc) =>
            DocumentPayload.WriteAllText(destination, Encoding.UTF8, Export(doc));

        public ImportResult Import(string text) =>
            new(DocumentNode.FromParagraphs(text.Split('\n')), Compromises);

        public ImportResult Import(Stream source) =>
            Import(DocumentPayload.ReadAllText(source, Encoding.UTF8));
    }

    /// <summary>An importer that has nothing to complain about.</summary>
    private sealed class Quiet : IDocumentImporter
    {
        public string Format => "quiet";

        public string MediaType => "text/quiet";

        public Encoding? TextEncoding => Encoding.UTF8;

        public ImportResult Import(string text) => new(DocumentNode.FromParagraphs(text.Split('\n')));

        public ImportResult Import(Stream source) =>
            Import(DocumentPayload.ReadAllText(source, Encoding.UTF8));
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

    /// <summary>A transfer carrying <paramref name="payload"/> under <paramref name="format"/>'s flavour.</summary>
    private static DataTransfer Transfer(string format, string payload)
    {
        var flavour = RichTextClipboard.FlavorFor(format);
        var item = new DataTransferItem();

        item.Set(flavour.DataFormat, Encoding.UTF8.GetBytes(payload));

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

    [AvaloniaFact]
    public async Task A_paste_reports_what_the_importer_had_to_give_up()
    {
        var view = Shown("hello");
        var seen = new List<RichTextView.ContentImportedEventArgs>();

        view.Formats.Add(new Chatty());
        view.ContentImported += (_, e) => seen.Add(e);
        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(Transfer("chatty", "pasted"));

        Assert.True(await view.PasteAsync());

        var reported = Assert.Single(seen);

        Assert.Equal(Compromises, reported.Diagnostics.AsEnumerable());
        Assert.True(reported.AnythingDropped);
    }

    /// <summary>
    /// A drop is the same transfer as a paste and reported the same way.
    /// </summary>
    /// <remarks>
    /// Its own test rather than a trust in the shared helper, because the drop path reads the
    /// transfer through a different overload and was the second place the diagnostics were
    /// discarded.
    /// </remarks>
    [AvaloniaFact]
    public void A_drop_reports_what_the_importer_had_to_give_up()
    {
        var view = Shown("hello");
        var seen = new List<RichTextView.ContentImportedEventArgs>();

        view.Formats.Add(new Chatty());
        view.ContentImported += (_, e) => seen.Add(e);

        view.RaiseEvent(new DragEventArgs(
            DragDrop.DropEvent,
            Transfer("chatty", "dropped"),
            view,
            new Point(4, 4),
            KeyModifiers.None));

        Assert.Contains("dropped", DocumentText.Of(view.State.Doc));

        var reported = Assert.Single(seen);

        Assert.Equal(Compromises, reported.Diagnostics.AsEnumerable());
    }

    /// <summary>
    /// A programmatic import reports too, so a host reading a file gets the same notification a
    /// paste does.
    /// </summary>
    [AvaloniaFact]
    public void A_programmatic_import_reports_the_same_way()
    {
        var view = Shown("hello");
        var seen = new List<RichTextView.ContentImportedEventArgs>();

        view.ContentImported += (_, e) => seen.Add(e);
        view.MoveTo(1, extend: false);

        Assert.True(view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"), Compromises)));
        Assert.Equal(Compromises, Assert.Single(seen).Diagnostics.AsEnumerable());
    }

    /// <summary>
    /// A clean import still reports, with nothing in it.
    /// </summary>
    /// <remarks>
    /// The event says content arrived; the diagnostics say what it cost. A host that shows "two
    /// things were changed" after one paste needs the next, clean paste to take the notice away,
    /// which it can only do if that paste tells it anything at all.
    /// </remarks>
    [AvaloniaFact]
    public void A_clean_import_reports_an_empty_list_rather_than_nothing()
    {
        var view = Shown("hello");
        var seen = new List<RichTextView.ContentImportedEventArgs>();

        view.Formats.Add(new Quiet());
        view.ContentImported += (_, e) => seen.Add(e);
        view.MoveTo(1, extend: false);

        view.RaiseEvent(new DragEventArgs(
            DragDrop.DropEvent,
            Transfer("quiet", "dropped"),
            view,
            new Point(4, 4),
            KeyModifiers.None));

        var reported = Assert.Single(seen);

        Assert.Empty(reported.Diagnostics.AsEnumerable());
        Assert.False(reported.AnythingDropped);
    }

    /// <summary>Plain text off the clipboard is an import as well, and reports as one.</summary>
    [AvaloniaFact]
    public async Task A_plain_text_paste_reports_an_import()
    {
        var view = Shown("hello");
        var seen = 0;

        view.ContentImported += (_, _) => seen++;
        view.MoveTo(1, extend: false);

        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(TextTransfer("pasted"));

        Assert.True(await view.PasteAsync());
        Assert.Equal(1, seen);
    }

    /// <summary>
    /// An import that never reached the document reports nothing.
    /// </summary>
    /// <remarks>
    /// The companion the others need: an event raised unconditionally would satisfy every test
    /// above while telling a host that content arrived when none did.
    /// </remarks>
    [AvaloniaFact]
    public void An_import_a_read_only_view_refused_reports_nothing()
    {
        var view = Shown("hello");
        var seen = 0;

        view.ContentImported += (_, _) => seen++;
        view.IsReadOnly = true;

        Assert.False(view.PasteImport(new ImportResult(DocumentNode.FromParagraphs("imported"), Compromises)));
        Assert.Equal(0, seen);
    }

    [AvaloniaFact]
    public void PasteImport_checks_its_argument()
    {
        Assert.Throws<ArgumentNullException>(() => Shown("hello").PasteImport(null!));
    }
}
