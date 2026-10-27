using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// An exporter that throws during a copy costs its own flavour, and is reported rather than
/// silent (owner decision 2026-10-27, on <c>RichTextClipboard.WriteAsync</c>).
/// </summary>
/// <remarks>
/// The copy already went ahead without the failing format; nothing said so, so a broken exporter
/// only showed as a paste elsewhere arriving with less formatting than it should. Ctrl+C still
/// never throws.
/// </remarks>
public class ExportFailureTests
{
    private sealed class Throwing : IDocumentExporter
    {
        public string Format => "boom";

        public string MediaType => "text/x-boom";

        public Encoding? TextEncoding => Encoding.UTF8;

        public void Export(Stream destination, DocumentNode doc) =>
            throw new InvalidOperationException("the exporter is broken");
    }

    private sealed class Listener : TraceListener
    {
        public StringBuilder Written { get; } = new();

        public override void Write(string? message) => Written.Append(message);

        public override void WriteLine(string? message) => Written.AppendLine(message);
    }

    private static (Window Window, T Control) Shown<T>(T control)
        where T : Control
    {
        var window = new Window { Content = control, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return (window, control);
    }

    [AvaloniaFact]
    public async Task A_copy_with_a_throwing_exporter_still_copies_the_rest_and_reports_it()
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var reported = new List<ExportFailedEventArgs>();
        var listener = new Listener();

        view.Formats.Add(new Throwing());
        view.Formats.Add(HtmlFormat.Instance);
        view.ExportFailed += (_, e) => reported.Add(e);

        var (window, _) = Shown(view);

        view.SelectAll();
        Trace.Listeners.Add(listener);

        try
        {
            Assert.True(await view.CopyAsync());
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }

        var failure = Assert.Single(reported);

        Assert.Equal("boom", failure.Format);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Contains("boom exporter threw", listener.Written.ToString(), StringComparison.Ordinal);

        // The other flavours made it.
        using var data = await window.Clipboard!.TryGetDataAsync();

        Assert.NotNull(data);
        Assert.Equal("hello", await data.TryGetTextAsync());
        Assert.NotNull(await data.TryGetValueAsync(RichTextClipboard.FlavorFor("html").DataFormat));
    }

    [AvaloniaFact]
    public async Task The_viewer_reports_a_throwing_exporter_too()
    {
        var viewer = new RichTextViewer { Document = DocumentNode.FromParagraphs("hello") };
        var reported = new List<ExportFailedEventArgs>();

        viewer.Formats.Add(new Throwing());
        viewer.ExportFailed += (_, e) => reported.Add(e);
        Shown(viewer);

        viewer.SelectAll();

        Assert.True(await viewer.CopyAsync());
        Assert.Equal("boom", Assert.Single(reported).Format);
    }

    /// <summary>A subscription made through the editor survives the editor being retemplated.</summary>
    [AvaloniaFact]
    public void The_editor_forwards_the_report_across_a_retemplate()
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var reported = 0;

        editor.Formats.Add(new Throwing());
        editor.ExportFailed += (_, _) => reported++;

        var (window, _) = Shown(editor);
        var before = editor.EnsureView();

        editor.Template = RetemplateTests.Chrome();
        editor.ApplyTemplate();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        Assert.NotSame(before, editor.EnsureView());

        // The payload a copy writes, built by the new view: no clipboard is needed to see whether the
        // report reaches a handler subscribed before the retemplate.
        var view = editor.EnsureView();

        view.SelectAll();
        view.BuildCopyData(view.SelectedText());

        Assert.Equal(1, reported);
    }

    [Fact]
    public void Building_the_payload_reports_each_failing_exporter_and_keeps_the_rest()
    {
        var failed = new List<string>();

        var data = RichTextClipboard.Build(
            DocumentNode.FromParagraphs("hello"),
            [new Throwing(), HtmlFormat.Instance],
            exportFailed: (exporter, _) => failed.Add(exporter.Format));

        Assert.Equal(["boom"], failed);
        Assert.Single(data.Items);
    }
}
