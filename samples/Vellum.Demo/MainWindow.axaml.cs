using System.Text;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Vellum.Avalonia;
using Vellum.Interop.Html;
using Vellum.Interop.Rtf;

namespace Vellum.Demo;

public partial class MainWindow : Window
{
    /// <summary>The clipboard format Windows registers for RTF.</summary>
    /// <remarks>
    /// Requested as bytes, not as a string, and that is not a detail. Windows puts RTF on the
    /// clipboard as a byte sequence; asking for it as a string hands back those bytes reinterpreted
    /// as UTF-16, which pairs them up and turns "{\rtf1\ansi" into CJK. The importer then correctly
    /// reports that the text has no RTF header, and a perfectly good paste is lost.
    /// </remarks>
    private static readonly DataFormat<byte[]> RtfFormat =
        DataFormat.CreateBytesPlatformFormat("Rich Text Format");

    /// <summary>
    /// The clipboard formats HTML arrives under. Windows uses the CF_HTML envelope under the name
    /// "HTML Format"; everything else uses the MIME type. Both are bytes, for the same reason.
    /// </summary>
    private static readonly DataFormat<byte[]>[] HtmlFormats =
    [
        DataFormat.CreateBytesPlatformFormat("HTML Format"),
        DataFormat.CreateBytesPlatformFormat("text/html"),
    ];

    public MainWindow()
    {
        InitializeComponent();

        UndoButton.Click += (_, _) => Run(() => View.Undo());
        RedoButton.Click += (_, _) => Run(() => View.Redo());

        BodyButton.Click += (_, _) => Run(() => View.SetParagraphKind(ParagraphKind.Body));
        H1Button.Click += (_, _) => Run(() => View.SetParagraphKind(ParagraphKind.Heading1));
        H2Button.Click += (_, _) => Run(() => View.SetParagraphKind(ParagraphKind.Heading2));
        H3Button.Click += (_, _) => Run(() => View.SetParagraphKind(ParagraphKind.Heading3));
        QuoteButton.Click += (_, _) => Run(() => View.SetParagraphKind(ParagraphKind.Quote));
        CodeButton.Click += (_, _) => Run(() => View.SetParagraphKind(ParagraphKind.Code));

        AlignLeftButton.Click += (_, _) => Run(() => View.SetAlign(TextAlign.Left));
        AlignCenterButton.Click += (_, _) => Run(() => View.SetAlign(TextAlign.Center));
        AlignRightButton.Click += (_, _) => Run(() => View.SetAlign(TextAlign.Right));
        AlignJustifyButton.Click += (_, _) => Run(() => View.SetAlign(TextAlign.Justify));

        IndentButton.Click += (_, _) => Run(() => View.Indent());
        OutdentButton.Click += (_, _) => Run(() => View.Outdent());

        SampleButton.Click += (_, _) => LoadSample();
        ImportHtmlButton.Click += (_, _) => Show(HtmlImporter.Import(SourceBox.Text ?? string.Empty), "HTML");
        ImportRtfButton.Click += (_, _) => Show(RtfImporter.Import(SourceBox.Text ?? string.Empty), "RTF");
        PasteButton.Click += OnPaste;
        FormatsButton.Click += OnListFormats;

        View.PropertyChanged += (_, e) =>
        {
            if (e.Property == RichTextView.StateProperty)
            {
                UpdateStatus();
            }
        };

        LoadSample();
    }

    /// <summary>Runs an editor command, then puts focus back where the typing goes.</summary>
    private void Run(Func<bool> command)
    {
        command();
        UpdateStatus();
        View.Focus();
    }

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

            if (await ReadAsync(data, RtfFormat, Encoding.Latin1) is { Length: > 0 } rtf)
            {
                SourceBox.Text = rtf;
                Show(RtfImporter.Import(rtf), "clipboard RTF");
                return;
            }

            foreach (var format in HtmlFormats)
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
        View.State = EditorState.Create(result.Doc);

        DiagnosticsText.Text = result.Diagnostics.Length == 0
            ? $"Nothing was dropped reading the {what}."
            : string.Join(
                "\n",
                result.Diagnostics.Select(d => $"[{d.Severity}] {d.Message}"
                    + (string.IsNullOrEmpty(d.Context) ? "" : $"  ({d.Context})")));

        Status($"Read {what}: {result.Doc.Blocks.Length} block(s), {result.Diagnostics.Length} diagnostic(s).");
        View.Focus();
    }

    private void Status(string message) => StatusText.Text = message;

    private void UpdateStatus()
    {
        var state = View.State;
        Status($"Caret {state.Selection.From}"
            + (state.Selection.From == state.Selection.To ? "" : $"\u2013{state.Selection.To}")
            + $"   |   {state.Doc.Blocks.Length} block(s)"
            + $"   |   block {View.CaretBlockIndex}");
    }

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