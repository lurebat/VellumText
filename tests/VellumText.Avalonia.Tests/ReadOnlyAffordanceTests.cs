using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Input;
using global::Avalonia.Input.Platform;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Threading;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// What a read-only editor offers to do, as opposed to what it agrees to do.
/// </summary>
/// <remarks>
/// <para>
/// Reported from live use: a clipboard manager rendering previews as
/// <c>RichTextEditor { IsReadOnly = true }</c> got the full floating toolbar over any selection —
/// bold, lists, colour, link, undo — every control of which the editor then refused. The same
/// applies to Cut and Paste on the context menu and to the whole table submenu.
/// </para>
/// <para>
/// <b>Every assertion here is about an affordance, not about a command.</b> That the commands
/// refuse is already covered elsewhere and was never the defect: an editor that renders a Bold
/// button and then declines to embolden has already misled the user by the time the command runs.
/// The two tests that previously covered read-only asserted the markup produced the right
/// document, which is equally true of a control that offers everything and honours nothing.
/// </para>
/// </remarks>
public class ReadOnlyAffordanceTests
{
    private const double Width = 620;
    private const double Height = 460;

    [AvaloniaFact]
    public void AReadOnlyEditorDoesNotFloatAFormattingBarOverASelection()
    {
        var editor = Shown(out var window, readOnly: true);

        Select(editor);
        Layout(window);

        Assert.False(editor.View!.State.Selection.IsEmpty);
        Assert.False(
            editor.SelectionToolbar!.IsVisible,
            "a read-only editor offered a formatting bar for edits it will refuse");
    }

    /// <remarks>
    /// The flag keeps its own authority. A host that wants no bar on an editable control — a
    /// preview pane that will one day become editable — can still say so, so the gate is an
    /// additional condition rather than a replacement for the flag.
    /// </remarks>
    [AvaloniaFact]
    public void AnEditableEditorStillFloatsOne()
    {
        var editor = Shown(out var window, readOnly: false);

        Select(editor);
        Layout(window);

        Assert.True(editor.SelectionToolbar!.IsVisible);
    }

    /// <remarks>
    /// The second half of the same bug, and the half the report did not reach:
    /// <c>IsReadOnlyProperty</c> routed to <c>PushOptions</c> and not to
    /// <c>UpdateSelectionToolbar</c>, so a bar already on screen stayed there after the control
    /// became read-only. Gating visibility alone would not have fixed it.
    /// </remarks>
    [AvaloniaFact]
    public void MakingAnEditorReadOnlyTakesAwayTheBarAlreadyOnScreen()
    {
        var editor = Shown(out var window, readOnly: false);

        Select(editor);
        Layout(window);

        Assert.True(editor.SelectionToolbar!.IsVisible);

        editor.IsReadOnly = true;

        Assert.False(editor.SelectionToolbar.IsVisible);

        // The selection itself is untouched: it is still copyable, which is the one edit-adjacent
        // thing a read-only editor genuinely offers.
        Assert.False(editor.View!.State.Selection.IsEmpty);
    }

    [AvaloniaFact]
    public void MakingAnEditorEditableAgainBringsTheBarBack()
    {
        var editor = Shown(out var window, readOnly: true);

        Select(editor);
        Layout(window);

        Assert.False(editor.SelectionToolbar!.IsVisible);

        editor.IsReadOnly = false;
        Layout(window);

        Assert.True(editor.SelectionToolbar.IsVisible);
    }

    /// <remarks>
    /// Both flags are independent gates, so the explicit one must still be able to say no on a
    /// control that is not read-only. Without this the fix could have been "ignore the flag when
    /// read-only", which reads the same in the passing case and is wrong.
    /// </remarks>
    [AvaloniaFact]
    public void TheExplicitFlagStillSuppressesTheBarOnAnEditableEditor()
    {
        var editor = Shown(out var window, readOnly: false);

        editor.IsSelectionToolbarEnabled = false;

        Select(editor);
        Layout(window);

        Assert.False(editor.SelectionToolbar!.IsVisible);
    }

    [AvaloniaFact]
    public void AReadOnlyViewOffersNeitherCutNorPaste()
    {
        var view = ShownView(readOnly: true);

        view.SelectAll();
        view.SyncContextFlyout();

        // Hidden, not greyed: see SyncContextFlyout's remark on why read-only is the hiding case
        // and an empty selection is the greying one.
        Assert.False(Item(view, "Cut").IsVisible, "a read-only view offered Cut");
        Assert.False(Item(view, "Paste").IsVisible, "a read-only view offered Paste");
    }

