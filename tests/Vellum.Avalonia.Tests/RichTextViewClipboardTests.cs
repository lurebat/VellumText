using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// Selecting, the clipboard commands, and the affordances that tell a user the control is editable.
/// </summary>
public class RichTextViewClipboardTests
{
    [AvaloniaFact]
    public void ThePointerIsAnIBeam()
    {
        // The complaint that prompted this was "the cursor doesn't react at all". It is the only
        // thing distinguishing an editor from a label before you click it.
        var view = new RichTextView();

        Assert.NotNull(view.Cursor);
        // Cursor has no value equality, so compare what it reports itself to be.
        Assert.Equal(new Cursor(StandardCursorType.Ibeam).ToString(), view.Cursor!.ToString());
        Assert.NotEqual(new Cursor(StandardCursorType.Arrow).ToString(), view.Cursor.ToString());
    }

    [AvaloniaFact]
    public void ARightClickHasAMenuToPutUp()
    {
        var view = new RichTextView();

        var flyout = Assert.IsType<MenuFlyout>(view.ContextFlyout);
        var headers = flyout.ItemsSource!.Cast<object>().OfType<MenuItem>().Select(i => i.Header).ToArray();

        Assert.Equal(new object[] { "Cut", "Copy", "Paste", "Select All" }, headers);
    }

    [AvaloniaFact]
    public void SelectAllCoversTheWholeDocument()
    {
        var view = Realized(DocumentNode.FromParagraphs("first", "second"));

        Assert.True(view.SelectAll());
        Assert.Equal("first\nsecond", view.SelectedText());

        // Nothing changed, so nothing is reported as changed.
        Assert.False(view.SelectAll());
    }

    [AvaloniaFact]
    public void SelectedTextIsEmptyForACaret()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello"));

        Assert.Equal(string.Empty, view.SelectedText());
    }

    [AvaloniaFact]
    public void SelectedTextIsJustTheSelectedRange()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello world"));

        view.MoveTo(1, extend: false);
        view.MoveTo(6, extend: true);

        Assert.Equal("hello", view.SelectedText());
    }

    [AvaloniaFact]
    public void DeleteSelectionRemovesTheRangeAndLeavesACaret()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello world"));

        view.MoveTo(1, extend: false);
        view.MoveTo(7, extend: true);

        Assert.True(view.DeleteSelection());
        Assert.Equal("world", Text(view));
        Assert.True(view.State.Selection.IsEmpty);
        Assert.Equal(1, view.State.Selection.From);
    }

    [AvaloniaFact]
    public void DeleteSelectionDoesNothingToACaret()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello"));

        Assert.False(view.DeleteSelection());
        Assert.Equal("hello", Text(view));
    }

    [AvaloniaFact]
    public void PastingTextReplacesTheSelection()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello world"));

        view.MoveTo(1, extend: false);
        view.MoveTo(6, extend: true);

        Assert.True(view.PasteText("goodbye"));
        Assert.Equal("goodbye world", Text(view));
    }

    [AvaloniaTheory]
    [InlineData("a\r\nb")]
    [InlineData("a\nb")]
    [InlineData("a\rb")]
    public void EveryLineBreakConventionPastesAsOneBreak(string text)
    {
        // A Windows clipboard's CRLF must not paste as two paragraph splits, and a classic-Mac CR
        // must not paste as none.
        var view = Realized(DocumentNode.FromParagraphs(string.Empty));

        Assert.True(view.PasteText(text));
        Assert.Equal(2, view.State.Doc.Blocks.Length);
        Assert.Equal("a\nb", Text(view));
    }

    [AvaloniaFact]
    public void PastingNothingChangesNothing()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello"));

        Assert.False(view.PasteText(string.Empty));
        Assert.Equal("hello", Text(view));
    }

    [AvaloniaTheory]
    [InlineData(Key.C)]
    [InlineData(Key.X)]
    [InlineData(Key.V)]
    public void TheClipboardChordsAreHandledSoTheyNeverTypeALetter(Key key)
    {
        // Ctrl+V falling through to text input types a literal "v" while the paste is in flight.
        var view = Realized(DocumentNode.FromParagraphs("hello"));
        var window = new Window { Content = view };

        window.Show();
        view.Focus();

        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = KeyModifiers.Control,
            Source = view,
        };

        view.RaiseEvent(args);

        Assert.True(args.Handled);
    }

    [AvaloniaFact]
    public void ControlAStillSelectsEverything()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello"));
        var window = new Window { Content = view };

        window.Show();
        view.Focus();
        window.KeyPressQwerty(PhysicalKey.A, RawInputModifiers.Control);

        Assert.Equal("hello", view.SelectedText());
    }

    [AvaloniaFact]
    public void CutAndCopyAreOfferedOnlyWhenThereIsASelection()
    {
        // Asserted on the menu's own state rather than through a popup, because the headless
        // platform has no popup implementation to open one with.
        var view = Realized(DocumentNode.FromParagraphs("hello world"));
        var flyout = (MenuFlyout)view.ContextFlyout!;
        var items = flyout.ItemsSource!.Cast<object>().OfType<MenuItem>().ToDictionary(i => (string)i.Header!);

        view.SyncContextFlyout();

        Assert.False(items["Cut"].IsEnabled);
        Assert.False(items["Copy"].IsEnabled);
        Assert.True(items["Select All"].IsEnabled);

        view.MoveTo(1, extend: false);
        view.MoveTo(6, extend: true);
        view.SyncContextFlyout();

        Assert.True(items["Cut"].IsEnabled);
        Assert.True(items["Copy"].IsEnabled);
    }
    private static string Text(RichTextView view) =>
        string.Join('\n', view.State.Doc.Blocks.OfType<ParagraphNode>().Select(p => p.Content.Text));

    private static RichTextView Realized(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };

        view.Measure(new global::Avalonia.Size(400, 400));
        view.Arrange(new global::Avalonia.Rect(0, 0, 400, 400));

        return view;
    }
}