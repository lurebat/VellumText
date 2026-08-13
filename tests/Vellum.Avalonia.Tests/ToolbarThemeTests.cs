using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.VisualTree;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// The default toolbar template that ships in <c>Themes/Vellum.axaml</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RichTextToolbarTests"/> deliberately never applies a template, because the control's
/// state and commands must work without one. That leaves the shipped markup itself untested, and
/// a theme that fails to load is a control that draws nothing — the exact failure a consumer sees
/// first. These tests load the real resource by URI and drive the real buttons.
/// </para>
/// <para>
/// No base theme is loaded, so the buttons have no template of their own and draw nothing. That
/// is fine: what is under test is whether the markup parses, resolves, and is bound to the right
/// command and the right state, not what a Fluent button looks like.
/// </para>
/// </remarks>
public class ToolbarThemeTests
{
    [AvaloniaFact]
    public void TheShippedThemeGivesTheToolbarItsButtons()
    {
        var bar = Templated(out _);

        var buttons = Buttons(bar);

        // The count is a floor, not a fingerprint: the point is that the bar was populated from
        // markup rather than left empty, and a template edit should not have to touch this.
        Assert.True(buttons.Count >= 24, $"expected a populated bar, found {buttons.Count} buttons");
    }

    [AvaloniaFact]
    public void TheBoldButtonBoldsTheSelection()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        Click(bar, "Bold (Ctrl+B)");

