using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Vellum.Avalonia;
using Vellum.Interop.Html;
using Vellum.Interop.Rtf;

namespace Vellum.Demo;

/// <summary>One thing that came in through this window.</summary>
/// <param name="Label">Where it came from and when.</param>
/// <param name="Doc">The imported document, kept whole rather than flattened to a string.</param>
public sealed record HistoryEntry(string Label, DocumentNode Doc)
{
    /// <summary>
    /// What a screen reader reads for the row.
    /// </summary>
    /// <remarks>
    /// A list item with no template-supplied name falls back to the item's own
    /// <see cref="object.ToString"/>, and a record's generated one is its field dump — verified in
    /// the running app, which announced the row as
    /// <c>HistoryEntry { Label = sample, Doc = doc[11 blocks, size 787] }</c>.
    /// </remarks>
    public override string ToString() => Label;
}

public partial class MainWindow : Window
{
    /// <summary>The clipboard format Windows registers for RTF.</summary>
    /// <remarks>
    /// Requested as bytes, not as a string, and that is not a detail. Windows puts RTF on the
    /// clipboard as a byte sequence; asking for it as a string hands back those bytes reinterpreted
    /// as UTF-16, which pairs them up and turns "{\rtf1\ansi" into CJK. The importer then correctly
    /// reports that the text has no RTF header, and a perfectly good paste is lost.
    /// </remarks>
    private static readonly DataFormat<byte[]> ClipboardRtf =
        DataFormat.CreateBytesPlatformFormat("Rich Text Format");

    /// <summary>
    /// The clipboard formats HTML arrives under. Windows uses the CF_HTML envelope under the name
    /// "HTML Format"; everything else uses the MIME type. Both are bytes, for the same reason.
    /// </summary>
    private static readonly DataFormat<byte[]>[] ClipboardHtmlFormats =
    [
        DataFormat.CreateBytesPlatformFormat("HTML Format"),
        DataFormat.CreateBytesPlatformFormat("text/html"),
    ];

    private readonly ObservableCollection<HistoryEntry> _history = [];

    /// <summary>Set while the editor is being loaded from the history, so it is not re-recorded.</summary>
    private bool _restoring;

