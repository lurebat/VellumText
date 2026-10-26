using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using VellumText.Avalonia;
using VellumText.Interop.Html;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What a host configured on <see cref="RichTextEditor"/> survives the editor being retemplated.
/// </summary>
/// <remarks>
/// The forwarders exist so a host can configure the editor in code before it is on screen, and
/// doing that applies the default template to get a view. An application style that then gives
/// the editor its own chrome — the documented point of the shell — makes a new view, and every
/// forwarded setting lived only on the old one: registered formats, the image store, the renderer
/// and styles, the content policy, and the undo history all silently reverted to defaults.
/// </remarks>
public class RetemplateTests
{
    private static FuncControlTemplate<RichTextEditor> Chrome() =>
        new((_, scope) =>
        {
            var view = new RichTextView { Name = RichTextEditor.ViewPart };

            view.RegisterInNameScope(scope);

            return new Border { Child = new ScrollViewer { Content = view } };
        });

    [AvaloniaFact]
    public void Constructor_time_setup_survives_an_application_style_that_sets_the_template()
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var store = new DataUriImageStore();
        var embeds = new BitmapEmbedRenderer();
        var styles = new TextStyleResolver(new global::Avalonia.Media.Typeface(global::Avalonia.Media.FontFamily.Default), 13, global::Avalonia.Media.Brushes.Black);
        var vetoes = 0;
        var imported = 0;
        var refused = 0;

        editor.Formats.Add(HtmlFormat.Instance);
        editor.ImageStore = store;
        editor.Embeds = embeds;
        editor.TextStyles = styles;
        editor.SyntaxHighlighter = JsonHighlighter.Instance;
        editor.ContentImporting += (_, e) =>
        {
            vetoes++;
            e.Cancel = vetoes == 1;
        };
        editor.ContentImported += (_, _) => imported++;
        editor.ImageRefused += (_, _) => refused++;

        var early = editor.EnsureView();
        var window = new Window { Width = 400, Height = 300 };

        window.Styles.Add(new Style(x => x.OfType<RichTextEditor>())
        {
            Setters = { new Setter(TemplatedControl.TemplateProperty, Chrome()) },
        });
        window.Content = editor;
        window.Show();

        var late = editor.EnsureView();

        Assert.NotSame(early, late);
        Assert.Contains(HtmlFormat.Instance, late.Formats);
        Assert.Same(store, late.ImageStore);
        Assert.Same(embeds, late.Embeds);
        Assert.Same(styles, late.TextStyles);
        Assert.Same(JsonHighlighter.Instance, late.SyntaxHighlighter);

        // The policy still vetoes, and the reporting events still report.
        Assert.False(late.PasteImport(new ImportResult(DocumentNode.FromParagraphs("refused"))));
        Assert.Equal(1, vetoes);
        Assert.True(late.PasteImport(new ImportResult(DocumentNode.FromParagraphs("accepted"))));
        Assert.Equal(1, imported);

        late.ImageStore = new DataUriImageStore(maximumBytes: 1);
        late.InsertImage([1, 2, 3], "image/png");
        Assert.Equal(1, refused);
    }

    [AvaloniaFact]
    public void Retemplating_keeps_the_undo_history()
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var window = new Window { Width = 400, Height = 300, Content = editor };

        window.Show();

        var early = editor.EnsureView();

        early.MoveTo(early.BlockStart + 5, extend: false);
        Assert.True(early.InsertText("!"));
        Assert.True(editor.CanUndo);

        editor.Template = Chrome();
        editor.ApplyTemplate();

        Assert.NotSame(early, editor.View);
        Assert.True(editor.CanUndo, "undo history lost on retemplate");
        Assert.True(editor.Undo());
        Assert.Equal("hello", DocumentText.Of(editor.Document));
    }

    [AvaloniaFact]
    public void A_handler_removed_after_retemplating_is_removed_from_the_new_view()
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var vetoes = 0;
        EventHandler<RichTextView.ContentImportingEventArgs> veto = (_, e) =>
        {
            vetoes++;
            e.Cancel = true;
        };

        editor.ContentImporting += veto;
        editor.EnsureView();

        editor.Template = Chrome();
        editor.ApplyTemplate();

        editor.ContentImporting -= veto;

        Assert.True(editor.EnsureView().PasteImport(new ImportResult(DocumentNode.FromParagraphs("lands"))));
        Assert.Equal(0, vetoes);
    }

    [AvaloniaFact]
    public void The_old_view_no_longer_raises_the_hosts_handlers()
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var imported = 0;

        editor.ContentImported += (_, _) => imported++;

        var early = editor.EnsureView();

        editor.Template = Chrome();
        editor.ApplyTemplate();

        early.PasteImport(new ImportResult(DocumentNode.FromParagraphs("into the discarded view")));

        Assert.Equal(0, imported);
    }

    [AvaloniaFact]
    public void The_old_floating_toolbar_is_detached_from_the_editor()
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("hello")) };
        var window = new Window { Width = 400, Height = 300, Content = editor };

        window.Show();

        var old = editor.SelectionToolbar;

        Assert.NotNull(old);
        Assert.Same(editor, old.Editor);

        editor.Template = Chrome();
        editor.ApplyTemplate();

        Assert.Null(old.Editor);
    }
}
