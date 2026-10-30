using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Tests for character formatting on <see cref="RichTextView"/>.
/// </summary>
public class RichTextViewMarkTests
{
    [AvaloniaFact]
    public void BoldReachesExactlyTheSelectedCharactersAndNoOthers()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);

        Assert.True(view.ToggleStyle(TextStyle.Bold));

        Assert.Equal("BBBBB......", Map(view, TextStyle.Bold));
    }

    [AvaloniaFact]
    public void APartlyBoldSelectionBecomesWhollyBoldRatherThanWhollyPlain()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Bold);

        // Half bold, half not. Every other editor makes this all bold.
        Select(view, 0, 11);

        Assert.False(view.IsActive(TextStyle.Bold));
        Assert.True(view.ToggleStyle(TextStyle.Bold));
        Assert.Equal("BBBBBBBBBBB", Map(view, TextStyle.Bold));
    }

    [AvaloniaFact]
    public void TogglingAWhollyBoldSelectionTakesTheBoldOff()
    {
        var view = Shown("hello world");

        Select(view, 0, 11);
        view.ToggleStyle(TextStyle.Bold);

        Assert.True(view.IsActive(TextStyle.Bold));
        Assert.True(view.ToggleStyle(TextStyle.Bold));
        Assert.Equal("...........", Map(view, TextStyle.Bold));
    }

    [AvaloniaFact]
    public void AMixedSelectionReportsNeitherTheColourNorTheStyle()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.SetForeground(new Rgba(255, 0, 0, 255));
        view.ToggleStyle(TextStyle.Italic);

        Select(view, 0, 11);

        Assert.Null(view.SelectionMarks.Foreground);
        Assert.False(view.SelectionMarks.Has(TextStyle.Italic));
    }

    [AvaloniaFact]
    public void AWhollyFormattedSelectionReportsWhatItHas()
    {
        var view = Shown("hello world");

        Select(view, 0, 11);
        view.SetForeground(new Rgba(12, 34, 56, 255));
        view.SetFontSize(18);
        view.SetFontFamily("Courier New");

        Assert.Equal(new Rgba(12, 34, 56, 255), view.SelectionMarks.Foreground);
        Assert.Equal(18, view.SelectionMarks.FontSize);
        Assert.Equal("Courier New", view.SelectionMarks.FontFamily);
    }

    /// <summary>
    /// Pressing Ctrl+B with nothing selected has to arm the next character rather than edit
    /// anything, and must not leave an undo entry that undoes nothing visible.
    /// </summary>
    [AvaloniaFact]
    public void BoldAtAnEmptyCaretAppliesToWhatIsTypedNextAndIsNotItselfUndoable()
    {
        var view = Shown("hi");

        Select(view, 2, 2);

        Assert.True(view.ToggleStyle(TextStyle.Bold));
        Assert.False(view.CanUndo);

        view.InsertText("X");

        Assert.Equal("..B", Map(view, TextStyle.Bold));

        view.Undo();

        Assert.Equal("hi", Text(view));
    }

    [AvaloniaFact]
    public void ArmedFormattingIsForgottenWhenTheCaretIsMoved()
    {
        var view = Shown("hello world");

        Select(view, 11, 11);
        view.ToggleStyle(TextStyle.Bold);

        Assert.True(view.SelectionMarks.Has(TextStyle.Bold));

        view.MoveTo(view.BlockStart + 2, extend: false);

        Assert.False(view.SelectionMarks.Has(TextStyle.Bold));
    }

    [AvaloniaFact]
    public void SuperscriptReplacesSubscriptRatherThanJoiningIt()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Sub);

        Assert.True(view.IsActive(TextStyle.Sub));

        view.ToggleStyle(TextStyle.Super);

        Assert.True(view.IsActive(TextStyle.Super));
        Assert.False(view.IsActive(TextStyle.Sub));
    }

    [AvaloniaFact]
    public void AColourLeavesBoldAlone()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Bold);
        view.SetForeground(new Rgba(0, 0, 255, 255));

        Assert.True(view.IsActive(TextStyle.Bold));
        Assert.Equal(new Rgba(0, 0, 255, 255), view.SelectionMarks.Foreground);
    }

    [AvaloniaFact]
    public void ClearingFormattingKeepsTheLinkItCannotPutBack()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Bold);
        view.SetForeground(new Rgba(255, 0, 0, 255));
        view.SetLink(new LinkMark("https://example.com"));

        Assert.True(view.ClearFormatting());

        Assert.False(view.IsActive(TextStyle.Bold));
        Assert.Null(view.SelectionMarks.Foreground);
        Assert.Equal("https://example.com", view.SelectionMarks.Link?.Href);
    }

    /// <summary>
    /// A mark step spans every paragraph a range touches, so this has to be one undo entry
    /// rather than three.
    /// </summary>
    [AvaloniaFact]
    public void FormattingAcrossParagraphsIsOneUndoEntry()
    {
        var view = Shown("alpha", "bravo", "charlie");

        view.SelectAll();

        Assert.True(view.ToggleStyle(TextStyle.Bold));

        Assert.Equal("BBBBB", Map(view, TextStyle.Bold, 0));
        Assert.Equal("BBBBB", Map(view, TextStyle.Bold, 1));
        Assert.Equal("BBBBBBB", Map(view, TextStyle.Bold, 2));

        Assert.True(view.Undo());

        Assert.Equal(".....", Map(view, TextStyle.Bold, 0));
        Assert.Equal(".....", Map(view, TextStyle.Bold, 1));
        Assert.Equal(".......", Map(view, TextStyle.Bold, 2));
    }

    [AvaloniaFact]
    public void UndoPutsBackExactlyTheFormattingThatWasThereBefore()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.SetForeground(new Rgba(255, 0, 0, 255));

        Select(view, 3, 8);
        view.SetForeground(new Rgba(0, 128, 0, 255));

        Assert.True(view.Undo());

        Select(view, 0, 11);

        Assert.Equal(
            "RRRRR......",
            Map(view, m => m.Foreground == new Rgba(255, 0, 0, 255) ? 'R' : '.'));
    }

    [AvaloniaFact]
    public void FormattingTextThatAlreadyHasItRecordsNothingToUndo()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);

        Assert.True(view.SetStyle(TextStyle.Bold, true));
        Assert.False(view.SetStyle(TextStyle.Bold, true));
    }

    [AvaloniaFact]
    public void AFontSizeThatIsNotAPositiveNumberIsRefused()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);

        Assert.Throws<ArgumentOutOfRangeException>(() => view.SetFontSize(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.SetFontSize(double.NaN));
    }

    [AvaloniaFact]
    public void SettingNoFieldsIsNotAnEdit()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);

        Assert.False(view.SetMarks(new MarkSet(style: TextStyle.Bold), MarkFields.None));
        Assert.Equal("...........", Map(view, TextStyle.Bold));
    }

    [AvaloniaFact]
    public void AStyleThatIsNotAStyleIsRefused()
    {
        var view = Shown("hello world");

        Assert.Throws<ArgumentOutOfRangeException>(() => view.ToggleStyle(TextStyle.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => view.SetStyle((TextStyle)(1 << 20), true));
    }

    [AvaloniaFact]
    public void FormattingKeepsTheSelectionSoThatTheNextCommandActsOnTheSameText()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Bold);

        Assert.Equal(view.BlockStart + 0, view.State.Selection.From);
        Assert.Equal(view.BlockStart + 5, view.State.Selection.To);
    }

    [AvaloniaFact]
    public void ControlBIsBold()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        Press(view, Key.B, KeyModifiers.Control);

        Assert.Equal("BBBBB......", Map(view, TextStyle.Bold));
    }

    [AvaloniaFact]
    public void ControlIIsItalic()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        Press(view, Key.I, KeyModifiers.Control);

        Assert.True(view.IsActive(TextStyle.Italic));
    }

    [AvaloniaFact]
    public void ControlUIsUnderline()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        Press(view, Key.U, KeyModifiers.Control);

        Assert.True(view.IsActive(TextStyle.Underline));
    }

    [AvaloniaFact]
    public void ControlShiftXIsStrikethrough()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        Press(view, Key.X, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.True(view.IsActive(TextStyle.Strikethrough));
    }

    /// <summary>
    /// A chord the control claims has to be swallowed whether or not it changed anything.
    /// Ctrl+Space that fell through because there was no formatting to clear would be loose in
    /// a control whose text input handler accepts spaces.
    /// </summary>
    [AvaloniaFact]
    public void ControlSpaceOnUnformattedTextIsStillSwallowed()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);

        var args = Press(view, Key.Space, KeyModifiers.Control);

        Assert.True(args.Handled);
        Assert.Equal("hello world", Text(view));
    }

    [AvaloniaFact]
    public void ControlSpaceStripsFormatting()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Bold);
        view.SetFontSize(24);

        Press(view, Key.Space, KeyModifiers.Control);

        Assert.False(view.IsActive(TextStyle.Bold));
        Assert.Null(view.SelectionMarks.FontSize);
    }

    /// <remarks>
    /// F-32. The shared formatting is read off the mark spans a selection crosses, from the first
    /// leaf it reaches, and the walk stops once nothing is shared. It must still agree with the
    /// definition — the formatting every selected character has — for selections that start and
    /// end inside a stretch, cross unformatted gaps and paragraph boundaries, and reach into
    /// table cells. The oracle here is that definition, character by character.
    /// </remarks>
    [AvaloniaFact]
    public void TheSharedFormattingIsWhatEverySelectedCharacterHas()
    {
        var random = new Random(32);
        var bold = new MarkSet { Style = TextStyle.Bold };
        var red = new MarkSet { Style = TextStyle.Bold, Foreground = new Rgba(200, 0, 0, 255) };
        var doc = new DocumentNode(
        [
            ParagraphNode.FromText("alpha beta gamma"),
            ParagraphNode.FromText("delta epsilon"),
            new TableNode([new TableRowNode([TableCellNode.FromText("zeta eta"), TableCellNode.FromText("theta")])]),
            ParagraphNode.Empty,
            ParagraphNode.FromText("iota kappa lambda"),
        ]);

        for (var i = 0; i < 12; i++)
        {
            var from = random.Next(doc.ContentSize);
            var to = Math.Min(doc.ContentSize, from + random.Next(1, 12));

            doc = new AddMarkStep(from, to, random.Next(2) == 0 ? bold : red, MarkFields.All)
                .Apply(doc).Doc!;
        }

        var view = new RichTextView { State = EditorState.Create(doc) };

        view.Measure(new Size(400, 300));
        view.Arrange(new Rect(0, 0, 400, 300));

        var leaves = DocumentBlocks.Leaves(doc).Where(l => l.Node is ParagraphNode).ToArray();

        for (var a = 0; a < leaves.Length; a++)
        {
            for (var b = a; b < leaves.Length; b++)
            {
                foreach (var (from, to) in new[]
                {
                    (leaves[a].Start, leaves[b].End),
                    (leaves[a].Start + 1, leaves[b].End - 1),
                    (leaves[a].Start + 3, leaves[b].Start + 4),
                })
                {
                    if (from >= to)
                    {
                        continue;
                    }

                    view.MoveTo(from, extend: false);
                    view.MoveTo(to, extend: true);

                    var selection = view.State.Selection;

                    // A drag into a table can become a rectangle of cells, which has its own
                    // reading; this test is about ranges of text.
                    if (selection is CellSelection)
                    {
                        continue;
                    }

                    Assert.Equal(
                        Expected(doc, selection.From, selection.To),
                        view.SelectionMarks);
                }
            }
        }

        // Every character in range, intersected, exactly as the definition says; a selection with
        // no characters in it reports the formatting typing would get.
        MarkSet? Expected(DocumentNode document, int from, int to)
        {
            MarkSet? common = null;

            foreach (var leaf in DocumentBlocks.Leaves(document))
            {
                if (leaf.Node is not ParagraphNode paragraph)
                {
                    continue;
                }

                for (var at = Math.Max(from, leaf.Start); at < Math.Min(to, leaf.End); at++)
                {
                    var mark = paragraph.Content.MarkAt(at - leaf.Start);

                    common = common is { } far
                        ? new MarkSet
                        {
                            Style = far.Style & mark.Style,
                            FontFamily = far.FontFamily == mark.FontFamily ? far.FontFamily : null,
                            FontSize = far.FontSize == mark.FontSize ? far.FontSize : null,
                            Foreground = far.Foreground == mark.Foreground ? far.Foreground : null,
                            Highlight = far.Highlight == mark.Highlight ? far.Highlight : null,
                            Link = far.Link == mark.Link ? far.Link : null,
                        }
                        : mark;
                }
            }

            return common ?? view.SelectionMarks;
        }
    }

    private static RichTextView Shown(params string[] paragraphs)
    {
        var window = new Window { Width = 400, Height = 300 };
        var view = new RichTextView
        {
            State = EditorState.Create(DocumentNode.FromParagraphs(paragraphs)),
        };

        window.Content = view;
        window.Show();
        window.Measure(new Size(400, 300));
        window.Arrange(new Rect(0, 0, 400, 300));

        view.Focus();

        return view;
    }

    /// <summary>
    /// Selects a range given in offsets within the first paragraph, by moving the caret the way
    /// a user does. Setting the selection through a transaction does not work: Apply drops a
    /// transaction that changed no text, so the selection would silently stay where it was.
    /// </summary>
    private static void Select(RichTextView view, int from, int to)
    {
        view.MoveTo(view.BlockStart + from, extend: false);
        view.MoveTo(view.BlockStart + to, extend: true);
    }

    private static KeyEventArgs Press(RichTextView view, Key key, KeyModifiers modifiers)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        };

        view.RaiseEvent(args);

        return args;
    }

    private static string Text(RichTextView view) =>
        ((ParagraphNode)view.Slots[0].Node).Content.Text;

    private static string Map(RichTextView view, TextStyle style, int block = 0) =>
        Map(view, m => m.Has(style) ? char.ToUpperInvariant(style.ToString()[0]) : '.', block);

    /// <summary>One character per character of the paragraph, showing what it is marked with.</summary>
    private static string Map(RichTextView view, Func<MarkSet, char> of, int block = 0)
    {
        var content = ((ParagraphNode)view.Slots[block].Node).Content;

        return string.Concat(Enumerable
            .Range(0, content.Text.Length)
            .Select(i => of(content.MarkAt(i))));
    }
    /// <summary>
    /// Setting a font or a resolver throws away every block layout, and the slot list with it.
    /// A formatting query that read the empty slot list would report the formatting at the
    /// start of the selection instead of what the whole selection shares -- so a half-bold
    /// selection whose first word is bold would report bold, and Ctrl+B would unbold it
    /// instead of bolding it.
    /// </summary>
    [AvaloniaFact]
    public void AQueryStillTellsTheTruthAfterTheLayoutHasBeenThrownAway()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Bold);

        Select(view, 0, 11);

        // What DropLayout is reached by: a font change on the control.
        TextElement.SetFontSize(view, 22);

        Assert.False(view.IsActive(TextStyle.Bold));

        view.ToggleStyle(TextStyle.Bold);

        Assert.Equal("BBBBBBBBBBB", Map(view, TextStyle.Bold));
    }
    /// <summary>
    /// The field mask has to name only the style being changed. A mask covering every style
    /// would make bolding italic text quietly un-italicise it.
    /// </summary>
    [AvaloniaFact]
    public void MakingItalicTextBoldLeavesItItalic()
    {
        var view = Shown("hello world");

        Select(view, 0, 5);
        view.ToggleStyle(TextStyle.Italic);
        view.ToggleStyle(TextStyle.Bold);

        Assert.True(view.IsActive(TextStyle.Italic));
        Assert.True(view.IsActive(TextStyle.Bold));

        view.ToggleStyle(TextStyle.Underline);

        Assert.True(view.IsActive(TextStyle.Italic));
        Assert.True(view.IsActive(TextStyle.Bold));
        Assert.True(view.IsActive(TextStyle.Underline));
    }
}
