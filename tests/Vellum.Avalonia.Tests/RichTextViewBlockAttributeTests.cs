using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Vellum;
using Vellum.Avalonia;

namespace Vellum.Tests.Avalonia;

/// <summary>
/// What a paragraph's kind, alignment and indent do to it, and the commands that set them.
/// </summary>
public class RichTextViewBlockAttributeTests
{
    [AvaloniaFact]
    public void AHeadingIsTallerThanABodyParagraph()
    {
        var view = Shown("a heading");
        var body = view.Blocks[0].Size.Height;

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading1));

        Assert.True(
            Remeasure(view)[0] > body * 1.5,
            $"heading measured {Remeasure(view)[0]} against a body height of {body}");
    }

    [AvaloniaFact]
    public void TheHeadingLevelsDescendInSize()
    {
        var heights = new List<double>();

        foreach (var kind in new[]
        {
            ParagraphKind.Heading1,
            ParagraphKind.Heading2,
            ParagraphKind.Heading3,
            ParagraphKind.Heading4,
        })
        {
            var view = Shown("heading");

            view.SetParagraphKind(kind);
            heights.Add(Remeasure(view)[0]);
        }

        for (var i = 1; i < heights.Count; i++)
        {
            Assert.True(
                heights[i] < heights[i - 1],
                $"level {i + 1} measured {heights[i]}, which is not below level {i}'s {heights[i - 1]}");
        }
    }

    [AvaloniaFact]
    public void AKindChangeDoesNotChangeAnyText()
    {
        var view = Shown("a heading");

        view.SetParagraphKind(ParagraphKind.Heading1);

        Assert.Equal("a heading", ((ParagraphNode)view.Slots[0].Node).Content.Text);
        Assert.True(((ParagraphNode)view.Slots[0].Node).Content.Marks.IsEmpty);
    }

    [AvaloniaFact]
    public void AQuoteIsDrawnWithABarDownItsLeftEdge()
    {
        var view = Shown("quoted");

        // The bar's colour is supplied by the test rather than read from the library's default,
        // so the assertion is about the mechanism and survives a change of palette. It is a hue
        // no glyph is painted in, which is what lets the count discriminate.
        view.ParagraphStyles = new BarStyles(BarColour);

        // Counting *bar-coloured* rows, not painted ones. The glyphs of an ordinary paragraph
        // already reach the leftmost columns, so "anything is painted there" is true before the
        // quote exists and would let a test pass that proves nothing.
        var before = BarRows(view);

        Assert.True(view.SetParagraphKind(ParagraphKind.Quote));

        Assert.Equal(0, before);
        Assert.True(BarRows(view) > 0, "no bar was drawn down the quote's left edge");
    }

    [AvaloniaFact]
    public void AQuotesTextIsPushedRightOfItsBar()
    {
        var view = Shown("quoted");
        var before = view.Blocks[0].GetCaretRect(0).X;

        view.SetParagraphKind(ParagraphKind.Quote);
        Remeasure(view);

        Assert.True(
            view.Blocks[0].GetCaretRect(0).X > before,
            "the quote's first caret did not move right of the bar");
    }

    [AvaloniaFact]
    public void AnIndentedParagraphStartsFurtherRight()
    {
        var view = Shown("indent me");
        var before = view.Blocks[0].GetCaretRect(0).X;

        Assert.True(view.Indent());
        Remeasure(view);

        Assert.True(view.Blocks[0].GetCaretRect(0).X > before);
        Assert.Equal(1, ((ParagraphNode)view.Slots[0].Node).IndentLevel);
    }

    [AvaloniaFact]
    public void OutdentingStopsAtZeroRatherThanGoingNegative()
    {
        var view = Shown("indent me");

        Assert.False(view.Outdent());
        Assert.Equal(0, ((ParagraphNode)view.Slots[0].Node).IndentLevel);
    }

    [AvaloniaFact]
    public void IndentingStopsAtTheLimit()
    {
        var view = Shown("indent me");

        for (var i = 0; i < RichTextView.MaxIndentLevel; i++)
        {
            Assert.True(view.Indent());
        }

        Assert.False(view.Indent());
        Assert.Equal(RichTextView.MaxIndentLevel, ((ParagraphNode)view.Slots[0].Node).IndentLevel);
    }

    [AvaloniaFact]
    public void CentringMovesTheCaretRectRight()
    {
        var view = Shown("short");
        var before = view.Blocks[0].GetCaretRect(0).X;

        Assert.True(view.SetAlign(TextAlign.Center));
        Remeasure(view);

        Assert.True(view.Blocks[0].GetCaretRect(0).X > before);
        Assert.Equal(TextAlign.Center, ((ParagraphNode)view.Slots[0].Node).Align);
    }

    [AvaloniaFact]
    public void ACommandAppliesToEveryBlockTheSelectionTouches()
    {
        var view = Shown("first", "second", "third");

        view.MoveTo(view.Slots[0].Start + 2, extend: false);
        view.MoveTo(view.Slots[1].Start + 2, extend: true);

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading2));

        Assert.Equal(ParagraphKind.Heading2, Kind(view, 0));
        Assert.Equal(ParagraphKind.Heading2, Kind(view, 1));
        Assert.Equal(ParagraphKind.Body, Kind(view, 2));
    }

    [AvaloniaFact]
    public void ACommandSkipsABlockTheSelectionOnlyReachesTheEdgeOf()
    {
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[0].End, extend: true);

        view.SetParagraphKind(ParagraphKind.Heading2);

        Assert.Equal(ParagraphKind.Heading2, Kind(view, 0));
        Assert.Equal(ParagraphKind.Body, Kind(view, 1));
    }

    [AvaloniaFact]
    public void ACollapsedCaretChangesOnlyItsOwnBlock()
    {
        var view = Shown("first", "second");

        view.MoveTo(view.Slots[1].Start + 1, extend: false);
        view.SetParagraphKind(ParagraphKind.Quote);

        Assert.Equal(ParagraphKind.Body, Kind(view, 0));
        Assert.Equal(ParagraphKind.Quote, Kind(view, 1));
    }

    [AvaloniaFact]
    public void ChangingSeveralBlocksIsOneUndo()
    {
        var view = Shown("first", "second", "third");

        Press(view, Key.A, KeyModifiers.Control);
        Assert.True(view.SetParagraphKind(ParagraphKind.Heading3));

        Assert.True(view.Undo());

        Assert.All(new[] { 0, 1, 2 }, i => Assert.Equal(ParagraphKind.Body, Kind(view, i)));
    }

    [AvaloniaFact]
    public void SettingTheKindItAlreadyHasChangesNothing()
    {
        var view = Shown("first");
        var before = view.State.Doc;

        Assert.False(view.SetParagraphKind(ParagraphKind.Body));
        Assert.Same(before, view.State.Doc);
    }

    [AvaloniaFact]
    public void ARuleInTheSelectionIsLeftAlone()
    {
        var view = Shown(new DocumentNode([
            ParagraphNode.FromText("above"),
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]));

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.MoveTo(view.Slots[2].End, extend: true);

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading2));

        Assert.Equal(ParagraphKind.Heading2, Kind(view, 0));
        Assert.IsType<RuleNode>(view.Slots[1].Node);
        Assert.Equal(ParagraphKind.Heading2, Kind(view, 2));
    }

    [AvaloniaFact]
    public void TabIndentsAndShiftTabOutdents()
    {
        var view = Shown("indent me");

        Press(view, Key.Tab);
        Assert.Equal(1, ((ParagraphNode)view.Slots[0].Node).IndentLevel);

        Press(view, Key.Tab, KeyModifiers.Shift);
        Assert.Equal(0, ((ParagraphNode)view.Slots[0].Node).IndentLevel);
    }

    [AvaloniaFact]
    public void ATabThatChangesNothingIsLeftForTheFocusManager()
    {
        // Shift+Tab at indent zero must not be swallowed, or the control traps keyboard focus.
        // Asserting on Handled would prove nothing: Avalonia's own navigation handler runs after
        // the control and marks the event handled precisely when the control declined it. So this
        // asserts the outcome that matters instead — focus actually leaves.
        var root = new Window { Width = 300, Height = 200 };
        var earlier = new Button { Content = "before" };
        var view = new RichTextView { State = EditorState.Create(DocumentNode.FromParagraphs("stay put")) };

        root.Content = new StackPanel { Children = { earlier, view } };
        root.Show();
        view.Measure(new Size(300, 200));
        view.Arrange(new Rect(0, 0, 300, 200));
        view.Focus();

        Assert.True(view.IsFocused);

        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Tab,
            KeyModifiers = KeyModifiers.Shift,
            Source = view,
        });

        Assert.False(view.IsFocused, "Shift+Tab at indent zero trapped keyboard focus");
        Assert.True(earlier.IsFocused);
        Assert.Equal(0, ((ParagraphNode)view.Slots[0].Node).IndentLevel);
    }

    [AvaloniaFact]
    public void CtrlOneSetsAHeadingAndCtrlZeroClearsIt()
    {
        var view = Shown("a heading");

        Press(view, Key.D1, KeyModifiers.Control);
        Assert.Equal(ParagraphKind.Heading1, Kind(view, 0));

        Press(view, Key.D0, KeyModifiers.Control);
        Assert.Equal(ParagraphKind.Body, Kind(view, 0));
    }

    [AvaloniaFact]
    public void CtrlESetsCentreAlignment()
    {
        var view = Shown("centre me");

        Press(view, Key.E, KeyModifiers.Control);

        Assert.Equal(TextAlign.Center, ((ParagraphNode)view.Slots[0].Node).Align);
    }

    [AvaloniaFact]
    public void AHostCanReplaceTheParagraphStyles()
    {
        var view = Shown("a heading");

        view.SetParagraphKind(ParagraphKind.Heading1);
        var tall = Remeasure(view)[0];

        view.ParagraphStyles = new FlatStyles();

        Assert.True(
            Remeasure(view)[0] < tall,
            "replacing the resolver did not change how the heading is presented");
    }

    /// <summary>A resolver that presents every kind exactly like body text.</summary>
    private sealed class FlatStyles : IParagraphStyleResolver
    {
        public ParagraphStyle Resolve(ParagraphKind kind) => ParagraphStyle.Body;
    }

    private static ParagraphKind Kind(RichTextView view, int index) =>
        ((ParagraphNode)view.Slots[index].Node).Kind;

    /// <summary>How many rows carry the quote bar's grey in the leftmost columns.</summary>
    private static int BarRows(RichTextView view)
    {
        const int width = 300;
        const int height = 200;

        Remeasure(view);

        using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(
            new PixelSize(width, height));

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context);
        }

        var pixels = new uint[width * height];

        unsafe
        {
            fixed (uint* buffer = pixels)
            {
                bitmap.CopyPixels(
                    new PixelRect(new PixelSize(width, height)),
                    (nint)buffer,
                    pixels.Length * sizeof(uint),
                    width * sizeof(uint));
            }
        }

        var rows = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                var pixel = pixels[(y * width) + x];

                if ((pixel & 0xFF000000) == 0xFF000000
                    && (pixel & 0x00FFFFFF) == BarColour)
                {
                    rows++;
                    break;
                }
            }
        }

        return rows;
    }

    /// <summary>An opaque hue nothing else in the test draws in, so counting it means something.</summary>
    private const uint BarColour = 0x00FF00FF;

    /// <summary>A resolver whose quote bar is a known colour and whose other kinds are plain.</summary>
    private sealed class BarStyles(uint colour) : IParagraphStyleResolver
    {
        public ParagraphStyle Resolve(ParagraphKind kind) => kind == ParagraphKind.Quote
            ? new ParagraphStyle(
                Bar: new SolidColorBrush(Color.FromUInt32(colour | 0xFF000000)),
                BarWidth: 3,
                BarGap: 8)
            : ParagraphStyle.Body;
    }

    private static double[] Remeasure(RichTextView view)
    {
        view.Measure(new Size(300, 200));
        view.Arrange(new Rect(0, 0, 300, 200));

        return view.Blocks.Select(block => block.Size.Height).ToArray();
    }

    private static void Press(RichTextView view, Key key, KeyModifiers modifiers = KeyModifiers.None) =>
        view.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = view,
        });

    [AvaloniaFact]
    public void ASelectionStartingAtAParagraphsEndLeavesThatParagraphAlone()
    {
        // A selection that begins exactly where a paragraph ends touches no character of it.
        // Formatting it anyway would restyle a paragraph the user never highlighted.
        var view = Shown("first", "second");
        var doc = view.State.Doc;

        view.State = EditorState.Create(
            doc,
            TextSelection.Create(doc, view.Slots[0].End, view.Slots[1].Start + 1));

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading1));

        Assert.Equal(ParagraphKind.Body, Kind(view, 0));
        Assert.Equal(ParagraphKind.Heading1, Kind(view, 1));
    }

    [AvaloniaFact]
    public void AnOutOfRangeIndentLevelIsClampedRatherThanStored()
    {
        var view = Shown("clamp me");

        Assert.True(view.SetIndentLevel(99));
        Assert.Equal(RichTextView.MaxIndentLevel, ((ParagraphNode)view.Slots[0].Node).IndentLevel);

        Assert.True(view.SetIndentLevel(-3));
        Assert.Equal(0, ((ParagraphNode)view.Slots[0].Node).IndentLevel);
    }

    [AvaloniaFact]
    public void TheSpaceAboveAHeadingPushesItsTextAndItsHitTestingDown()
    {
        var view = Shown("a heading");

        Assert.Equal(0, view.Blocks[0].GetCaretRect(0).Y);

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading1));
        Remeasure(view);

        var caret = view.Blocks[0].GetCaretRect(0);

        Assert.True(caret.Y > 0, $"the heading's first line sits at y={caret.Y}, ignoring its space above");

        // The same offset has to apply to hit testing, or a click lands a line out.
        Assert.Equal(0, view.Blocks[0].HitTest(new Point(1, caret.Y + 1)));
    }

    [AvaloniaFact]
    public void ABoldMarkBeatsAKindThatAsksForALighterWeight()
    {
        // Marks are content and the kind is presentation, so the mark wins. Every built-in kind
        // that names a weight names Bold, which hides the question -- but ParagraphStyles is a
        // public extension point and a host may well ask for something lighter.
        var doc = DocumentNode.FromParagraphs("heavier or lighter");

        var light = Shown(doc);
        light.ParagraphStyles = new LightHeadings();
        light.SetParagraphKind(ParagraphKind.Heading1);
        Remeasure(light);

        var bold = Shown(new DocumentNode([
            ((ParagraphNode)doc.Blocks[0]).WithContent(
                InlineContent.FromText("heavier or lighter")
                    .ApplyMarks(0, 18, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold))]));

        bold.ParagraphStyles = new LightHeadings();
        bold.SetParagraphKind(ParagraphKind.Heading1);
        Remeasure(bold);

        Assert.Equal(FontWeight.Light, Weight(light));
        Assert.Equal(FontWeight.Bold, Weight(bold));
    }

    [AvaloniaFact]
    public void ACommandIssuedRightAfterReplacingTheStylesStillLands()
    {
        // Replacing a resolver drops the laid-out blocks. A command that consulted that empty
        // list would report failure and change nothing, which reads to a host as a command that
        // does not work.
        var view = Shown("restyle me");

        view.ParagraphStyles = new LightHeadings();

        Assert.True(view.SetParagraphKind(ParagraphKind.Heading1));
        Assert.Equal(ParagraphKind.Heading1, Kind(view, 0));
    }

    [AvaloniaFact]
    public void AKindThatOnlyChangesTheFamilyStillReachesTheRuns()
    {
        // The one property of a kind that changes nothing about size or weight. A resolver that
        // decided "this style changes nothing" on size and weight alone would drop it silently.
        var view = Shown("monospaced");

        view.ParagraphStyles = new MonospaceCode();

        Assert.True(view.SetParagraphKind(ParagraphKind.Code));
        Remeasure(view);

        Assert.Equal(
            "Courier New",
            ((ParagraphView)view.Blocks[0]).Layout.TextLines[0].TextRuns[0]
                .Properties!.Typeface.FontFamily.Name);
    }

    [AvaloniaFact]
    public void AFamilyMarkBeatsTheFamilyItsKindAsksFor()
    {
        // Same rule as weight: a family the author chose is content and survives the kind.
        var view = Shown(new DocumentNode([
            ParagraphNode.Empty.WithContent(
                InlineContent.FromText("chosen")
                    .ApplyMarks(0, 6, new MarkSet { FontFamily = "Georgia" }, MarkFields.FontFamily))]));

        view.ParagraphStyles = new MonospaceCode();

        Assert.True(view.SetParagraphKind(ParagraphKind.Code));
        Remeasure(view);

        Assert.Equal(
            "Georgia",
            ((ParagraphView)view.Blocks[0]).Layout.TextLines[0].TextRuns[0]
                .Properties!.Typeface.FontFamily.Name);
    }

    /// <summary>Code set in a named family and otherwise left alone.</summary>
    private sealed class MonospaceCode : IParagraphStyleResolver
    {
        public ParagraphStyle Resolve(ParagraphKind kind) => kind == ParagraphKind.Code
            ? new ParagraphStyle(Family: new FontFamily("Courier New"))
            : ParagraphStyle.Body;
    }

    private static FontWeight Weight(RichTextView view) =>
        ((ParagraphView)view.Blocks[0]).Layout.TextLines[0].TextRuns[0].Properties!.Typeface.Weight;

    /// <summary>Headings drawn lighter than the body, which no built-in kind asks for.</summary>
    private sealed class LightHeadings : IParagraphStyleResolver
    {
        public ParagraphStyle Resolve(ParagraphKind kind) => kind == ParagraphKind.Heading1
            ? new ParagraphStyle(FontScale: 2, Weight: FontWeight.Light)
            : ParagraphStyle.Body;
    }
    private static RichTextView Shown(params string[] paragraphs) =>
        Shown(DocumentNode.FromParagraphs(paragraphs));

    private static RichTextView Shown(DocumentNode doc)
    {
        var root = new Window { Width = 300, Height = 200 };
        var view = new RichTextView { State = EditorState.Create(doc) };

        root.Content = view;
        root.Show();
        view.Focus();

        view.Measure(new Size(300, 200));
        view.Arrange(new Rect(0, 0, 300, 200));

        return view;
    }
}