        Assert.True(bar.IsBold);
        Assert.True(Assert.IsType<ParagraphNode>(Assert.Single(editor.State.Doc.Blocks)).Content.Marks.Single().Value.Style
            .HasFlag(TextStyle.Bold));
    }

    /// <summary>
    /// The bar reports what the selection is; the buttons only display it. A button that lit
    /// because it was clicked rather than because the document changed would keep lying after
    /// undo, which is the whole reason the state is derived.
    /// </summary>
    [AvaloniaFact]
    public void AButtonLightsBecauseTheDocumentSaysSoNotBecauseItWasClicked()
    {
        var bar = Templated(out var editor);

        var bold = Find(bar, "Bold (Ctrl+B)") as ToggleButton;

        Assert.NotNull(bold);
        Assert.False(bold.IsChecked);

        var view = editor.View!;

        SelectAll(view);
        view.ToggleStyle(TextStyle.Bold);

        Assert.True(bold.IsChecked);

        view.Undo();

        Assert.False(bold.IsChecked);
    }

    [AvaloniaFact]
    public void TheHeadingButtonsLightOneAtATime()
    {
        var bar = Templated(out var editor);

        var body = (ToggleButton)Find(bar, "Body text")!;
        var h1 = (ToggleButton)Find(bar, "Heading 1")!;
        var h2 = (ToggleButton)Find(bar, "Heading 2")!;

        Assert.True(body.IsChecked);
        Assert.False(h1.IsChecked);

        Click(bar, "Heading 1");

        Assert.True(h1.IsChecked);
        Assert.False(body.IsChecked);
        Assert.False(h2.IsChecked);
        Assert.Equal(ParagraphKind.Heading1, Assert.IsType<ParagraphNode>(Assert.Single(editor.State.Doc.Blocks)).Kind);
    }

    [AvaloniaFact]
    public void UndoIsDisabledUntilThereIsSomethingToUndo()
    {
        var bar = Templated(out var editor);

        var undo = Find(bar, "Undo (Ctrl+Z)")!;

        Assert.False(undo.IsEnabled);

        var view = editor.View!;

        SelectAll(view);
        view.ToggleStyle(TextStyle.Bold);

        Assert.True(undo.IsEnabled);
    }

    /// <summary>
    /// The alignment buttons pass an enum through <c>CommandParameter</c>, which markup can only
    /// express as <c>x:Static</c>. Getting that wrong passes a string and the command silently
    /// does nothing, so it is worth one test that the parameter arrives as the enum.
    /// </summary>
    [AvaloniaFact]
    public void TheCentreButtonCentresTheParagraph()
    {
        var bar = Templated(out var editor);

        Click(bar, "Centre");

        Assert.Equal(TextAlign.Center, Assert.IsType<ParagraphNode>(Assert.Single(editor.State.Doc.Blocks)).Align);
        Assert.True(((ToggleButton)Find(bar, "Centre")!).IsChecked);
    }

    [AvaloniaFact]
    public void TheListButtonMakesTheParagraphAnItem()
    {
        var bar = Templated(out var editor);

        Click(bar, "Bulleted list (Ctrl+Shift+8)");

        Assert.Equal(ListKind.Unordered, bar.ListKind);
        Assert.IsType<ListNode>(Assert.Single(editor.State.Doc.Blocks));
    }

    /// <summary>
    /// A flyout's content lives in a popup, which is not a visual descendant of the button that
    /// opened it. Whether a binding written against the templated parent survives that is exactly
    /// the sort of thing that compiles, looks right, and silently does nothing.
    /// </summary>
    [AvaloniaFact]
    public void ASwatchInTheColourFlyoutColoursTheSelection()
    {
        var bar = Templated(out var editor);

        var view = editor.View!;

        SelectAll(view);

        var swatch = Swatches(bar, "Text colour")[8];

        Assert.Equal("#C82020", swatch.CommandParameter);

        swatch.Command!.Execute(swatch.CommandParameter);

        Assert.Equal(new Rgba(0xC8, 0x20, 0x20, 255), bar.SelectionForeground);
    }

    [AvaloniaFact]
    public void TheHighlightFlyoutReachesTheOtherCommand()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var swatch = Swatches(bar, "Highlight")[11];

        swatch.Command!.Execute(swatch.CommandParameter);

        Assert.Equal(new Rgba(0x18, 0x90, 0x40, 255), bar.SelectionHighlight);
        Assert.Null(bar.SelectionForeground);
    }

    /// <summary>
    /// No colour is not black. It removes the mark, so the text takes what it inherits — which is
    /// the difference between pasted content that follows a theme and content that is stuck dark.
    /// </summary>
    [AvaloniaFact]
    public void ClearingTheColourRemovesTheMarkRatherThanSettingBlack()
    {
        var bar = Templated(out var editor);

        SelectAll(editor.View!);

        var swatch = Swatches(bar, "Text colour")[0];

        swatch.Command!.Execute(swatch.CommandParameter);

        Assert.Equal(new Rgba(0, 0, 0, 255), bar.SelectionForeground);

        var clear = Popup(bar, "Text colour").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("vellum-clear-colour"));

        clear.Command!.Execute(clear.CommandParameter);

        Assert.Null(bar.SelectionForeground);
    }

    /// <summary>Opens a colour button's flyout and returns the swatch buttons inside it.</summary>
    private static List<Button> Swatches(RichTextToolbar bar, string tip) =>
        Popup(bar, tip).GetVisualDescendants().OfType<Button>()
            .Where(b => b.CommandParameter is string)
            .ToList();

    private static Visual Popup(RichTextToolbar bar, string tip)
    {
        var button = Find(bar, tip) ?? throw new InvalidOperationException($"no button tipped {tip}");
        var flyout = button.Flyout as Flyout
            ?? throw new InvalidOperationException($"{tip} has no flyout");

        flyout.ShowAt(button);

        var content = (Control)flyout.Content!;

        // The item containers do not exist until the popup has been through a layout pass, and
        // headless does not run one for us.
        content.Measure(new Size(400, 400));
        content.Arrange(new Rect(0, 0, 400, 400));

        return content;
    }

    /// <summary>Builds an editor and a toolbar with the shipped theme loaded, and shows them.</summary>
    private static RichTextToolbar Templated(out RichTextEditor editor)
    {
        // Loaded into the window rather than the application, because the headless application is
        // shared across every test in the assembly and one that accumulated styles would make the
        // tests depend on the order they ran in.
        var include = new StyleInclude(new Uri("avares://Vellum.Avalonia/"))
        {
            Source = new Uri("avares://Vellum.Avalonia/Themes/Vellum.axaml"),
        };

        editor = new RichTextEditor
        {
            State = EditorState.Create(DocumentNode.FromParagraphs("hello world")),
        };

        var bar = new RichTextToolbar { Editor = editor };

        var window = new Window { Width = 900, Height = 400 };

        // Fluent first, and not for looks: a window with no template has no overlay layer, and
        // headless offers no popup implementation, so without it a flyout cannot open at all.
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Styles.Add(include);
        window.Content = new StackPanel { Children = { bar, editor } };

        window.Show();
        window.Measure(new Size(900, 400));
        window.Arrange(new Rect(0, 0, 900, 400));

        return bar;
    }

    private static void SelectAll(RichTextView view)
    {
        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[^1].End, extend: true);
    }

    private static List<Button> Buttons(RichTextToolbar bar) =>
        bar.GetVisualDescendants().OfType<Button>().ToList();

    /// <summary>
    /// Finds a button by its tooltip, which is the only label some of them have — an icon button
    /// has no text, and matching on the geometry would be matching on the drawing.
    /// </summary>
    private static Button? Find(RichTextToolbar bar, string tip) =>
        Buttons(bar).SingleOrDefault(b => (string?)ToolTip.GetTip(b) == tip);

    private static void Click(RichTextToolbar bar, string tip)
    {
        var button = Find(bar, tip) ?? throw new InvalidOperationException($"no button tipped {tip}");

        // Not a synthesized pointer event: with no base theme the button has no template and so
        // no hit-testable interior. What is under test is the binding from the button to the
        // command, and that is exactly what this exercises.
        button.Command!.Execute(button.CommandParameter);
    }
}
