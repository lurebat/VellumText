using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The automation surface: replacing the whole value, hearing block images, and being told when
/// the control becomes read-only.
/// </summary>
public class AutomationValueTests
{
    private static RichTextView Editor(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc), MinHeight = 300 };
        var window = new Window { Content = view, Width = 500, Height = 400 };

        window.Show();

        return view;
    }

    private static IValueProvider Peer(Control control) =>
        (IValueProvider)ControlAutomationPeer.CreatePeerForElement(control);

    private static ImageEmbed Image(string? alt) => new("data:image/png;base64,AAAA", altText: alt);

    /// <summary>
    /// SetValue selected all and pasted; on a document that is one table, select-all is a
    /// rectangle of cells, which paste refuses — so the call did nothing and said nothing.
    /// </summary>
    [AvaloniaFact]
    public void Setting_the_value_of_a_table_only_document_replaces_it()
    {
        var editor = Editor(new DocumentNode([
            new TableNode([new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")])]),
        ]));
        var peer = Peer(editor);

        peer.SetValue("hello");

        Assert.Equal("hello", peer.Value);
        Assert.IsType<ParagraphNode>(Assert.Single(editor.State.Doc.Blocks));
    }

    /// <summary>Select-all spans text positions, so a block image at either edge survived a "replace everything".</summary>
    [AvaloniaFact]
    public void Setting_the_value_of_a_document_with_edge_images_replaces_them_too()
    {
        var editor = Editor(new DocumentNode([
            new BlockImageNode(Image("x")),
            ParagraphNode.FromText("old"),
            new BlockImageNode(Image("y")),
        ]));
        var peer = Peer(editor);

        peer.SetValue("hello\nworld");

        Assert.Equal("hello\nworld", peer.Value);
        Assert.All(editor.State.Doc.Blocks, b => Assert.IsType<ParagraphNode>(b));
        Assert.Equal(2, editor.State.Doc.Blocks.Length);
    }

    [AvaloniaFact]
    public void Setting_the_value_is_one_undo()
    {
        var editor = Editor(DocumentNode.FromParagraphs("old"));
        var peer = Peer(editor);

        peer.SetValue("new");
        Assert.True(editor.Undo());

        Assert.Equal("old", peer.Value);
    }

    [AvaloniaFact]
    public void Setting_an_empty_value_leaves_one_empty_paragraph()
    {
        var editor = Editor(DocumentNode.FromParagraphs("old", "older"));
        var peer = Peer(editor);

        peer.SetValue(string.Empty);

        Assert.Equal(string.Empty, peer.Value);
        Assert.Single(editor.State.Doc.Blocks);
    }

    /// <summary>A screen reader heard an inline image's alt text but not a block image's.</summary>
    [AvaloniaFact]
    public void A_block_images_alt_text_reaches_the_automation_value()
    {
        var editor = Editor(new DocumentNode([
            ParagraphNode.FromText("Sales"),
            new BlockImageNode(Image("Bar chart of 2025 sales")),
            ParagraphNode.FromText("End"),
        ]));

        Assert.Equal("Sales\nBar chart of 2025 sales\nEnd", Peer(editor).Value);
    }

    [AvaloniaFact]
    public void A_block_image_without_alt_text_adds_nothing()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("Sales"), new BlockImageNode(Image(null)), ParagraphNode.FromText("End")]);

        Assert.Equal("Sales\nEnd", DocumentText.Of(doc));
    }

    [AvaloniaFact]
    public void Toggling_IsReadOnly_announces_the_value_patterns_read_only_change()
    {
        var view = Editor(DocumentNode.FromParagraphs("hello"));
        var peer = ControlAutomationPeer.CreatePeerForElement(view);
        var provider = Assert.IsAssignableFrom<IValueProvider>(peer);
        var changes = new List<AutomationPropertyChangedEventArgs>();

        Assert.False(provider.IsReadOnly);

        peer.PropertyChanged += (_, e) => changes.Add(e);
        view.IsReadOnly = true;

        Assert.True(provider.IsReadOnly);

        var change = Assert.Single(changes, c => c.Property == ValuePatternIdentifiers.IsReadOnlyProperty);
        Assert.Equal(false, change.OldValue);
        Assert.Equal(true, change.NewValue);

        changes.Clear();
        view.IsReadOnly = false;

        Assert.Contains(changes, c => c.Property == ValuePatternIdentifiers.IsReadOnlyProperty && Equals(c.NewValue, false));
    }
}
