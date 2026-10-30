using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Headless;
using global::Avalonia.Input;
using global::Avalonia.Markup.Xaml.Styling;
using global::Avalonia.Media.TextFormatting;
using global::Avalonia.Threading;
using global::Avalonia.VisualTree;
using System.Reflection;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Drives the shipped toolbar the way a user does, and reads back what the user would see.
/// </summary>
/// <remarks>
/// A toolbar test asserts the outcome — the marks or attributes on exactly the selected text, the
/// bar's own state afterwards, and for anything that moves text, lines laid out without overlap —
/// never only that a command ran (owner rule). Two regressions shipped past tests that checked the
/// mechanism: a colour pick raised the click event by hand, which never runs the command, and a
/// large size passed while its lines were drawn over one another.
/// </remarks>
internal static class ToolbarHarness
{
    /// <summary>Long enough that a large size wraps it, so there are lines to compare.</summary>
    public const string First = "hello world and enough further words that larger text has to wrap onto more lines";

    public const string Second = "second line here";

    public const string Third = "third stays put";

    /// <summary>Three paragraphs: two to select across, and one after them the edit must not reach.</summary>
    public static DocumentNode ThreeParagraphs() => DocumentNode.FromParagraphs(First, Second, Third);

    /// <summary>Builds an editor and a toolbar with the shipped theme loaded, and shows them.</summary>
    public static RichTextToolbar Templated(out RichTextEditor editor, DocumentNode? doc = null)
    {
        // Loaded into the window rather than the application, because the headless application is
        // shared across every test in the assembly and one that accumulated styles would make the
        // tests depend on the order they ran in.
        var include = new StyleInclude(new Uri("avares://VellumText.Avalonia/"))
        {
            Source = new Uri("avares://VellumText.Avalonia/Themes/VellumText.axaml"),
        };

        editor = new RichTextEditor
        {
            State = EditorState.Create(doc ?? DocumentNode.FromParagraphs("hello world")),
        };

        var bar = new RichTextToolbar { Editor = editor };

        var window = new Window { Width = 900, Height = 600 };

        // Fluent first, and not for looks: a window with no template has no overlay layer, and
        // headless offers no popup implementation, so without it a flyout cannot open at all.
        window.Styles.Add(new global::Avalonia.Themes.Fluent.FluentTheme());
        window.Styles.Add(include);
        window.Content = new StackPanel { Children = { bar, editor } };

        window.Show();
        Layout(window);

        return bar;
    }

    public static Window WindowOf(Visual visual) => (Window)TopLevel.GetTopLevel(visual)!;

    /// <summary>
    /// Clicks a button the way a pointer release does: <see cref="Button"/>'s own click handling,
    /// which, for a <see cref="ToggleButton"/>, flips it first, then raises
    /// <see cref="Button.ClickEvent"/> and runs the command. Raising the event by hand, or running
    /// the command directly, skips part of that, which is how a broken pick once passed every test.
    /// </summary>
    public static void UserClick(Button button)
    {
        Assert.True(button.IsEffectivelyEnabled, $"{ToolTip.GetTip(button)} is disabled; a user could not click it");

        // Taken first: a swatch leaves the tree when its flyout closes.
        var window = WindowOf(button);

        typeof(Button).GetMethod("OnClick", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(button, null);
        Dispatcher.UIThread.RunJobs();
        Layout(window);
    }

    /// <summary>Clicks the button with this tooltip; see <see cref="UserClick(Button)"/>.</summary>
    public static void UserClick(RichTextToolbar bar, string tip) =>
        UserClick(Find(bar, tip) ?? throw new InvalidOperationException($"no button tipped {tip}"));

    /// <summary>A real press and release of the left button over the middle of a control.</summary>
    /// <remarks>
    /// Slower to set up than <see cref="UserClick(Button)"/>, and only needed where what is under
    /// test is what the pointer does besides clicking — where the keyboard focus ends up.
    /// </remarks>
    public static void PointerClick(Control control)
    {
        var window = WindowOf(control);
        var middle = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        window.MouseDown(middle, MouseButton.Left);
        window.MouseUp(middle, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Layout(window);
    }

    /// <summary>Types into whatever has the keyboard, then presses Enter.</summary>
    public static void TypeAndEnter(Control target, string text)
    {
        var window = WindowOf(target);

        window.KeyTextInput(text);
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, "\r");
        Dispatcher.UIThread.RunJobs();
        Layout(window);
    }

    public static void Layout(Window window)
    {
        window.UpdateLayout();
        window.Measure(new Size(window.Width, window.Height));
        window.Arrange(new Rect(0, 0, window.Width, window.Height));
    }

    public static List<Button> Buttons(RichTextToolbar bar) =>
        bar.GetVisualDescendants().OfType<Button>().ToList();

    /// <summary>
    /// Finds a button by its tooltip, which is the only label some of them have — an icon button
    /// has no text, and matching on the geometry would be matching on the drawing.
    /// </summary>
    public static Button? Find(RichTextToolbar bar, string tip) =>
        Buttons(bar).SingleOrDefault(b => (string?)ToolTip.GetTip(b) == tip);

    public static ToggleButton Toggle(RichTextToolbar bar, string tip) =>
        Assert.IsAssignableFrom<ToggleButton>(Find(bar, tip));

    public static ComboBox Box(RichTextToolbar bar, string name) =>
        bar.GetVisualDescendants().OfType<ComboBox>().Single(b => b.Name == name);

    /// <summary>Opens a colour button's flyout and returns the swatch buttons inside it.</summary>
    public static List<Button> Swatches(RichTextToolbar bar, string tip) =>
        Popup(bar, tip).GetVisualDescendants().OfType<Button>()
            .Where(b => b.CommandParameter is string)
            .ToList();

    /// <summary>Opens a colour button's flyout and returns its "no colour" button.</summary>
    public static Button ClearColour(RichTextToolbar bar, string tip) =>
        Popup(bar, tip).GetVisualDescendants().OfType<Button>()
            .Single(b => b.Classes.Contains("VellumText-clear-colour"));

    public static Visual Popup(RichTextToolbar bar, string tip)
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

    /// <summary>Selects "hello", the start of the first paragraph and only part of it.</summary>
    public static void SelectPart(RichTextView view)
    {
        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[0].Start + 5, extend: true);
    }

