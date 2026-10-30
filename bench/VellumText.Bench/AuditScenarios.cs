using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using VellumText.Avalonia;
using VellumText.Interop.Html;
using VellumText.Interop.Markdown;
using VellumText.Interop.Rtf;

namespace VellumText.Bench;

/// <summary>The scenarios behind the 2026-10-25 codebase audit's performance findings.</summary>
/// <remarks>
/// <para>
/// Each scenario is the reproduction a finding was confirmed with — F-11 <c>a-undofmt</c>, F-12
/// <c>a-undo</c>, F-13 <c>a-findrender</c>, F-14 <c>a-stages</c> and <c>a-sustained</c>, F-15
/// <c>a-structure</c>, F-32 <c>a-selectall</c>, F-47 <c>a-exports</c>, F-71 <c>a-preview</c> — and
/// the rest are the ones later work takes up. They are kept so that a fix is re-measured against
/// the same shape that found the problem, not against a new scenario that may miss it.
/// </para>
/// <para>
/// The times need a quiet machine. The deterministic half of each claim — bytes allocated, and
/// the work counters behind the scans that allocate nothing — is pinned in the test suite by
/// <c>ScalingGuardTests</c>, which is what CI runs.
/// </para>
/// </remarks>
internal static class AuditScenarios
{
    private const double Width = 800;
    private const double Height = 600;
    private static readonly int[] Big = [6_250, 12_500, 25_000, 50_000];

    private static DocumentNode Document(int paragraphs) =>
        new(Enumerable.Range(0, paragraphs)
            .Select(i => (BlockNode)ParagraphNode.FromText(
                $"Paragraph {i} with enough words in it to need shaping and to wrap once."))
            .ToArray());

    /// <summary>Same text, every other word bold, built directly in canonical form.</summary>
    private static DocumentNode MarkedDocument(int paragraphs) =>
        new(Enumerable.Range(0, paragraphs)
            .Select(i => (BlockNode)new ParagraphNode(Alternating(
                $"Paragraph {i} with enough words in it to need shaping and to wrap once.")))
            .ToArray());