    /// <remarks>
    /// Copy is the one entry that stays. It is a read, so it is the only thing on this menu a
    /// read-only control can honour — and taking it away is what drove the report in the first
    /// place.
    /// </remarks>
    [AvaloniaFact]
    public void AReadOnlyViewStillOffersCopy()
    {
        var view = ShownView(readOnly: true);

        view.SelectAll();
        view.SyncContextFlyout();

        Assert.True(Item(view, "Copy").IsEnabled);
    }

    [AvaloniaFact]
    public void AnEditableViewStillOffersCut()
    {
        var view = ShownView(readOnly: false);

        view.SelectAll();
        view.SyncContextFlyout();

        Assert.True(Item(view, "Cut").IsEnabled);
    }

    /// <remarks>
    /// Hidden rather than disabled, matching what the submenu already does when there is no table:
    /// eight greyed entries say "you are in a table but may do nothing", which is a longer way of
    /// saying nothing.
    ///
    /// This does not contradict <c>BuildTableItem</c>'s rule that entries stay enabled when they
    /// cannot apply. That rule is about state which changes as the caret moves — merge with no
    /// rectangle selected — and exists to stop entries flickering. Read-only is a property of the
    /// control that does not change as the user moves around, so gating on it cannot flicker.
    /// </remarks>
    [AvaloniaFact]
    public void AReadOnlyViewInATableOffersNoTableCommands()
    {
        var view = ShownView(readOnly: true, withTable: true);

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.SyncContextFlyout();

        Assert.True(view.IsInTable, "the caret is not in a table, so this proves nothing");
        Assert.False(Item(view, "Table").IsVisible, "a read-only view offered table editing");
    }

    [AvaloniaFact]
    public void AnEditableViewInATableStillOffersThem()
    {
        var view = ShownView(readOnly: false, withTable: true);

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.SyncContextFlyout();

        Assert.True(view.IsInTable);
        Assert.True(Item(view, "Table").IsVisible);
    }

    /// <remarks>
    /// Asserts what the menu does <b>not</b> offer. Listing what it has would be satisfied by a
    /// superset — a menu that also had Cut on it would pass — which is the same failure mode as
    /// the round-trip tests here that asserted only that an import was schema-valid and stayed
    /// green for weeks while the importer dropped rows.
    /// </remarks>
    [AvaloniaFact]
    public void AReadOnlyViewOffersNothingThatWouldChangeTheDocument()
    {
        var view = ShownView(readOnly: true, withTable: true);

        view.SelectAll();
        view.SyncContextFlyout();

        var offered = ((MenuFlyout)view.ContextFlyout!)
            .ItemsSource!
            .OfType<MenuItem>()
            .Where(i => i.IsVisible && i.IsEnabled)
            .Select(i => (string?)i.Header)
            .ToArray();

        Assert.DoesNotContain("Cut", offered);
        Assert.DoesNotContain("Paste", offered);
        Assert.DoesNotContain("Table", offered);

        // And the reads survive, or the control would be useless rather than merely honest.
        Assert.Equal(["Copy", "Select All"], offered.Order().AsEnumerable());
    }

    /// <remarks>
    /// <c>CutAsync</c> copies first and deletes second, and only the delete consults
    /// <c>IsReadOnly</c> — so a read-only Cut left the document alone but still wrote the
    /// clipboard. Not data loss, but worse than a no-op: half of it ran, and the user cannot see
    /// which half. Asserting the document is unchanged would have passed throughout.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARefusedCutDoesNotWriteTheClipboardEither()
    {
        var view = ShownView(readOnly: true);
        var clipboard = TopLevel.GetTopLevel(view)!.Clipboard!;

        await SetClipboard(clipboard, "untouched");

        view.SelectAll();

        Assert.False(await view.CutAsync());
        Assert.Equal("untouched", await ReadClipboard(clipboard));
    }

