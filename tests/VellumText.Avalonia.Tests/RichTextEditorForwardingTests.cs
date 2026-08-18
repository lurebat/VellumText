using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What a host can reach on the shell without first proving it has been templated.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RichTextEditor"/> forwarded five of the view's surfaces and not the rest, which
/// pushed every host to <c>Editor.View!</c> — a null-forgiving operator on a property that
/// genuinely is null until the template is applied. For a command that is fine, because a command
/// runs in response to a user action; for setup it is a race, and the losing side is silent. A
/// format registered on a view that does not exist yet throws, and an event subscribed on one is
/// simply never heard from.
/// </para>
/// <para>
/// So the setup surfaces are forwarded and the commands are not, with
/// <see cref="RichTextEditor.EnsureView"/> as the honest way to reach the rest.
/// </para>
/// </remarks>
public class RichTextEditorForwardingTests
{
    [AvaloniaFact]
    public void FormatsCanBeRegisteredBeforeTheControlIsAnywhere()
    {
        // Never shown, never measured, never added to a window: the constructor-time case.
        var editor = new RichTextEditor();

        editor.Formats.Add(HtmlFormat.Instance);

        Assert.Contains(HtmlFormat.Instance, editor.EnsureView().Formats);
    }

    [AvaloniaFact]
    public void FormatsRegisteredOnTheShellAreTheOnesTheViewCopiesWith()
    {
        var editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("hello")),
        };

        editor.Formats.Add(HtmlFormat.Instance);

        var view = editor.EnsureView();

        view.State = EditorState.Create(
            view.State.Doc, TextSelection.Create(view.State.Doc, 1, view.State.Doc.ContentSize - 1));

        IDataTransferItem item = Assert.Single(view.BuildCopyData("hello").Items);

        Assert.NotNull(item.TryGetValue(RichTextClipboard.FlavorFor("html").DataFormat));
    }

    [AvaloniaFact]
    public void AnImportReportSubscribedBeforeTemplatingStillArrives()
    {
        var editor = new RichTextEditor();
        var heard = 0;

        editor.ContentImported += (_, _) => heard++;

        var window = new Window { Width = 400, Height = 200, Content = editor };

        window.Show();

        Assert.True(editor.EnsureView().PasteImport(
            new ImportResult(DocumentNode.FromParagraphs("pasted"))));

        Assert.Equal(1, heard);
    }

    [AvaloniaFact]
    public void ThePresentationSurfacesAreReachableBeforeTemplating()
    {
        var editor = new RichTextEditor();
        var embeds = new PlaceholderEmbedRenderer();

        editor.Embeds = embeds;
        editor.SyntaxTheme = SyntaxTheme.Light;
        editor.SyntaxHighlighter = null;

        var view = editor.EnsureView();

        Assert.Same(embeds, view.Embeds);
        Assert.Same(SyntaxTheme.Light, view.SyntaxTheme);
        Assert.Same(embeds, editor.Embeds);
    }

    [AvaloniaFact]
    public void TheImageStoreIsReachableBeforeTemplating()
    {
        // A security control: it decides which sources a pasted document may name. A host that
        // could only set it after templating would have a window in which the default applies.
        var editor = new RichTextEditor();
        var store = new DataUriImageStore();

        editor.ImageStore = store;

        Assert.Same(store, editor.EnsureView().ImageStore);
    }

    [AvaloniaFact]
    public void EnsureViewIsTheSameViewTheTemplateProduces()
    {
        var editor = new RichTextEditor();

        Assert.Null(editor.View);

        var ensured = editor.EnsureView();

        Assert.Same(ensured, editor.View);
        Assert.Same(ensured, editor.EnsureView());
    }

    [AvaloniaFact]
    public void EnsureViewSurvivesBeingShownAfterwards()
    {
        // Templating early must not make the control template twice or leave the shell talking to
        // a view nobody can see.
        var editor = new RichTextEditor();
        var early = editor.EnsureView();
        var window = new Window { Width = 400, Height = 200, Content = editor };

        window.Show();

        Assert.Same(early, editor.View);
    }
}