    public MainWindow()
    {
        InitializeComponent();

        HistoryList.ItemsSource = _history;
        HistoryList.SelectionChanged += OnHistorySelected;

        SampleButton.Click += (_, _) => LoadSample();
        ImportHtmlButton.Click += (_, _) => Show(HtmlImporter.Import(SourceBox.Text ?? string.Empty), "HTML");
        ImportRtfButton.Click += (_, _) => Show(RtfImporter.Import(SourceBox.Text ?? string.Empty), "RTF");
        PasteButton.Click += OnPaste;
        FormatsButton.Click += OnListFormats;

        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextEditor.StateProperty)
            {
                UpdateStatus();
            }
            else if (e.Property == RichTextEditor.MatchCountProperty
                || e.Property == RichTextEditor.CurrentMatchNumberProperty)
            {
                UpdateFindCount();
            }
        };

        WireFind();

        LoadSample();
    }

    /// <summary>The find bar, which is demo chrome rather than part of the control.</summary>
    /// <remarks>
    /// Deliberately here and not in <c>Vellum.Avalonia</c>. The control exposes the operations and
    /// an observable count; what a find bar looks like, where it sits and which keys open it are
    /// the host application's business, and a library that decided them would be wrong somewhere.
    /// </remarks>
    private void WireFind()
    {
        FindBox.TextChanged += (_, _) => Research();
        MatchCaseBox.IsCheckedChanged += (_, _) => Research();
        WholeWordBox.IsCheckedChanged += (_, _) => Research();

        FindNextButton.Click += (_, _) => Editor.FindNext();
        FindPrevButton.Click += (_, _) => Editor.FindPrevious();
        FindCloseButton.Click += (_, _) => CloseFind();

        ReplaceButton.Click += (_, _) => Editor.ReplaceCurrent(ReplaceBox.Text ?? string.Empty);
        ReplaceAllButton.Click += (_, _) =>
            Status($"Replaced {Editor.ReplaceAll(ReplaceBox.Text ?? string.Empty)}.");

        // On the find box rather than the window, so Enter steps through matches only while the
        // user is typing a query — Enter in the document has to keep splitting paragraphs.
        FindBox.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Editor.FindPrevious();
            }
            else
            {
                Editor.FindNext();
            }

            e.Handled = true;
        };

        // Tunnelling, so Ctrl+F and Escape are seen before the editor treats them as text input.
        AddHandler(KeyDownEvent, OnFindKey, RoutingStrategies.Tunnel);
    }

    private void OnFindKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            OpenFind();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && FindBar.IsVisible)
        {
            CloseFind();
            e.Handled = true;
        }
        else if (e.Key == Key.F3 && FindBar.IsVisible)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Editor.FindPrevious();
            }
            else
            {
                Editor.FindNext();
            }

            e.Handled = true;
        }
    }

    /// <remarks>
    /// Seeds the box from the selection, so Ctrl+F on a selected word searches for it, and selects
    /// the box's text so that typing replaces the seed rather than appending to it.
    /// </remarks>
    private void OpenFind()
    {
        if (Editor.View?.SelectedText() is { Length: > 0 } selected && !selected.Contains('\n'))
        {
            FindBox.Text = selected;
        }

        FindBar.IsVisible = true;
        FindBox.Focus();
        FindBox.SelectAll();

        Research();
    }

    private void CloseFind()
    {
        FindBar.IsVisible = false;
        Editor.ClearFind();
        Editor.View?.Focus();
    }

    private void Research() =>
        Editor.Find(
            FindBox.Text ?? string.Empty,
            new SearchOptions
            {
                MatchCase = MatchCaseBox.IsChecked == true,
                WholeWord = WholeWordBox.IsChecked == true,
            });

    private void UpdateFindCount() =>
        FindCount.Text = Editor.MatchCount == 0
            ? (FindBox.Text is { Length: > 0 } ? "none" : string.Empty)
            : $"{Editor.CurrentMatchNumber} of {Editor.MatchCount}";

    /// <summary>Reads the clipboard the way a paste has to: richest format first.</summary>
    /// <remarks>
    /// The order is the whole point. A Word or browser copy puts RTF, HTML and plain text on the
    /// clipboard at once, and taking the first one that answers would get the poorest of the three.
    /// </remarks>
    private async void OnPaste(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;

            if (clipboard is null)
            {
                Status("There is no clipboard on this platform.");
                return;
            }

            using var data = await clipboard.TryGetDataAsync();

            if (data is null)
            {
                Status("The clipboard is empty.");
                return;
            }

            if (await ReadAsync(data, ClipboardRtf, Encoding.Latin1) is { Length: > 0 } rtf)
            {
                SourceBox.Text = rtf;
                Show(RtfImporter.Import(rtf), "clipboard RTF");
                return;
            }

            foreach (var format in ClipboardHtmlFormats)
            {
                if (await ReadAsync(data, format, Encoding.UTF8) is { Length: > 0 } html)
                {
                    SourceBox.Text = html;
                    Show(HtmlImporter.ImportClipboard(html), "clipboard HTML");
                    return;
                }
            }

            if (await data.TryGetTextAsync() is { Length: > 0 } text)
            {
                SourceBox.Text = text;
                Show(new ImportResult(DocumentNode.FromParagraphs(text.Split('\n')), []), "clipboard text");
                return;
            }

            Status("The clipboard holds nothing this demo can read.");
        }
        catch (Exception ex)
        {
            Status($"Reading the clipboard failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Reads one clipboard format and turns its bytes back into text.</summary>
    /// <remarks>
    /// RTF is decoded as Latin-1 because that maps every byte to exactly one character, which keeps
    /// an RTF file intact: it is 7-bit ASCII, and anything outside that range it writes as its own
    /// <c>\'hh</c> escape, which the reader resolves against the font's own code page. CF_HTML is
    /// UTF-8 by specification. The trailing NUL is Windows padding, not content.
    /// </remarks>
    private static async Task<string?> ReadAsync(
        IAsyncDataTransfer data,
        DataFormat<byte[]> format,
        Encoding encoding)
    {
        if (!data.Contains(format))
        {
            return null;
        }

        return await data.TryGetValueAsync(format) is { } bytes
            ? encoding.GetString(bytes).TrimEnd('\0')
            : null;
    }

    /// <summary>Reports what the clipboard is actually offering, which is rarely what one expects.</summary>
    private async void OnListFormats(object? sender, RoutedEventArgs e)
    {
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            using var data = clipboard is null ? null : await clipboard.TryGetDataAsync();

            if (data is null)
            {
                Status("The clipboard is empty.");
                return;
            }

            var names = data.Formats.Select(f => f.Identifier).ToArray();
            DiagnosticsText.Text = names.Length == 0
                ? "The clipboard offers no formats."
                : "The clipboard is offering:\n  " + string.Join("\n  ", names);
            Status($"{names.Length} format(s) on the clipboard.");
        }
        catch (Exception ex)
        {
            Status($"Reading the clipboard failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Puts an import into the editor and shows everything it had to give up.</summary>
    private void Show(ImportResult result, string what)
    {
        Editor.State = EditorState.Create(result.Doc);

        if (!_restoring)
        {
            // Newest first, which is the order a clipboard history is read in.
            _history.Insert(0, new HistoryEntry(
                $"{what}  \u00b7  {DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}",
                result.Doc));
        }

        DiagnosticsText.Text = result.Diagnostics.Length == 0
            ? $"Nothing was dropped reading the {what}."
            : string.Join(
                "\n",
                result.Diagnostics.Select(d => $"[{d.Severity}] {d.Message}"
                    + (string.IsNullOrEmpty(d.Context) ? "" : $"  ({d.Context})")));

        Status($"Read {what}: {result.Doc.Blocks.Length} block(s), {result.Diagnostics.Length} diagnostic(s).");

        // Only once there is a window to take focus. Focusing during construction makes the
        // scroll viewer bring the caret into view before it has been laid out, and the document
        // opens scrolled past its own title.
        if (IsLoaded)
        {
            Editor.View?.Focus();
        }
    }

    private void Status(string message) => StatusText.Text = message;

    /// <summary>Brings an old entry back into the editor, which is what a history is for.</summary>
    private void OnHistorySelected(object? sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry)
        {
            return;
        }

        // Otherwise loading an entry would record it again, and every click would grow the list.
        _restoring = true;

        try
        {
            Show(new ImportResult(entry.Doc, []), "history");
        }
        finally
        {
            _restoring = false;
        }
    }

    private void UpdateStatus()
    {
        var state = Editor.State;
        var view = Editor.View;

        // Counted in laid-out blocks, the same denominator as the index beside it. The
        // document's own Blocks are top-level nodes, so a list of four items counts as one and
        // the pair read as an index out of range.
        Status($"Caret {state.Selection.From}"
            + (state.Selection.From == state.Selection.To ? "" : $"\u2013{state.Selection.To}")
            + (view is null ? "" : $"   |   block {view.CaretBlockIndex + 1} of {view.Blocks.Count}"));
    }

    /// <summary>A 1x1 transparent PNG, so the sample can carry an image without a file beside it.</summary>
    /// <remarks>
    /// A <c>data:</c> URL rather than a relative path because the sanitizer drops a relative
    /// address — it has no document to resolve it against — and a dropped source drops the image
    /// with it. The placeholder renderer draws a box at the declared size either way; the point of
    /// the sample is something to click and drag, not a picture.
    /// </remarks>
    private const string Dot =
        "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8"
        + "z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    /// <summary>
    /// The starting document is produced by the HTML importer rather than assembled by hand, so the
    /// demo exercises the import path before anyone touches a button.
    /// </summary>
    private void LoadSample()
    {
        var html = new StringBuilder()
            .Append("<h1>Vellum</h1>")
            .Append("<p>A rich text editor control for Avalonia. This document was not built in code &mdash; ")
            .Append("it was <i>imported</i>, by the same HTML reader that handles a paste.</p>")
            .Append("<h2>What is here already</h2>")
            .Append("<ul><li>Paragraphs, headings, quotes and code blocks.</li>")
            .Append("<li>Lists, with nesting.</li>")
            .Append("<li>Alignment and indentation.</li>")
            .Append("<li>Undo and redo, with typing coalesced into sensible steps.</li></ul>")
            .Append("<h2>Try it</h2>")
            .Append("<p style='text-align:center'>Copy something out of Word, Outlook or a browser, ")
            .Append("then press <b>Paste from clipboard</b>.</p>")
            .Append("<blockquote>The importer never throws and never trusts what it is given. ")
            .Append("Whatever it could not keep is listed on the right.</blockquote>")
            .Append("<h2>Tables</h2>")
            .Append("<table><tr><th>Feature</th><th>State</th><th>Notes</th></tr>")
            .Append("<tr><td>Import</td><td>HTML and RTF</td><td rowspan='2'>A cell can span rows.</td></tr>")
            .Append("<tr><td>Export</td><td>HTML and RTF</td></tr>")
            .Append("<tr><td colspan='3'>And a cell can span columns.</td></tr></table>")
            .Append("<h2>Images</h2>")
            .Append("<p>An image sits in the text like a very large letter: ")
            .Append($"<img src='{Dot}' width='48' height='32' alt='inline'> &mdash; click it and ")
            .Append("drag a corner to resize it.</p>")
            .Append($"<p><img src='{Dot}' width='160' height='90' alt='block'></p>")
            .Append("<h2>Text that tends to break editors</h2>")
            .Append("<p>Bidi: the Hebrew word \u05e9\u05dc\u05d5\u05dd sits inside this English sentence.</p>")
            .Append("<p>Clusters: \U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466 and e\u0301\u0332 ")
            .Append("should each move the caret as one thing.</p>")
            .Append("<pre>var editor = new RichTextView();\neditor.State = EditorState.Create(doc);</pre>")
            .ToString();

        Show(HtmlImporter.Import(html), "sample");
        SourceBox.Text = html;
    }
}
