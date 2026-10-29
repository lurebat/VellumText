using System.Collections.Immutable;
using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Headless;
using global::Avalonia.Headless.XUnit;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Find and replace in the editor, per architecture 10.3 P7.
/// </summary>
/// <remarks>
/// The search itself is covered in <c>DocumentSearchTests</c>. What is left to this file is
/// everything that needs a view: that the highlights survive an edit, that stepping lands on the
/// match rather than near it, and that a replacement is one undo.
/// </remarks>
public class RichTextViewFindTests
{
    private const double Width = 400;
    private const double Height = 300;

    [AvaloniaFact]
    public void FindingReportsHowManyThereAre()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat", "on the cat"));

        Assert.Equal(2, view.Find("cat"));
        Assert.Equal(2, view.Matches.Length);
    }

    /// <remarks>
    /// A user typing into a find box wants to see where the matches are. Scrolling away on every
    /// keystroke, before they have finished the word, is the behaviour people turn off.
    /// </remarks>
    [AvaloniaFact]
    public void FindingDoesNotMoveTheSelection()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat"));
        var before = view.State.Selection;

        view.Find("cat");

        Assert.Equal(-1, view.CurrentMatch);
        Assert.Equal(before, view.State.Selection);
    }

    [AvaloniaFact]
    public void SteppingSelectsTheMatch()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat"));

        view.Find("cat");

        Assert.True(view.FindNext());
        Assert.Equal(0, view.CurrentMatch);
        Assert.Equal("cat", view.SelectedText());
    }

    /// <remarks>
    /// Stepping from the far end of the selection is what stops Find Next finding the match it is
    /// already sitting on, forever.
    /// </remarks>
    [AvaloniaFact]
    public void SteppingMovesOffTheMatchItIsOn()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat cat cat"));

        view.Find("cat");

        Assert.True(view.FindNext());
        Assert.Equal(0, view.CurrentMatch);

        Assert.True(view.FindNext());
        Assert.Equal(1, view.CurrentMatch);

        Assert.True(view.FindNext());
        Assert.Equal(2, view.CurrentMatch);
    }

    [AvaloniaFact]
    public void SteppingWrapsAtEitherEnd()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat dog cat"));

        view.Find("cat");

        Assert.True(view.FindNext());
        Assert.True(view.FindNext());
        Assert.Equal(1, view.CurrentMatch);

        Assert.True(view.FindNext());
        Assert.Equal(0, view.CurrentMatch);

        Assert.True(view.FindPrevious());
        Assert.Equal(1, view.CurrentMatch);
    }

    [AvaloniaFact]
    public void SteppingThroughNothingFindsNothing()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat"));

        view.Find("giraffe");

        Assert.False(view.FindNext());
        Assert.False(view.FindPrevious());
        Assert.Equal(-1, view.CurrentMatch);
    }

    /// <remarks>
    /// The whole reason the matches are re-found rather than mapped. A replacement of a different
    /// length moves every later match, and a stale list would highlight the wrong text and then
    /// replace it.
    /// </remarks>
    [AvaloniaFact]
    public void MatchesFollowAnEdit()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat cat"));

        Assert.Equal(2, view.Find("cat"));

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.InsertText("a much longer prefix ");

        Assert.Equal(2, view.Matches.Length);

        foreach (var match in view.Matches)
        {
            Assert.Equal("cat", DocumentText.Of(Slice.Cut(view.State.Doc, match.From, match.To).Content));
        }
    }

    [AvaloniaFact]
    public void MatchesAppearAndVanishAsTheDocumentChanges()
    {
        var view = Realized(DocumentNode.FromParagraphs("nothing here"));

        Assert.Equal(0, view.Find("cat"));

        view.MoveTo(view.Slots[0].End, extend: false);
        view.InsertText(" cat");

        Assert.Single(view.Matches);
    }

    /// <remarks>
    /// The match a user was on cannot be identified across an edit — its positions moved by an
    /// amount only the transaction's mapping knows — so it is given up rather than guessed at.
    /// Nothing depends on it surviving: Replace steps explicitly.
    /// </remarks>
    [AvaloniaFact]
    public void AnEditGivesUpTheCurrentMatch()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat and cat"));

        view.Find("cat");
        view.FindNext();
        view.FindNext();

        Assert.Equal(1, view.CurrentMatch);

        view.MoveTo(view.Slots[0].Start, extend: false);
        view.InsertText("a much longer prefix ");

        Assert.Equal(-1, view.CurrentMatch);
        Assert.Equal(2, view.Matches.Length);
    }

    /// <remarks>
    /// Moving the caret is the commonest state change there is, and the one stepping between
    /// matches goes through. Re-running the search on each would make a find bar quadratic, so
    /// this asserts on the identity of the array, not merely on its contents.
    /// </remarks>
    [AvaloniaFact]
    public void MovingTheCaretDoesNotDisturbTheCurrentMatch()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat cat"));

        view.Find("cat");
        view.FindNext();

        var matches = view.Matches;

        view.MoveTo(view.Slots[0].End, extend: false);

        Assert.Equal(0, view.CurrentMatch);
        Assert.True(matches == view.Matches);
    }

    [AvaloniaFact]
    public void ClearingTakesTheHighlightsAway()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat cat"));

        view.Find("cat");
        view.ClearFind();

        Assert.Empty(view.Matches);
        Assert.Equal(-1, view.CurrentMatch);
        Assert.Equal(string.Empty, view.Query);
    }

    [AvaloniaFact]
    public void AnEmptyQueryEndsTheSearch()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat cat"));

        view.Find("cat");

        Assert.Equal(0, view.Find(string.Empty));
        Assert.Empty(view.Matches);
    }

    [AvaloniaFact]
    public void ReplacingTheCurrentMatchWritesOverIt()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat"));

        view.Find("cat");
        view.FindNext();

        Assert.True(view.ReplaceCurrent("dog"));
        Assert.Equal("the dog sat", DocumentText.Of(view.State.Doc));
    }

    /// <remarks>
    /// So that holding Replace walks the document exactly as pressing Find Next would.
    /// </remarks>
    [AvaloniaFact]
    public void ReplacingStepsOnToTheNextMatch()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat and cat"));

        view.Find("cat");
        view.FindNext();

        Assert.True(view.ReplaceCurrent("dog"));

        Assert.Single(view.Matches);
        Assert.Equal(0, view.CurrentMatch);
        Assert.Equal("cat", view.SelectedText());
    }

    [AvaloniaFact]
    public void ReplacingNothingReplacesNothing()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat"));

        view.Find("cat");

        Assert.False(view.ReplaceCurrent("dog"));
        Assert.Equal("the cat sat", DocumentText.Of(view.State.Doc));
    }

    /// <remarks>
    /// Replace acts on the match the user stepped to, not on whatever happens to be selected.
    /// Taking the selection would make clicking away from the match, then pressing Replace,
    /// overwrite ordinary text that was never a match at all.
    /// </remarks>
    [AvaloniaFact]
    public void ReplacingIgnoresASelectionThatIsNotTheMatch()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat and dogs"));

        view.Find("cat");
        view.FindNext();

        view.MoveTo(view.Slots[0].Start + 8, extend: false);
        view.MoveTo(view.Slots[0].Start + 12, extend: true);
        Assert.Equal("dogs", view.SelectedText());

        Assert.True(view.ReplaceCurrent("ox"));
        Assert.Equal("ox and dogs", DocumentText.Of(view.State.Doc));
    }

    /// <remarks>
    /// Back to front, or the first replacement of a different length moves every match after it
    /// and the second lands in the wrong place. The lengths here differ deliberately.
    /// </remarks>
    [AvaloniaFact]
    public void ReplacingAllRewritesEveryMatch()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat sat on cat", "cat"));

        view.Find("cat");

        Assert.Equal(3, view.ReplaceAll("elephant"));
        Assert.Equal("elephant sat on elephant\nelephant", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void OneUndoTakesBackAReplaceAll()
    {
        var view = Realized(DocumentNode.FromParagraphs("cat sat on cat"));

        view.Find("cat");
        view.ReplaceAll("dog");

        Assert.True(view.Undo());
        Assert.Equal("cat sat on cat", DocumentText.Of(view.State.Doc));
    }

    /// <remarks>
    /// Matches that abut cannot share one replacement step, whose ranges must not touch, so they
    /// are split across two. It is still one undo, and the caret between two of them stays put.
    /// </remarks>
    [AvaloniaFact]
    public void AbuttingMatchesAreAllReplacedAndUndoneAsOne()
    {
        var view = Realized(DocumentNode.FromParagraphs("aaaa b aa"));

        view.MoveTo(3, extend: false);
        view.Find("aa");

        Assert.Equal(3, view.ReplaceAll("xyz"));
        Assert.Equal("xyzxyz b xyz", DocumentText.Of(view.State.Doc));
        Assert.Equal(TextSelection.Cursor(4), view.State.Selection);

        Assert.True(view.Undo());
        Assert.Equal("aaaa b aa", DocumentText.Of(view.State.Doc));
        Assert.False(view.Undo());
    }

    [AvaloniaFact]
    public void ReplacingWithNothingDeletes()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat"));

        view.Find(" cat");

        Assert.Equal(1, view.ReplaceAll(string.Empty));
        Assert.Equal("the sat", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void ReplacingAllOfNothingDoesNothing()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat"));

        view.Find("giraffe");

        Assert.Equal(0, view.ReplaceAll("dog"));
        Assert.Equal("the cat sat", DocumentText.Of(view.State.Doc));
    }

    [AvaloniaFact]
    public void CaseAndWholeWordAreCarried()
    {
        var view = Realized(DocumentNode.FromParagraphs("Cat cat scatter"));

        Assert.Equal(3, view.Find("cat"));
        Assert.Equal(1, view.Find("cat", new SearchOptions { MatchCase = true, WholeWord = true }));
    }

    /// <remarks>
    /// Rendering is the point of the feature, and the only way to tell a highlight that is drawn
    /// from one that is merely computed is to look at the pixels.
    /// </remarks>
    [AvaloniaFact]
    public void MatchesArePainted()
    {
        var plain = Pixels(DocumentNode.FromParagraphs("the cat sat"), search: null);
        var found = Pixels(DocumentNode.FromParagraphs("the cat sat"), search: "cat");

        Assert.NotEqual(plain, found);
    }

    /// <remarks>
    /// The match the user is on has to be distinguishable from the rest, or Find Next appears to
    /// do nothing on a screen where every match is already highlighted.
    /// </remarks>
    [AvaloniaFact]
    public void TheCurrentMatchIsPaintedDifferently()
    {
        var found = Pixels(DocumentNode.FromParagraphs("the cat sat"), search: "cat");
        var current = Pixels(DocumentNode.FromParagraphs("the cat sat"), search: "cat", step: true);

        Assert.NotEqual(found, current);
    }

    /// <remarks>
    /// Stepping to a match selects it, so the current highlight is always painted exactly where
    /// the selection is. Painting it underneath leaves the selection tint mixed in and the result
    /// reads as neither — so it goes over. Counting the pixels that are exactly the highlight
    /// colour says that directly: with the layers the wrong way round the translucent selection
    /// tints every one of them and the count falls to zero. Comparing whole pictures cannot be
    /// used here, because the two differ at the antialiased rectangle edges either way.
    /// </remarks>
    [AvaloniaFact]
    public void TheCurrentHighlightIsDrawnOverTheSelection()
    {
        var selected = Pixels(
            DocumentNode.FromParagraphs("the cat sat"),
            search: "cat",
            step: true,
            selection: true,
            currentBrush: Brushes.Orange);

        var unselected = Pixels(
            DocumentNode.FromParagraphs("the cat sat"),
            search: "cat",
            step: true,
            currentBrush: Brushes.Orange);

        Assert.True(CountOf(unselected, Colors.Orange) > 0, "nothing was painted orange");
        Assert.Equal(CountOf(unselected, Colors.Orange), CountOf(selected, Colors.Orange));
    }

    /// <remarks>
    /// Both highlight brushes are translucent, so painting the plain one under the current one
    /// would blend the two and leave the current match's colour depending on a brush that is not
    /// supposed to apply to it.
    /// </remarks>
    [AvaloniaFact]
    public void TheCurrentHighlightIsNotBlendedWithThePlainOne()
    {
        var loud = Pixels(
            DocumentNode.FromParagraphs("the cat sat"),
            search: "cat",
            step: true,
            findBrush: Brushes.Blue);

        var quiet = Pixels(
            DocumentNode.FromParagraphs("the cat sat"),
            search: "cat",
            step: true,
            findBrush: Brushes.Transparent);

        Assert.Equal(quiet, loud);
    }

    /// <remarks>
    /// F-13. Painting now looks only at the matches inside the blocks on screen, found by binary
    /// search. Scrolled to the middle of a document of identical paragraphs, aligned so a block
    /// starts exactly at the top, the screen has to be the same picture as the top of the
    /// document, highlights included: a search that started a match late, or stopped a block
    /// early, would leave the first or last line on screen unpainted.
    /// </remarks>
    [AvaloniaFact]
    public void MatchesArePaintedTheSameAfterScrollingDeepIntoTheDocument()
    {
        var doc = new DocumentNode(Enumerable.Range(0, 3_000)
            .Select(_ => (BlockNode)ParagraphNode.FromText("needle in the haystack, needle"))
            .ToArray());

        var top = ScrolledPixels(doc, "needle", block: 0);
        var middle = ScrolledPixels(doc, "needle", block: 1_500);

        Assert.True(CountOf(top, Colors.Blue) > 0, "nothing was painted blue");
        Assert.Equal(top, middle);
    }

    /// <remarks>
    /// A font change throws every layout away until the next measure pass, and a frame can be
    /// painted in between. With a search open, painting the highlights then has no realized
    /// blocks to look the matches up against, and must paint none rather than index an empty
    /// block list.
    /// </remarks>
    [AvaloniaFact]
    public void PaintingWithASearchOpenAfterTheLayoutIsDroppedPaintsNothingAndDoesNotThrow()
    {
        var view = Realized(DocumentNode.FromParagraphs("the cat sat", "on the cat"));

        view.Find("cat");
        view.FontSize = 31;

        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Width, (int)Height));
        using var context = bitmap.CreateDrawingContext();

        view.Render(context);
    }

    /// <summary>
    /// Renders <paramref name="doc"/> scrolled so that <paramref name="block"/> starts at the top
    /// of the window, with every match of <paramref name="search"/> painted in solid blue.
    /// </summary>
    private static byte[] ScrolledPixels(DocumentNode doc, string search, int block)
    {
        var view = new RichTextView
        {
            State = EditorState.Create(doc),
            SelectionBrush = null,
            FindHighlightBrush = Brushes.Blue,
        };

        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        view.Find(search);

        // Twice: the first pass lays out the blocks around the target and replaces their
        // estimated heights with real ones, which moves the target; the second lands on it.
        for (var i = 0; i < 2; i++)
        {
            view.Offset = new Vector(0, view.Offset.Y + view.BlockOrigin(block).Y);
            window.Measure(new Size(Width, Height));
            window.Arrange(new Rect(0, 0, Width, Height));
        }

        Assert.Equal(0, view.BlockOrigin(block).Y, 3);

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");

        return RenderedPixels.Rgba(frame);
    }

    [AvaloniaFact]
    public void ItRefusesNull()
    {
        var view = Realized(DocumentNode.FromParagraphs("hello"));

        Assert.Throws<ArgumentNullException>(() => view.Find(null!));
        Assert.Throws<ArgumentNullException>(() => view.ReplaceCurrent(null!));
        Assert.Throws<ArgumentNullException>(() => view.ReplaceAll(null!));
    }

    /// <summary>
    /// Renders a document with an optional search open, and returns the raw RGBA pixels.
    /// </summary>
    /// <remarks>
    /// The selection brush is cleared by default so that a difference in the picture can only have
    /// come from the find highlight — stepping to a match also selects it, and a test that could
    /// not tell the two apart would pass whether or not a highlight was ever drawn. A null brush
    /// argument leaves the control's own default in place; pass <see cref="Brushes.Transparent"/>
    /// to paint nothing while keeping the same drawing path.
    /// </remarks>
    private static byte[] Pixels(
        DocumentNode doc,
        string? search,
        bool step = false,
        bool selection = false,
        IBrush? findBrush = null,
        IBrush? currentBrush = null)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };

        if (!selection)
        {
            view.SelectionBrush = null;
        }

        if (findBrush is not null)
        {
            view.FindHighlightBrush = findBrush;
        }

        if (currentBrush is not null)
        {
            view.CurrentFindHighlightBrush = currentBrush;
        }

        var window = new Window { Content = view, Width = Width, Height = Height };

        window.Show();
        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        if (search is not null)
        {
            view.Find(search);

            if (step)
            {
                view.FindNext();
            }
        }

        window.Measure(new Size(Width, Height));
        window.Arrange(new Rect(0, 0, Width, Height));

        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("nothing was rendered");

        return RenderedPixels.Rgba(frame);
    }

    /// <summary>Counts the pixels painted in exactly <paramref name="colour"/>.</summary>
    /// <remarks>
    /// <paramref name="rgba"/> comes from <see cref="RenderedPixels.Rgba"/>: reading a frame in the
    /// wrong channel order silently counts nothing, which would make every assertion built on it
    /// vacuous.
    /// </remarks>
    private static int CountOf(byte[] rgba, Color colour)
    {
        var found = 0;

        for (var i = 0; i + 3 < rgba.Length; i += 4)
        {
            if (rgba[i] == colour.R
                && rgba[i + 1] == colour.G
                && rgba[i + 2] == colour.B
                && rgba[i + 3] == colour.A)
            {
                found++;
            }
        }

        return found;
    }

    private static RichTextView Realized(DocumentNode doc)
    {
        var view = new RichTextView { State = EditorState.Create(doc) };

        view.Measure(new Size(Width, Height));
        view.Arrange(new Rect(0, 0, Width, Height));

        return view;
    }
}