    private static InlineContent Alternating(string text)
    {
        var spans = new List<ValueSpan<MarkSet>>();
        var bold = new MarkSet { Style = TextStyle.Bold };
        var word = 0;
        var start = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == ' ')
            {
                if (word % 2 == 1 && i > start)
                {
                    spans.Add(new ValueSpan<MarkSet>(start, i - start, bold));
                }

                word++;
                start = i + 1;
            }
        }

        return InlineContent.Create(text, spans, []);
    }

    private static RichTextView Shown(DocumentNode doc, out Window window)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };
        window = new Window { Content = view, Width = Width, Height = Height };
        window.Show();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));
        return view;
    }

    private static RichTextView Shown(DocumentNode doc) => Shown(doc, out _);

    private static void Layout(Visual v)
    {
        if (v is Layoutable l)
        {
            l.Measure(new Size(Width, Height));
            l.Arrange(new Rect(0, 0, Width, Height));
        }
    }

    private static readonly RenderTargetBitmap Target = new(new PixelSize((int)Width, (int)Height));

    private static void Frame(Visual v) => Target.Render(v);

    // ------------------------------------------------------------------ undo of a typing run

    internal static void UndoTypingRun()
    {
        // Model only: k coalesced keystrokes at the end of an N-paragraph document, then one Undo.
        (EditorState, History) Build(int n, int k)
        {
            var doc = Document(n);
            var state = EditorState.Create(doc, TextSelection.Cursor(doc.ContentSize - 2));
            var history = History.Empty;
            var t0 = DateTimeOffset.UnixEpoch;

            for (var i = 0; i < k; i++)
            {
                var at = state.Selection.Head;
                var tr = state.Transaction().As(TransactionKind.Typing).At(t0.AddMilliseconds(100 * i));
                tr.InsertText(at, "x").SetSelection(TextSelection.Cursor(at + 1));
                history = history.Record(tr);
                state = state.Apply(tr);
            }

            if (history.UndoDepth != 1)
            {
                throw new InvalidOperationException($"expected one coalesced event, got {history.UndoDepth}");
            }

            return (state, history);
        }

        var bySize = Big.Select(n =>
        {
            var built = Build(n, 100);
            return Measure.Run(() => built, b => b.Item2.Undo(b.Item1), n, rounds: 5, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Undo — one coalesced run of 100 typed chars, model only", "paragraphs", bySize,
            "One Ctrl+Z. Budget 16 ms.");

        var byRun = new[] { 25, 50, 100, 200, 400 }.Select(k =>
        {
            var built = Build(50_000, k);
            return Measure.Run(() => built, b => b.Item2.Undo(b.Item1), k, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Undo — 50,000 paragraphs, coalesced run of k typed chars, model only", "keystrokes", byRun,
            "One Ctrl+Z after k keystrokes typed without a 500 ms pause.");

        // Through the view, at the AC12 size: type 100 chars with the real clock, then Ctrl+Z.
        var big = Document(50_000);
        var viewPoint = Measure.Run(
            () =>
            {
                var view = Shown(big);
                view.MoveTo(big.ContentSize - 2, false);
                for (var i = 0; i < 100; i++)
                {
                    view.InsertText("x");
                }

                Layout(view);
                if (view.History.UndoDepth != 1)
                {
                    throw new InvalidOperationException($"view did not coalesce: {view.History.UndoDepth}");
                }

                return view;
            },
            view =>
            {
                view.Undo();
                Layout(view);
            },
            50_000,
            rounds: 3,
            warmup: 1);

        Console.WriteLine($"   VIEW: Undo of 100 coalesced chars at 50,000 paragraphs: {viewPoint.Milliseconds:F1} ms, {viewPoint.Bytes:N0} B");
    }

    // ------------------------------------------------------------------ undo of select-all format

    internal static void UndoFormat()
    {
        var sizes = new[] { 1_000, 2_000, 4_000, 8_000 };

        (EditorState, History) Build(DocumentNode doc)
        {
            var state = EditorState.Create(doc);
            var tr = state.Transaction().As(TransactionKind.Format);
            tr.Step(new AddMarkStep(0, doc.ContentSize, new MarkSet { Style = TextStyle.Italic }, MarkFields.Italic));
            return (state.Apply(tr), History.Empty.Record(tr));
        }

        var apply = sizes.Select(n =>
        {
            var doc = Document(n);
            return Measure.Run(() => EditorState.Create(doc), s =>
            {
                var tr = s.Transaction();
                tr.Step(new AddMarkStep(0, doc.ContentSize, new MarkSet { Style = TextStyle.Italic }, MarkFields.Italic));
                s.Apply(tr);
            }, n, rounds: 5, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Select-all + italic (plain text) — model apply", "paragraphs", apply);

        var plain = sizes.Select(n =>
        {
            var built = Build(Document(n));
            return Measure.Run(() => built, b => b.Item2.Undo(b.Item1), n, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Undo of select-all + italic (plain text), model only", "paragraphs", plain,
            "RestoreMarksStep walks every recorded span for every paragraph.");

        var marked = sizes.Select(n =>
        {
            var built = Build(MarkedDocument(n));
            return Measure.Run(() => built, b => b.Item2.Undo(b.Item1), n, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Undo of select-all + italic (every other word bold), model only", "paragraphs", marked);
    }

    // ------------------------------------------------------------------ long paragraph typing

    internal static void LongParagraph()
    {
        var lengths = new[] { 12_500, 25_000, 50_000, 100_000 };

        foreach (var marked in new[] { false, true })
        {
            var points = lengths.Select(len =>
            {
                var sb = new StringBuilder(len + 16);
                var w = 0;
                while (sb.Length < len)
                {
                    sb.Append("word").Append(w++ % 10).Append(' ');
                }

                var text = sb.ToString(0, len);
                var content = marked ? Alternating(text) : InlineContent.FromText(text);
                var doc = new DocumentNode([new ParagraphNode(content)]);

                return Measure.Run(
                    () =>
                    {
                        var v = Shown(doc);
                        v.MoveTo(1 + (len / 2), false);
                        Layout(v);
                        return v;
                    },
                    v =>
                    {
                        v.InsertText("x");
                        Layout(v);
                    },
                    len,
                    rounds: 5,
                    warmup: 1);
            }).ToList();

            Measure.Report(
                $"AUDIT Keystroke in the middle of one long paragraph ({(marked ? $"every other word bold, ~len/12 spans" : "no marks")})",
                "chars",
                points,
                "Budget 16 ms per keystroke.");
        }

        // Model only, the InlineContent + transaction part.
        var model = lengths.Select(len =>
        {
            var sb = new StringBuilder(len + 16);
            var w = 0;
            while (sb.Length < len)
            {
                sb.Append("word").Append(w++ % 10).Append(' ');
            }

            var doc = new DocumentNode([new ParagraphNode(Alternating(sb.ToString(0, len)))]);
            var state = EditorState.Create(doc);
            return Measure.Run(() => state, s =>
            {
                var tr = s.Transaction();
                tr.InsertText(1 + (len / 2), "x");
                s.Apply(tr);
            }, len, rounds: 7, warmup: 2);
        }).ToList();

        Measure.Report("AUDIT Model insert into one long marked paragraph (no view)", "chars", model);
    }

    // ------------------------------------------------------------------ table

    private static DocumentNode Table(int rows, int cols = 5) =>
        new([
            ParagraphNode.FromText("Before the table."),
            new TableNode(Enumerable.Range(0, rows).Select(r =>
                new TableRowNode(Enumerable.Range(0, cols).Select(c =>
                    TableCellNode.FromText($"Row {r} column {c} value {r * c}"))))),
            ParagraphNode.FromText("After the table."),
        ]);

    internal static void TableCell()
    {
        var rows = new[] { 125, 250, 500, 1_000 };

        var type = rows.Select(r =>
        {
            var doc = Table(r);
            // first cell's text: doc pos: para(17+2=19) + table open 1 + row open 1 + cell open 1 + para open 1 => 23
            return Measure.Run(
                () =>
                {
                    var v = Shown(doc);
                    v.MoveTo(24, false);
                    Layout(v);
                    return v;
                },
                v =>
                {
                    v.InsertText("x");
                    Layout(v);
                },
                r,
                rounds: 5,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Keystroke in the first cell of an R x 5 table (layout only)", "rows", type, "Budget 16 ms.");

        var frame = rows.Select(r =>
        {
            var doc = Table(r);
            var v = Shown(doc, out var window);
            return Measure.Run(() => window, w => Frame(w), r, rounds: 7, warmup: 2);
        }).ToList();

        Measure.Report("AUDIT One rendered frame of a window showing the top of an R x 5 table", "rows", frame,
            "Every caret blink and every scroll is a frame.");

        var control = Big.Take(3).Select(n =>
        {
            var v = Shown(Document(n), out var window);
            return Measure.Run(() => window, w => Frame(w), n, rounds: 7, warmup: 2);
        }).ToList();

        Measure.Report("AUDIT Control: one rendered frame, N plain paragraphs", "paragraphs", control);
    }

    // ------------------------------------------------------------------ find highlights in render

    internal static void FindRender()
    {
        var frames = Big.Select(n =>
        {
            var v = Shown(Document(n), out var window);
            var matches = v.Find("e");
            Layout(window);
            var p = Measure.Run(() => window, w => Frame(w), n, rounds: 5, warmup: 1);
            Console.WriteLine($"   {n:N0} paragraphs: {matches:N0} matches");
            return p;
        }).ToList();

        Measure.Report("AUDIT One rendered frame with a find query \"e\" open", "paragraphs", frames,
            "Caret blink re-renders every 500 ms; scrolling renders every frame. Budget 16 ms.");

        var none = Big.Select(n =>
        {
            var v = Shown(Document(n), out var window);
            v.Find("enough");
            Layout(window);
            return Measure.Run(() => window, w => Frame(w), n, rounds: 5, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT One rendered frame with a find query \"enough\" (1 match/paragraph)", "paragraphs", none);

        var typing = Big.Select(n =>
        {
            var doc = Document(n);
            return Measure.Run(
                () =>
                {
                    var v = Shown(doc);
                    v.Find("enough");
                    v.MoveTo(doc.ContentSize - 2, false);
                    Layout(v);
                    return v;
                },
                v =>
                {
                    v.InsertText("x");
                    Layout(v);
                },
                n,
                rounds: 5,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Keystroke at document end while a find query (\"enough\") is open", "paragraphs", typing,
            "Compare the AC12 keystroke-at-end row (no find open). Budget 16 ms.");
    }

    // ------------------------------------------------------------------ select all

    internal static void SelectAll()
    {
        var marks = Big.Select(n =>
        {
            var v = Shown(Document(n));
            v.SelectAll();
            return Measure.Run(() => v, view => _ = view.SelectionMarks, n, rounds: 5, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT SelectionMarks with everything selected (what RichTextToolbar.Sync reads on every state change)", "paragraphs", marks);

        var del = Big.Select(n =>
        {
            var doc = Document(n);
            return Measure.Run(
                () =>
                {
                    var v = Shown(doc);
                    v.SelectAll();
                    return v;
                },
                v =>
                {
                    v.DeleteSelection();
                    Layout(v);
                },
                n,
                rounds: 3,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Select all + Delete, through the view", "paragraphs", del);

        var bold = Big.Select(n =>
        {
            var doc = Document(n);
            return Measure.Run(
                () =>
                {
                    var v = Shown(doc);
                    v.SelectAll();
                    return v;
                },
                v =>
                {
                    v.ToggleStyle(TextStyle.Bold);
                    Layout(v);
                },
                n,
                rounds: 3,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Select all + Ctrl+B (ToggleStyle), through the view", "paragraphs", bold);
    }

    // ------------------------------------------------------------------ paste

    private static string Html(int paragraphs)
    {
        var sb = new StringBuilder("<html><body>");
        for (var i = 0; i < paragraphs; i++)
        {
            sb.Append("<p>Paragraph ").Append(i)
              .Append(" with <b>enough</b> words in <i>it</i> to need <span style=\"color:#c00\">shaping</span> and to wrap once, <a href=\"https://example.com/").Append(i).Append("\">link</a>.</p>\n");
        }

        return sb.Append("</body></html>").ToString();
    }

    internal static void Paste()
    {
        var sizes = new[] { 1_250, 2_500, 5_000, 10_000 };

        var import = sizes.Select(p =>
        {
            var html = Html(p);
            Console.WriteLine($"   {p:N0} paragraphs = {html.Length / 1024.0 / 1024.0:F2} M chars of HTML");
            return Measure.Run(() => html, h => HtmlFormat.Instance.ImportAsync(h).GetAwaiter().GetResult(), p, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT HTML import (HtmlFormat.Import) of N paragraphs", "paragraphs", import);

        var land = sizes.Select(p =>
        {
            var result = HtmlFormat.Instance.ImportAsync(Html(p)).GetAwaiter().GetResult();
            var host = Document(1);
            return Measure.Run(
                () =>
                {
                    var v = Shown(host);
                    v.MoveTo(5, false);
                    return v;
                },
                v =>
                {
                    v.PasteImport(result);
                    Layout(v);
                },
                p,
                rounds: 3,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT PasteImport of an imported N-paragraph document + layout", "paragraphs", land);

        var direct = sizes.Select(p =>
        {
            var result = HtmlFormat.Instance.ImportAsync(Html(p)).GetAwaiter().GetResult();
            var host = Document(1);
            return Measure.Run(
                () =>
                {
                    var v = Shown(host);
                    v.MoveTo(5, false);
                    return v;
                },
                v =>
                {
                    v.PasteDocument(result.Doc);
                    Layout(v);
                },
                p,
                rounds: 3,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT PasteDocument (no WouldPaste probe) of the same document + layout", "paragraphs", direct,
            "The gap against PasteImport is the probe transaction built and thrown away.");

        var into = Big.Take(3).Select(n =>
        {
            var result = HtmlFormat.Instance.ImportAsync(Html(10)).GetAwaiter().GetResult();
            var host = Document(n);
            return Measure.Run(
                () =>
                {
                    var v = Shown(host);
                    v.MoveTo(5, false);
                    return v;
                },
                v =>
                {
                    v.PasteImport(result);
                    Layout(v);
                },
                n,
                rounds: 3,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT PasteImport of a 10-paragraph fragment into an N-paragraph document", "paragraphs", into);
    }

    // ------------------------------------------------------------------ RTF import

    private static byte[] NoisePng(int side)
    {
        using var bmp = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            var rnd = new Random(side);
            var row = new byte[fb.RowBytes];
            for (var y = 0; y < side; y++)
            {
                rnd.NextBytes(row);
                for (var x = 3; x < row.Length; x += 4)
                {
                    row[x] = 255;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + (y * fb.RowBytes), row.Length);
            }
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, new PngBitmapEncoderOptions());
        return ms.ToArray();
    }

    private static string Rtf(int paragraphs, byte[]? image)
    {
        var sb = new StringBuilder(@"{\rtf1\ansi\ansicpg1252\deff0{\fonttbl{\f0\fswiss Calibri;}}{\colortbl;\red192\green0\blue0;}" + "\n");
        for (var i = 0; i < paragraphs; i++)
        {
            sb.Append(@"\pard\f0\fs22 Paragraph ").Append(i)
              .Append(@" with {\b enough} words in {\i it} to need {\cf1 shaping} and to wrap once.\par").Append('\n');
            if (image is not null && i == paragraphs / 2)
            {
                sb.Append(@"{\pict\pngblip\picw1000\pich1000\picwgoal6000\pichgoal6000 ");
                sb.Append(Convert.ToHexString(image));
                sb.Append("}\\par\n");
            }
        }

        return sb.Append('}').ToString();
    }

    internal static void RtfImport()
    {
        var sizes = new[] { 1_250, 2_500, 5_000, 10_000 };

        var text = sizes.Select(p =>
        {
            var rtf = Rtf(p, null);
            Console.WriteLine($"   {p:N0} paragraphs = {rtf.Length / 1024.0 / 1024.0:F2} M chars of RTF");
            return Measure.Run(() => rtf, r => RtfFormat.Instance.ImportAsync(r).GetAwaiter().GetResult(), p, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT RTF import, N paragraphs, no images", "paragraphs", text);

        var stages = sizes.Select(p =>
        {
            var rtf = Rtf(p, null);
            return Measure.Run(() => rtf, r => RtfPipe.Rtf.ToHtml(r), p, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT RTF stage 1 only: RtfPipe.Rtf.ToHtml", "paragraphs", stages);

        foreach (var side in new[] { 300, 600, 1_000 })
        {
            var png = NoisePng(side);
            var rtf = Rtf(1_000, png);
            var p = Measure.Run(() => rtf, r => RtfFormat.Instance.ImportAsync(r).GetAwaiter().GetResult(), side, rounds: 3, warmup: 1);
            var res = RtfFormat.Instance.ImportAsync(rtf).GetAwaiter().GetResult();
            var imgs = res.Doc.Blocks.OfType<ParagraphNode>().Sum(b => b.Content.Embeds.Length)
                + res.Doc.Blocks.OfType<BlockImageNode>().Count();
            Console.WriteLine($"   RTF 1,000 paragraphs + one {png.Length / 1024.0:F0} KB PNG ({rtf.Length / 1024.0 / 1024.0:F2} M chars): {p.Milliseconds:F1} ms, {p.Bytes:N0} B allocated, images kept: {imgs}, diagnostics: {res.Diagnostics.Length}");
        }
    }

    // ------------------------------------------------------------------ exports

    internal static void Exports()
    {
        var md = Big.Select(n =>
        {
            var doc = MarkedDocument(n);
            return Measure.Run(() => doc, d => MarkdownFormat.Instance.Export(d), n, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Markdown export, N paragraphs, every other word bold", "paragraphs", md);

        var html = Big.Select(n =>
        {
            var doc = MarkedDocument(n);
            return Measure.Run(() => doc, d => HtmlFormat.Instance.Export(d), n, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT HTML export, N paragraphs, every other word bold", "paragraphs", html);

        var rtf = Big.Select(n =>
        {
            var doc = MarkedDocument(n);
            return Measure.Run(() => doc, d => RtfFormat.Instance.Export(d), n, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT RTF export, N paragraphs, every other word bold", "paragraphs", rtf);

        // One long paragraph: Runs() calls MarkAt per character.
        var lengths = new[] { 25_000, 50_000, 100_000, 200_000 };
        var longMd = lengths.Select(len =>
        {
            var sb = new StringBuilder();
            var w = 0;
            while (sb.Length < len)
            {
                sb.Append("word").Append(w++ % 10).Append(' ');
            }

            var doc = new DocumentNode([new ParagraphNode(Alternating(sb.ToString(0, len)))]);
            return Measure.Run(() => doc, d => MarkdownFormat.Instance.Export(d), len, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Markdown export, one paragraph of N chars, every other word bold", "chars", longMd);
    }

    // ------------------------------------------------------------------ images

    internal static void Images()
    {
        var props = new RichTextView().TextStyles.Default;

        foreach (var side in new[] { 300, 600, 1_000, 1_400 })
        {
            var png = NoisePng(side);
            var source = "data:image/png;base64," + Convert.ToBase64String(png);
            var renderer = new BitmapEmbedRenderer();
            var embed = new ImageEmbed(source);
            renderer.Measure(embed, props);

            var p = Measure.Run(() => renderer, r => r.Measure(embed, props), side, rounds: 15, warmup: 3);
            Console.WriteLine($"   cached Measure of a {png.Length / 1024.0:F0} KB PNG data URI ({source.Length / 1024.0 / 1024.0:F2} M chars): {p.Milliseconds * 1000:F1} us, {p.Bytes:N0} B");
        }

        // A view with 4 such images on screen: one frame, nothing changed.
        foreach (var side in new[] { 600, 1_400 })
        {
            var png = NoisePng(side);
            var source = "data:image/png;base64," + Convert.ToBase64String(png);
            var blocks = new List<BlockNode>();
            for (var i = 0; i < 4; i++)
            {
                // One shared source string; every lookup still hashes the whole string.
                blocks.Add(new ParagraphNode(InlineContent.Create($"Image {i} \uFFFC here", [], [new ImageEmbed(source, 120, 90)])));
            }

            blocks.AddRange(Enumerable.Range(0, 50).Select(i => (BlockNode)ParagraphNode.FromText($"Filler {i}")));
            var r4 = new BitmapEmbedRenderer();
            var v = new RichTextView { Embeds = r4, State = EditorState.Create(new DocumentNode(blocks)) };
            var window = new Window { Content = v, Width = Width, Height = Height };
            window.Show();
            Layout(window);
            Frame(window);
            var p = Measure.Run(() => window, w => Frame(w), side, rounds: 9, warmup: 2);
            Console.WriteLine($"   one frame with 4 visible images of {png.Length / 1024.0:F0} KB ({source.Length / 1024.0 / 1024.0:F2} M chars each): {p.Milliseconds:F2} ms, decodes {r4.DecodeCount}, cached {r4.CachedCount}");
        }

        // > CacheLimit distinct images visible: decodes per frame.
        foreach (var count in new[] { 48, 64, 80, 120 })
        {
            var blocks = new List<BlockNode>();
            var embeds = Enumerable.Range(0, count)
                .Select(i => (InlineEmbed)new ImageEmbed("data:image/png;base64," + Convert.ToBase64String(NoisePngSeed(16, i))))
                .ToList();

            var text = string.Concat(Enumerable.Repeat("\uFFFC ", count));
            blocks.Add(new ParagraphNode(InlineContent.Create(text, [], embeds)));
            var renderer = new BitmapEmbedRenderer();
            var v = new RichTextView { Embeds = renderer, State = EditorState.Create(new DocumentNode(blocks)) };
            var window = new Window { Content = v, Width = Width, Height = Height };
            window.Show();
            Layout(window);
            Frame(window);
            var decodesBefore = renderer.DecodeCount;
            var p = Measure.Run(() => window, w => Frame(w), count, rounds: 5, warmup: 0);
            var perFrame = (renderer.DecodeCount - decodesBefore) / 5.0;
            Console.WriteLine($"   {count} distinct images on screen: frame {p.Milliseconds:F2} ms, decodes per frame {perFrame:F1}, cached {renderer.CachedCount}");
        }
    }

    private static byte[] NoisePngSeed(int side, int seed)
    {
        using var bmp = new WriteableBitmap(new PixelSize(side, side), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var fb = bmp.Lock())
        {
            var rnd = new Random(seed);
            var row = new byte[fb.RowBytes];
            for (var y = 0; y < side; y++)
            {
                rnd.NextBytes(row);
                for (var x = 3; x < row.Length; x += 4)
                {
                    row[x] = 255;
                }

                System.Runtime.InteropServices.Marshal.Copy(row, 0, fb.Address + (y * fb.RowBytes), row.Length);
            }
        }

        using var ms = new MemoryStream();
        bmp.Save(ms, new PngBitmapEncoderOptions());
        return ms.ToArray();
    }

    // ------------------------------------------------------------------ history memory

    internal static void HistoryMemory()
    {
        foreach (var n in new[] { 1_000, 50_000 })
        {
            foreach (var coalesce in new[] { true, false })
            {
                var doc = Document(n);
                var state = EditorState.Create(doc, TextSelection.Cursor(doc.ContentSize - 2));
                var history = History.Empty;
                var t0 = DateTimeOffset.UnixEpoch;
                const int K = 2_000;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var before = GC.GetTotalMemory(true);

                for (var i = 0; i < K; i++)
                {
                    var at = state.Selection.Head;
                    var tr = state.Transaction().As(TransactionKind.Typing).At(t0.AddMilliseconds((coalesce ? 100 : 1000) * i));
                    tr.InsertText(at, "x").SetSelection(TextSelection.Cursor(at + 1));
                    history = history.Record(tr);
                    state = state.Apply(tr);
                }

                state = EditorState.Create(doc);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var after = GC.GetTotalMemory(true);
                GC.KeepAlive(history);
                Console.WriteLine($"   History, {n:N0} paragraphs, {K} keystrokes, {(coalesce ? "coalesced" : "separate")} (depth {history.UndoDepth}): retained {(after - before) / (double)K:F0} B/keystroke");
            }
        }
    }

    // ------------------------------------------------------------------ keystroke decomposition

    internal static void Stages()
    {
        foreach (var n in new[] { 12_500, 50_000 })
        {
            var doc = Document(n);
            var at = doc.ContentSize - 2;
            var state = EditorState.Create(doc, TextSelection.Cursor(at));

            Transaction Tr(EditorState s)
            {
                var tr = s.Transaction().As(TransactionKind.Typing);
                tr.InsertText(at, "x").SetSelection(TextSelection.Cursor(at + 1));
                return tr;
            }

            var after = state.Apply(Tr(state)).Doc;

            var rows = new (string Name, Point P)[]
            {
                ("1 model: transaction + Apply", Measure.Run(() => state, s => s.Apply(Tr(s)), n, rounds: 9, warmup: 3)),
                ("2 History.Record", Measure.Run(() => Tr(state), tr => History.Empty.Record(tr), n, rounds: 9, warmup: 3)),
                ("3 DocumentBlocks.Flatten(new doc)", Measure.Run(() => after, d => DocumentBlocks.Flatten(d), n, rounds: 9, warmup: 3)),
                ("4 DocumentBlocks.Leaves(new doc)", Measure.Run(() => after, d => DocumentBlocks.Leaves(d), n, rounds: 9, warmup: 3)),
                ("5 view.InsertText (no layout)", Measure.Run(
                    () => { var v = Shown(doc); v.MoveTo(at, false); Layout(v); return v; },
                    v => v.InsertText("x"), n, rounds: 9, warmup: 3)),
                ("6 view.InsertText + Measure/Arrange", Measure.Run(
                    () => { var v = Shown(doc); v.MoveTo(at, false); Layout(v); return v; },
                    v => { v.InsertText("x"); Layout(v); }, n, rounds: 9, warmup: 3)),
                ("7 view.InsertText + layout + render frame", Measure.Run(
                    () => { var v = Shown(doc, out var w); v.MoveTo(at, false); Layout(w); Frame(w); return (v, w); },
                    t => { t.v.InsertText("x"); Layout(t.w); Frame(t.w); }, n, rounds: 9, warmup: 3)),
            };

            Console.WriteLine();
            Console.WriteLine($"## AUDIT Keystroke at document end, decomposed — {n:N0} paragraphs");
            foreach (var (name, p) in rows)
            {
                Console.WriteLine($"| {name,-44} | {p.Milliseconds,8:F3} ms | {p.Bytes,12:N0} B |");
            }
        }
    }

    // ------------------------------------------------------------------ sustained typing (GC included)

    internal static void Sustained()
    {
        foreach (var n in new[] { 12_500, 50_000 })
        {
            var doc = Document(n);
            var v = Shown(doc, out var w);
            v.MoveTo(doc.ContentSize - 2, false);
            Layout(w);
            Frame(w);

            for (var i = 0; i < 20; i++)
            {
                v.InsertText("x");
                Layout(w);
                Frame(w);
            }

            const int K = 300;
            var times = new double[K];
            var g0 = GC.CollectionCount(0);
            var g1 = GC.CollectionCount(1);
            var g2 = GC.CollectionCount(2);
            var pause = GC.GetTotalPauseDuration();
            var alloc = GC.GetTotalAllocatedBytes();

            for (var i = 0; i < K; i++)
            {
                var sw = Stopwatch.StartNew();
                v.InsertText("x");
                Layout(w);
                Frame(w);
                times[i] = sw.Elapsed.TotalMilliseconds;
            }

            var sorted = times.OrderBy(t => t).ToArray();
            Console.WriteLine();
            Console.WriteLine($"## AUDIT Sustained typing, {K} keystrokes at the end of {n:N0} paragraphs (no forced GC; GC pauses included)");
            Console.WriteLine($"   p50 {sorted[K / 2]:F2} ms | p90 {sorted[K * 9 / 10]:F2} ms | p99 {sorted[K * 99 / 100]:F2} ms | max {sorted[^1]:F2} ms | over 16 ms: {times.Count(t => t > 16)}/{K}");
            Console.WriteLine($"   GCs: gen0 {GC.CollectionCount(0) - g0}, gen1 {GC.CollectionCount(1) - g1}, gen2 {GC.CollectionCount(2) - g2}; GC pause total {(GC.GetTotalPauseDuration() - pause).TotalMilliseconds:F1} ms; allocated {(GC.GetTotalAllocatedBytes() - alloc) / (double)K / 1024 / 1024:F2} MB/keystroke");
        }
    }

    // ------------------------------------------------------------------ markdown edge stripping

    internal static void MarkdownSpaces()
    {
        var sizes = new[] { 5_000, 10_000, 20_000, 40_000 };

        foreach (var (label, make) in new (string, Func<int, string>)[]
        {
            ("leading spaces", k => new string(' ', k) + "x"),
            ("trailing spaces", k => "x" + new string(' ', k)),
            ("leading punctuation", k => new string('!', k) + "x"),
        })
        {
            var points = sizes.Select(k =>
            {
                var text = "a " + make(k) + " b";
                var content = InlineContent.Create(text, [new ValueSpan<MarkSet>(2, k + 1, new MarkSet { Style = TextStyle.Bold })], []);
                var doc = new DocumentNode([new ParagraphNode(content)]);
                return Measure.Run(() => doc, d => MarkdownFormat.Instance.Export(d), k, rounds: 3, warmup: 1);
            }).ToList();

            Measure.Report($"AUDIT Markdown export of one bold run with k {label}", "k", points);
        }

        var control = sizes.Select(k =>
        {
            var text = "a " + new string('y', k) + "x b";
            var content = InlineContent.Create(text, [new ValueSpan<MarkSet>(2, k + 1, new MarkSet { Style = TextStyle.Bold })], []);
            var doc = new DocumentNode([new ParagraphNode(content)]);
            return Measure.Run(() => doc, d => MarkdownFormat.Instance.Export(d), k, rounds: 3, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Markdown export control: same-length bold run of letters", "k", control);
    }

    // ------------------------------------------------------------------ HTML stage breakdown

    internal static void HtmlStages()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        foreach (var p in new[] { 2_500, 10_000 })
        {
            var html = Html(p);
            var rtfHtml = RtfPipe.Rtf.ToHtml(Rtf(p, null));
            var rtfHtmlNoStyle = System.Text.RegularExpressions.Regex.Replace(rtfHtml, "\\sstyle=\"[^\"]*\"", "");
            var parser = new AngleSharp.Html.Parser.HtmlParser();

            var rows = new (string, Point)[]
            {
                ($"hand HTML {html.Length / 1024}K chars: AngleSharp parse only", Measure.Run(() => html, h => parser.ParseDocument(h), p, rounds: 3, warmup: 1)),
                ($"hand HTML: full HtmlFormat.Import", Measure.Run(() => html, h => HtmlFormat.Instance.ImportAsync(h).GetAwaiter().GetResult(), p, rounds: 3, warmup: 1)),
                ($"RtfPipe HTML {rtfHtml.Length / 1024}K chars: AngleSharp parse only", Measure.Run(() => rtfHtml, h => parser.ParseDocument(h), p, rounds: 3, warmup: 1)),
                ($"RtfPipe HTML: full HtmlImporter.Import", Measure.Run(() => rtfHtml, h => HtmlImporter.ImportAsync(h).GetAwaiter().GetResult(), p, rounds: 3, warmup: 1)),
                ($"RtfPipe HTML, style attrs stripped {rtfHtmlNoStyle.Length / 1024}K: full import", Measure.Run(() => rtfHtmlNoStyle, h => HtmlImporter.ImportAsync(h).GetAwaiter().GetResult(), p, rounds: 3, warmup: 1)),
            };

            Console.WriteLine();
            Console.WriteLine($"## AUDIT HTML import stages — {p:N0} paragraphs");
            foreach (var (name, pt) in rows)
            {
                Console.WriteLine($"| {name,-60} | {pt.Milliseconds,9:F1} ms | {pt.Bytes,14:N0} B |");
            }

            Console.WriteLine("   RtfPipe HTML sample: " + rtfHtml.Substring(0, Math.Min(400, rtfHtml.Length)));
        }
    }

    // ------------------------------------------------------------------ preview of a leading table

    internal static void Preview()
    {
        var rows = new[] { 1_000, 10_000, 50_000 };

        var points = rows.Select(r =>
        {
            var doc = new DocumentNode([
                new TableNode(Enumerable.Range(0, r).Select(i =>
                    new TableRowNode([TableCellNode.FromText($"Row {i}"), TableCellNode.FromText("value")]))),
                ParagraphNode.FromText("After the table."),
            ]);

            return Measure.Run(() => doc, d => DocumentPreview.IsLongerThan(d, 80), r, rounds: 5, warmup: 1);
        }).ToList();

        Measure.Report("AUDIT DocumentPreview.IsLongerThan(doc, 80) with an R-row table first", "rows", points,
            "The remark promises it costs eighty characters, not the whole table.");
    }

    // ------------------------------------------------------------------ enter / join at AC12 size

    internal static void StructureKeys()
    {
        var enter = Big.Select(n =>
        {
            var doc = Document(n);
            return Measure.Run(
                () =>
                {
                    var v = Shown(doc);
                    v.MoveTo(doc.ContentSize - 10, false);
                    Layout(v);
                    return v;
                },
                v =>
                {
                    v.Split();
                    Layout(v);
                },
                n,
                rounds: 5,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Enter (split) near the document end, through the view", "paragraphs", enter, "Budget 16 ms.");

        var join = Big.Select(n =>
        {
            var doc = Document(n);
            var lastStart = doc.Resolve(doc.ContentSize - 2).Start(1);
            return Measure.Run(
                () =>
                {
                    var v = Shown(doc);
                    v.MoveTo(lastStart, false);
                    Layout(v);
                    return v;
                },
                v =>
                {
                    v.Backspace();
                    Layout(v);
                },
                n,
                rounds: 5,
                warmup: 1);
        }).ToList();

        Measure.Report("AUDIT Backspace at the start of the last paragraph (join), through the view", "paragraphs", join, "Budget 16 ms.");
    }
}
