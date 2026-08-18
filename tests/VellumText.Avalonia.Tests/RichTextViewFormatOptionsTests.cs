using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Import options reaching the importer through the seam a host is actually told to use.
/// </summary>
/// <remarks>
/// The <c>HtmlFormatOptionsTests</c> unit tests call the format directly. This is the other half,
/// and the half that matters: a host configures the format, hands it to <c>Formats</c>, and never
/// touches the importer again. Nothing between the registration and the parse may quietly
/// substitute the defaults — least of all for
/// <see cref="HtmlImportOptions.AllowRemoteImages"/>, where the failure is a document phoning home
/// that the host believed it had switched off, or an image the host deliberately allowed silently
/// vanishing.
/// </remarks>
public class RichTextViewFormatOptionsTests
{
    [AvaloniaFact]
    public void A_paste_keeps_a_remote_image_when_the_registered_format_allows_it()
    {
        var view = Shown();

        view.Formats.Add(HtmlFormat.Instance.With(new HtmlImportOptions { AllowRemoteImages = true }));

        Drop(view, "<p><img src='https://cdn.test/a.png'></p>");

        var image = Assert.Single(Images(view));

        Assert.Equal("https://cdn.test/a.png", image.Source);
    }

    /// <summary>
    /// The companion. Without it the test above would pass just as well if the registration seam
    /// were ignored entirely and something upstream had started allowing remote images.
    /// </summary>
    [AvaloniaFact]
    public void A_paste_still_drops_a_remote_image_when_the_registered_format_is_the_default()
    {
        var view = Shown();

        view.Formats.Add(HtmlFormat.Instance);

        Drop(view, "<p><img src='https://cdn.test/a.png'></p>");

        Assert.Empty(Images(view));
    }

    /// <summary>
    /// <c>BaseUri</c> is the reason a pasted image resolves at all, and it is set once at
    /// registration because a host knows where its content comes from and the control does not.
    /// </summary>
    [AvaloniaFact]
    public void A_relative_link_resolves_against_the_base_the_registered_format_carries()
    {
        var view = Shown();

        view.Formats.Add(HtmlFormat.Instance.With(
            new HtmlImportOptions { BaseUri = new Uri("https://example.com/dir/page") }));

        Drop(view, "<p><a href='/somewhere'>click</a></p>");

        Assert.Contains(Links(view), href => href == "https://example.com/somewhere");
    }

    [AvaloniaFact]
    public void A_relative_link_is_still_dropped_when_the_registered_format_is_the_default()
    {
        var view = Shown();

        view.Formats.Add(HtmlFormat.Instance);

        Drop(view, "<p><a href='/somewhere'>click</a></p>");

        Assert.Empty(Links(view));
    }

    /// <summary>
    /// The shell forwards <c>Formats</c>, so a configured format registered before templating is
    /// the same one the view ends up importing with.
    /// </summary>
    [AvaloniaFact]
    public void A_format_configured_on_the_shell_before_templating_is_the_one_the_view_uses()
    {
        var editor = new RichTextEditor();
        var configured = HtmlFormat.Instance.With(new HtmlImportOptions { AllowRemoteImages = true });

        editor.Formats.Add(configured);

        Assert.Same(configured, Assert.Single(editor.EnsureView().Formats));
    }

    private static RichTextView Shown()
    {
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var window = new Window { Content = view, Width = 400, Height = 300 };

        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        return view;
    }

    private static void Drop(RichTextView view, string html)
    {
        var item = new DataTransferItem();

        item.Set(RichTextClipboard.FlavorFor("html").DataFormat, Encoding.UTF8.GetBytes(html));

        var data = new DataTransfer();

        data.Add(item);

        view.RaiseEvent(new DragEventArgs(
            DragDrop.DropEvent,
            data,
            view,
            new Point(4, 4),
            KeyModifiers.None));
    }

    private static IEnumerable<ImageEmbed> Images(RichTextView view) =>
        view.State.Doc.Blocks
            .OfType<ParagraphNode>()
            .SelectMany(p => p.Content.Embeds)
            .OfType<ImageEmbed>();

    private static IEnumerable<string> Links(RichTextView view) =>
        view.State.Doc.Blocks
            .OfType<ParagraphNode>()
            .SelectMany(p => Enumerable
                .Range(0, p.Content.Length)
                .Select(i => p.Content.MarkAt(i).Link?.Href))
            .OfType<string>()
            .Distinct();
}
