using Avalonia.Controls;
using Avalonia.Input;

namespace VellumText.Avalonia;

/// <summary>
/// The menu a right-click puts up.
/// </summary>
/// <remarks>
/// Built in code rather than declared in a theme, for the same reason the editor's default template
/// is: a control that only works once someone remembers to include a resource dictionary is a
/// control that does not work. Replacing <see cref="global::Avalonia.Controls.Control.ContextFlyout"/>
/// with a richer menu is a single assignment, so nothing here forecloses that.
/// </remarks>
public partial class RichTextView
{
    private MenuItem? _cutItem;
    private MenuItem? _copyItem;
    private MenuItem? _pasteItem;
    private MenuItem? _tableItem;

    private MenuFlyout BuildContextFlyout()
    {
        _cutItem = Item("Cut", "Ctrl+X", () => _ = CutAsync());
        _copyItem = Item("Copy", "Ctrl+C", () => _ = CopyAsync());
        _pasteItem = Item("Paste", "Ctrl+V", () => _ = PasteAsync());
        _tableItem = BuildTableItem();

        var selectAll = Item("Select All", "Ctrl+A", () => SelectAll());
        var flyout = new MenuFlyout
        {
            ItemsSource = new object[]
            {
                _cutItem, _copyItem, _pasteItem, new Separator(), selectAll,
                new Separator(), _tableItem,
            },
        };

        flyout.Opening += (_, _) => SyncContextFlyout();

        return flyout;
    }

    /// <summary>
    /// Greys out what cannot work. Read when the menu opens, because the selection changes
    /// constantly and a menu that always offers everything informs nobody.
    /// </summary>
    /// <remarks>
    /// <b>Greying and hiding answer two different questions, and read-only is the second.</b>
    /// Cut is greyed with no selection because it is always meaningful and only sometimes
    /// possible — that tracks the caret, which moves constantly, so hiding it would flicker. A
    /// read-only control refuses every edit for as long as the host says so, and that changes
    /// only when the host decides, so hiding cannot flicker. A permanently greyed Cut on a
    /// preview pane teaches the reader nothing except that the application looks broken.
    ///
    /// Copy and Select All stay: both are reads, and taking Copy away is what made a read-only
    /// preview useless in the first place.
    /// </remarks>
    internal void SyncContextFlyout()
    {
        var hasSelection = !_state.Selection.IsEmpty;
        var editable = !IsReadOnly;

        if (_cutItem is not null)
        {
            _cutItem.IsVisible = editable;
            _cutItem.IsEnabled = hasSelection;
        }

        if (_copyItem is not null)
        {
            _copyItem.IsEnabled = hasSelection;
        }

        if (_pasteItem is not null)
        {
            _pasteItem.IsVisible = editable;
            _pasteItem.IsEnabled = TopLevel.GetTopLevel(this)?.Clipboard is not null;
        }

        if (_tableItem is not null)
        {
            // Hidden rather than disabled. Cut and Paste are always meaningful and only
            // sometimes possible, so greying them says something; a table submenu in a document
            // with no table in it says nothing at all, and its separator would leave a gap.
            _tableItem.IsVisible = IsInTable && editable;
        }
    }

    /// <summary>
    /// The table submenu.
    /// </summary>
    /// <remarks>
    /// Every entry stays enabled while the caret is in a table, even when it cannot apply — merge
    /// with no rectangle selected, split on a cell that was never merged. Those two change with
    /// every caret move, and a menu whose entries flicker in and out reads as broken; a command
    /// that quietly does nothing reads as a command that did not apply. This is the same rule the
    /// toolbar follows.
    /// </remarks>
    private MenuItem BuildTableItem() => new()
    {
        Header = "Table",
        ItemsSource = new object[]
        {
            Item("Insert Row Above", () => InsertRowAbove()),
            Item("Insert Row Below", () => InsertRowBelow()),
            Item("Insert Column Left", () => InsertColumnLeft()),
            Item("Insert Column Right", () => InsertColumnRight()),
            new Separator(),
            Item("Delete Row", () => DeleteRow()),
            Item("Delete Column", () => DeleteColumn()),
            new Separator(),
            Item("Merge Cells", () => MergeCells()),
            Item("Split Cell", () => SplitCell()),
        },
    };

    private static MenuItem Item(string header, string gesture, Action invoke)
    {
        var item = new MenuItem { Header = header, InputGesture = KeyGesture.Parse(gesture) };

        item.Click += (_, _) => invoke();

        return item;
    }

    private static MenuItem Item(string header, Func<bool> invoke)
    {
        var item = new MenuItem { Header = header };

        item.Click += (_, _) => invoke();

        return item;
    }
}