using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;
using VellumText.Demo;
using VellumText.Interop.Html;
using VellumText.Interop.Markdown;
using VellumText.Interop.Rtf;

namespace VellumText.Tests.Avalonia;

/// <summary>Smoke tests for the sample application in <c>samples/VellumText.Demo</c>.</summary>
public class DemoTests
{
    /// <summary>
    /// The demo's editor copies and pastes rich text, not just plain text. It registered no
    /// formats, so Ctrl+C and Ctrl+V in the very app meant to show off rich paste carried only
    /// plain text or an image (found in the live window; owner decision to register them).
    /// </summary>
    [AvaloniaFact]
    public void TheDemoEditorReadsAndWritesHtmlRtfAndMarkdown()
    {
        var window = new MainWindow();
        var editor = window.FindControl<RichTextEditor>("Editor");

        Assert.NotNull(editor);
        Assert.Contains(editor.Formats, f => f is HtmlFormat);
        Assert.Contains(editor.Formats, f => f is RtfFormat);
        Assert.Contains(editor.Formats, f => f is MarkdownFormat);
    }
}