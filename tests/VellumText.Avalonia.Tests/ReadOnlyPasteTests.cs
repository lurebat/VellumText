using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// A paste that cannot land must not read and parse the clipboard first.
/// </summary>
/// <remarks>
/// Read-only was checked only when the decoded document was about to be applied, so a viewer
/// handed every Ctrl+V to every registered importer: untrusted content fully parsed — and exposed
/// to every importer bug — for a paste that was always going to be refused. 600 KB of HTML cost
/// 912 ms on a read-only view against 4 ms for the refusal itself.
/// </remarks>
public class ReadOnlyPasteTests
{
    /// <summary>An importer that counts how often it is asked.</summary>
    private sealed class Counting : IDocumentImporter
    {
        public int Runs { get; private set; }

        public string Format => "counting";

        public string MediaType => "text/x-counting";

        public Encoding? TextEncoding => Encoding.UTF8;

        public ImportResult Import(string text)
        {
            Runs++;
            return new ImportResult(DocumentNode.FromParagraphs(text));
        }

        public async Task<ImportResult> ImportAsync(Stream source, CancellationToken cancellationToken = default) =>
            Import(await DocumentPayload.ReadAllTextAsync(source, Encoding.UTF8, cancellationToken));
    }

    private static RichTextView Shown(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return view;
    }

    private static DataTransfer Transfer(string format, string payload)
    {
        var item = new DataTransferItem();

        item.Set(RichTextClipboard.FlavorFor(format).DataFormat, Encoding.UTF8.GetBytes(payload));
        item.SetText(payload);

        var data = new DataTransfer();

        data.Add(item);

        return data;
    }

    private static async Task<Counting> Staged(RichTextView view)
    {
        var importer = new Counting();

        view.Formats.Add(importer);
        await TopLevel.GetTopLevel(view)!.Clipboard!.SetDataAsync(Transfer(importer.Format, "pasted"));

        return importer;
    }

    [AvaloniaFact]
    public async Task A_read_only_view_does_not_run_the_importer()
    {
        var view = Shown(DocumentNode.FromParagraphs("hello"));
        var importer = await Staged(view);

        view.IsReadOnly = true;

        Assert.False(await view.PasteAsync());
        Assert.False(await view.PasteTextAsync());

        Assert.Equal(0, importer.Runs);
        Assert.Equal("hello", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public async Task A_cell_rectangle_does_not_run_the_importer()
    {
        var table = new TableNode([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("a")]),
                new TableCellNode([ParagraphNode.FromText("b")]),
            ]),
        ]);
        var view = Shown(new DocumentNode([table]));
        var importer = await Staged(view);
        var before = view.State.Doc;

        var grid = TableGrid.Build(0, table);

        view.State = EditorState.Create(
            before,
            CellSelection.Create(before, grid.At(0, 0)!.Value.Pos, grid.At(0, 1)!.Value.Pos)!);
        Assert.IsType<CellSelection>(view.State.Selection);

        Assert.False(await view.PasteAsync());
        Assert.False(await view.PasteTextAsync());

        Assert.Equal(0, importer.Runs);
        Assert.Same(before, view.State.Doc);
    }

    /// <summary>The control: the same clipboard on an editable view does reach the importer.</summary>
    [AvaloniaFact]
    public async Task An_editable_view_runs_the_importer()
    {
        var view = Shown(DocumentNode.FromParagraphs("hello"));
        var importer = await Staged(view);

        Assert.True(await view.PasteAsync());
        Assert.Equal(1, importer.Runs);
    }
}