    /// <remarks>
    /// Hiding the menu entry does not disarm the accelerator, and this is the one shortcut where
    /// that matters: <c>CutAsync</c> copies before it deletes, so an ungated Ctrl+X on a read-only
    /// pane overwrites the clipboard and then declines the delete. The document is untouched, which
    /// is exactly why asserting the document would have proved nothing.
    ///
    /// The consequence is worst in the product that reported it — a clipboard manager, where a
    /// mis-hit for Ctrl+C on a preview silently discards what the user was holding and records the
    /// replacement as a clip they never made, from a key that visibly did nothing.
    ///
    /// Gated in <c>CutAsync</c> rather than in the key handler on purpose: one choke point covers
    /// the menu entry, the accelerator and any programmatic caller. Gating call sites is how the
    /// fourth call site gets missed.
    /// </remarks>
    [AvaloniaFact]
    public async Task ARefusedCutDoesNotWriteTheClipboardFromTheKeyboardEither()
    {
        var view = ShownView(readOnly: true);
        var window = (Window)TopLevel.GetTopLevel(view)!;
        var clipboard = window.Clipboard!;

        await SetClipboard(clipboard, "SENTINEL");

        view.Focus();
        view.SelectAll();

        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);

        // The handler is async void, so the cut would land after the gesture returns.
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("SENTINEL", await ReadClipboard(clipboard));
    }

    /// <remarks>
    /// Guards the guard. If the gesture above never reached <c>CutAsync</c> at all — a wrong key,
    /// an unfocused view — the assertion would pass against a completely ungated control, so the
    /// same gesture is shown to write the clipboard on an editable one.
    /// </remarks>
    [AvaloniaFact]
    public async Task TheSameKeystrokeOnAnEditableViewDoesWriteTheClipboard()
    {
        var view = ShownView(readOnly: false);
        var window = (Window)TopLevel.GetTopLevel(view)!;
        var clipboard = window.Clipboard!;

        await SetClipboard(clipboard, "SENTINEL");

        view.Focus();
        view.SelectAll();

        window.KeyPressQwerty(PhysicalKey.X, RawInputModifiers.Control);

        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual("SENTINEL", await ReadClipboard(clipboard));
    }

    /// <remarks>
    /// A tripwire over every other clipboard-adjacent gesture, so that binding a Cut synonym later
    /// is caught here rather than in a preview pane. Today's table has no <c>Key.Insert</c> case at
    /// all, and <c>Key.Delete</c> carries no modifier guard, so Shift+Delete falls into plain
    /// delete — the classic Cut synonyms are absent and inert respectively.
    ///
    /// Ctrl+C is retained as a control on purpose: a sweep asserting only that nothing wrote would
    /// pass just as well against a fixture where the clipboard is unreachable, which would prove
    /// the opposite of what it claims.
    /// </remarks>
    [AvaloniaFact]
    public async Task NoOtherShortcutWritesTheClipboardOnAReadOnlyView()
    {
        (PhysicalKey Key, RawInputModifiers Modifiers)[] inert =
        [
            (PhysicalKey.Delete, RawInputModifiers.Shift),
            (PhysicalKey.Insert, RawInputModifiers.Control),
            (PhysicalKey.Insert, RawInputModifiers.Shift),
            (PhysicalKey.V, RawInputModifiers.Control),
            (PhysicalKey.Delete, RawInputModifiers.None),
            (PhysicalKey.Backspace, RawInputModifiers.None),
        ];

        foreach (var (key, modifiers) in inert)
        {
            var view = ShownView(readOnly: true);
            var window = (Window)TopLevel.GetTopLevel(view)!;
            var clipboard = window.Clipboard!;

            await SetClipboard(clipboard, "SENTINEL");

            view.Focus();
            view.SelectAll();

            window.KeyPressQwerty(key, modifiers);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("SENTINEL", await ReadClipboard(clipboard));
        }
    }

    [AvaloniaFact]
    public async Task ButCopyStillReachesTheClipboardFromTheSameFixture()
    {
        var view = ShownView(readOnly: true);
        var window = (Window)TopLevel.GetTopLevel(view)!;
        var clipboard = window.Clipboard!;

        await SetClipboard(clipboard, "SENTINEL");

        view.Focus();
        view.SelectAll();

        window.KeyPressQwerty(PhysicalKey.C, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual("SENTINEL", await ReadClipboard(clipboard));
    }

    /// <remarks>
    /// Found by auditing every gesture for side effects that survive a refused edit, after the
    /// Ctrl+X finding showed that <c>Apply</c> alone is not a sufficient gate.
    ///
    /// Formatting with an empty selection arms <c>StoredMarks</c> directly and never builds a
    /// transaction, so <c>Apply</c> never sees it: Ctrl+B on a read-only view left bold armed for
    /// an insertion that can never happen. Gated in <c>SetMarks</c>, which is the choke point every
    /// formatting shortcut goes through.
    /// </remarks>
    [AvaloniaFact]
    public void FormattingAReadOnlyViewDoesNotArmMarksForAnInsertionThatCannotHappen()
    {
        var view = ShownView(readOnly: true);

        view.MoveTo(view.Slots[0].Start + 2, extend: false);

        Assert.False(view.ToggleStyle(TextStyle.Bold));
        Assert.Null(view.State.StoredMarks);
    }

    [AvaloniaFact]
    public void FormattingAnEditableViewStillArmsThem()
    {
        var view = ShownView(readOnly: false);

        view.MoveTo(view.Slots[0].Start + 2, extend: false);

        Assert.True(view.ToggleStyle(TextStyle.Bold));
        Assert.NotNull(view.State.StoredMarks);
    }

    /// <remarks>
    /// <c>LinkRequested</c> asks the host to put a dialog on screen. Raising it on a read-only view
    /// costs the user a dialog and a typed URL before <c>ApplyLink</c> refuses — an affordance bug
    /// of the same family as the toolbar, and more expensive than most.
    /// </remarks>
    [AvaloniaFact]
    public void AReadOnlyViewDoesNotAskTheHostForALinkItWillRefuse()
    {
        var view = ShownView(readOnly: true);
        var window = (Window)TopLevel.GetTopLevel(view)!;
        var asked = 0;

        view.LinkRequested += (_, _) => asked++;
        view.Focus();
        view.SelectAll();

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, asked);
    }

    [AvaloniaFact]
    public void AnEditableViewStillAsks()
    {
        var view = ShownView(readOnly: false);
        var window = (Window)TopLevel.GetTopLevel(view)!;
        var asked = 0;

        view.LinkRequested += (_, _) => asked++;
        view.Focus();
        view.SelectAll();

        window.KeyPressQwerty(PhysicalKey.K, RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(1, asked);
    }

    private static async Task SetClipboard(IClipboard clipboard, string text)
    {
        var data = new DataTransfer();
        var item = new DataTransferItem();

        item.SetText(text);
        data.Add(item);

        await clipboard.SetDataAsync(data);
    }

    private static async Task<string?> ReadClipboard(IClipboard clipboard)
    {
        var data = await clipboard.TryGetDataAsync();

        return data is null ? null : await data.Items[0].TryGetTextAsync();
    }

    private static MenuItem Item(RichTextView view, string header) =>
        ((MenuFlyout)view.ContextFlyout!)
        .ItemsSource!
        .OfType<MenuItem>()
        .Single(i => (string?)i.Header == header);

    private static RichTextView ShownView(bool readOnly, bool withTable = false)
    {
        var doc = withTable
            ? new DocumentNode([Table()])
            : DocumentNode.FromParagraphs("a paragraph with several words in it");

        var view = new RichTextView
        {
            State = EditorState.Create(doc),
            IsReadOnly = readOnly,
        };

        var window = new Window { Width = Width, Height = Height, Content = view };

        window.Show();
        Layout(window);

        return view;
    }

    private static TableNode Table() =>
        new([
            new TableRowNode([
                new TableCellNode([ParagraphNode.FromText("one")]),
                new TableCellNode([ParagraphNode.FromText("two")]),
            ]),
        ]);

    private static RichTextEditor Shown(out Window window, bool readOnly)
    {
        var include = new StyleInclude(new Uri("avares://VellumText.Avalonia/"))
        {
            Source = new Uri("avares://VellumText.Avalonia/Themes/VellumText.axaml"),
        };

        var editor = new RichTextEditor
        {
            State = EditorState.Create(
                DocumentNode.FromParagraphs("a paragraph with several words in it")),
            IsReadOnly = readOnly,
        };

        window = new Window { Width = Width, Height = Height };

        window.Styles.Add(include);
        window.Content = editor;
        window.Show();

        Layout(window);

        return editor;
    }

    private static void Layout(Window window)
    {
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));
    }

    private static void Select(RichTextEditor editor)
    {
        var view = editor.View!;

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[0].End, extend: true);
    }
}
