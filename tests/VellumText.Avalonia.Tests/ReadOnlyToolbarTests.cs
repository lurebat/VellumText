using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// The toolbar's commands are disabled while the editor they are bound to is read-only (OD-10),
/// and come back when it is editable again.
/// </summary>
/// <remarks>
/// The bar's rule is that commands are always enabled, because disabling half of it whenever the
/// caret lands somewhere awkward reads as broken. A read-only editor is not an awkward caret: every
/// command is refused, so a bar offering them all on a locked document is offering nothing.
/// </remarks>
public class ReadOnlyToolbarTests
{
    private static (RichTextEditor Editor, RichTextToolbar Toolbar) Shown(bool readOnly)
    {
        var editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("locked")), IsReadOnly = readOnly };
        var toolbar = new RichTextToolbar { Editor = editor };
        var window = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { toolbar, editor } } };

        window.Show();
        editor.EnsureView().SelectAll();

        return (editor, toolbar);
    }

    private static IEnumerable<(string Name, ICommand Command, object? Parameter)> Commands(RichTextToolbar toolbar) =>
    [
        (nameof(toolbar.ToggleStyleCommand), toolbar.ToggleStyleCommand, TextStyle.Bold),
        (nameof(toolbar.SetParagraphKindCommand), toolbar.SetParagraphKindCommand, ParagraphKind.Heading1),
        (nameof(toolbar.ToggleParagraphKindCommand), toolbar.ToggleParagraphKindCommand, ParagraphKind.Heading1),
        (nameof(toolbar.SetAlignCommand), toolbar.SetAlignCommand, TextAlign.Center),
        (nameof(toolbar.ToggleListCommand), toolbar.ToggleListCommand, ListKind.Unordered),
        (nameof(toolbar.SetForegroundCommand), toolbar.SetForegroundCommand, "#FF0000"),
        (nameof(toolbar.SetHighlightCommand), toolbar.SetHighlightCommand, "#FFFF00"),
        (nameof(toolbar.SetFontFamilyCommand), toolbar.SetFontFamilyCommand, "Arial"),
        (nameof(toolbar.SetFontSizeCommand), toolbar.SetFontSizeCommand, 12.0),
        (nameof(toolbar.IndentCommand), toolbar.IndentCommand, null),
        (nameof(toolbar.OutdentCommand), toolbar.OutdentCommand, null),
        (nameof(toolbar.ClearFormattingCommand), toolbar.ClearFormattingCommand, null),
        (nameof(toolbar.SetLinkCommand), toolbar.SetLinkCommand, "https://example.test/"),
        (nameof(toolbar.UndoCommand), toolbar.UndoCommand, null),
        (nameof(toolbar.RedoCommand), toolbar.RedoCommand, null),
    ];

    [AvaloniaFact]
    public void Every_command_is_disabled_on_a_read_only_editor()
    {
        var (_, toolbar) = Shown(readOnly: true);

        var enabled = Commands(toolbar).Where(c => c.Command.CanExecute(c.Parameter)).Select(c => c.Name).ToList();

        Assert.Empty(enabled);
    }

    [AvaloniaFact]
    public void Commands_come_back_and_say_so_when_the_editor_becomes_editable()
    {
        var (editor, toolbar) = Shown(readOnly: true);
        var raised = new HashSet<string>();

        foreach (var (name, command, _) in Commands(toolbar))
        {
            command.CanExecuteChanged += (_, _) => raised.Add(name);
        }

        editor.IsReadOnly = false;

        Assert.All(Commands(toolbar), c => Assert.True(c.Command.CanExecute(c.Parameter), c.Name));
        Assert.Equal(Commands(toolbar).Select(c => c.Name).Order(), raised.Order());

        raised.Clear();
        editor.IsReadOnly = true;

        Assert.All(Commands(toolbar), c => Assert.False(c.Command.CanExecute(c.Parameter), c.Name));
        Assert.Equal(Commands(toolbar).Select(c => c.Name).Order(), raised.Order());
    }

    [AvaloniaFact]
    public void Commands_are_enabled_on_an_editable_editor_wherever_the_caret_is()
    {
        var (_, toolbar) = Shown(readOnly: false);

        Assert.All(Commands(toolbar), c => Assert.True(c.Command.CanExecute(c.Parameter), c.Name));
    }

    [AvaloniaFact]
    public void Rebinding_to_an_editable_editor_enables_the_commands()
    {
        var (_, toolbar) = Shown(readOnly: true);
        var raised = 0;

        toolbar.ToggleStyleCommand.CanExecuteChanged += (_, _) => raised++;
        toolbar.Editor = new RichTextEditor { State = EditorState.Create(DocumentNode.FromParagraphs("free")) };

        Assert.True(toolbar.ToggleStyleCommand.CanExecute(TextStyle.Bold));
        Assert.Equal(1, raised);
    }
}