    /// <summary>Selects from "world" in the first paragraph to "second" in the next.</summary>
    public static void SelectAcross(RichTextView view)
    {
        view.MoveTo(view.Slots[0].Start + 6, extend: false);
        view.MoveTo(view.Slots[1].Start + 6, extend: true);
    }

    /// <summary>The characters <see cref="SelectPart"/> selects: paragraph, from, to.</summary>
    public static readonly (int Block, int From, int To)[] Part = [(0, 0, 5)];

    /// <summary>The characters <see cref="SelectAcross"/> selects: paragraph, from, to.</summary>
    public static readonly (int Block, int From, int To)[] Across = [(0, 6, First.Length), (1, 0, 6)];

    /// <summary>The paragraphs of a document, looking inside lists, in document order.</summary>
    public static List<ParagraphNode> Paragraphs(DocumentNode doc)
    {
        var found = new List<ParagraphNode>();

        foreach (var block in doc.Blocks)
        {
            Collect(block);
        }

        return found;

        void Collect(BlockNode block)
        {
            switch (block)
            {
                case ParagraphNode paragraph:
                    found.Add(paragraph);
                    break;
                case ListNode list:
                    foreach (var item in list.Items)
                    {
                        foreach (var child in item.Blocks)
                        {
                            Collect(child);
                        }
                    }

                    break;
            }
        }
    }

    /// <summary>
    /// Asserts that a mark is on every selected character and on no other character in the
    /// document — the edit reached the whole selection and leaked nowhere.
    /// </summary>
    public static void AssertMarkedExactly(
        DocumentNode doc, (int Block, int From, int To)[] selected, Func<MarkSet, bool> has, string what)
    {
        var paragraphs = Paragraphs(doc);

        for (var block = 0; block < paragraphs.Count; block++)
        {
            var content = paragraphs[block].Content;

            for (var i = 0; i < content.Length; i++)
            {
                var inside = selected.Any(s => s.Block == block && i >= s.From && i < s.To);

                Assert.True(
                    has(content.MarkAt(i)) == inside,
                    $"paragraph {block}, character {i} ('{content.Text[i]}'): expected {(inside ? "" : "no ")}{what}");
            }
        }
    }

    /// <summary>
    /// The properties the laid-out run covering a character was drawn with — what is on screen,
    /// as opposed to what the document says.
    /// </summary>
    public static TextRunProperties RunAt(RichTextView view, int block, int offset)
    {
        var paragraph = Assert.IsType<ParagraphView>(view.Blocks[block]);

        foreach (var line in paragraph.Layout.TextLines)
        {
            var start = line.FirstTextSourceIndex;

            foreach (var run in line.TextRuns)
            {
                if (offset >= start && offset < start + run.Length)
                {
                    return run.Properties!;
                }

                start += run.Length;
            }
        }

        throw new InvalidOperationException($"no laid-out run covers character {offset} of block {block}");
    }

    /// <summary>
    /// Asserts that no laid-out line overlaps the one after it, across the whole document: for
    /// every line N, line N+1's top is at or below line N's bottom.
    /// </summary>
    /// <remarks>
    /// A line's box is the taller of its own height and the glyphs on it — the ascent and descent
    /// of every run at its baseline. The line height alone cannot see the failure that shipped:
    /// lines are stacked by their heights, so they never overlap by that measure, while 48-pixel
    /// text on 22.4-pixel lines was drawn far into the line below.
    /// </remarks>
    public static void AssertNoOverlappingLines(RichTextView view)
    {
        var boxes = new List<(string Name, double Top, double Bottom)>();

        for (var block = 0; block < view.Blocks.Count; block++)
        {
            if (view.Blocks[block] is not ParagraphView paragraph)
            {
                continue;
            }

            // The caret at the start sits at the top of the first line, so this is where the
            // block's text begins, past its indent, spacing and padding.
            var top = view.BlockOrigin(block).Y + paragraph.GetCaretRect(0).Y;
            var index = 0;

            foreach (var line in paragraph.Layout.TextLines)
            {
                var ascent = 0.0;
                var descent = 0.0;

                foreach (var run in line.TextRuns.OfType<ShapedTextRun>())
                {
                    ascent = Math.Min(ascent, run.TextMetrics.Ascent);
                    descent = Math.Max(descent, run.TextMetrics.Descent);
                }

                boxes.Add((
                    $"block {block} line {index++}",
                    Math.Min(top, top + line.Baseline + ascent),
                    Math.Max(top + line.Height, top + line.Baseline + descent)));

                top += line.Height;
            }
        }

        Assert.NotEmpty(boxes);

        for (var i = 1; i < boxes.Count; i++)
        {
            Assert.True(
                boxes[i].Top >= boxes[i - 1].Bottom - 0.01,
                $"{boxes[i].Name} starts at {boxes[i].Top:0.##}, above the bottom of {boxes[i - 1].Name} at {boxes[i - 1].Bottom:0.##}");
        }
    }

    public static IInputElement? Focused(Visual visual) =>
        TopLevel.GetTopLevel(visual)!.FocusManager!.GetFocusedElement();
}
