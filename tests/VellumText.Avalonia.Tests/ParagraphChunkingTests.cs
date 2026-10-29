using System.Text;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using VellumText;
using VellumText.Avalonia;

namespace VellumText.Tests.Avalonia;

/// <summary>
/// Line-range sub-layouts (F-33): a long paragraph laid out in chunks answers every geometry
/// question exactly as the same paragraph laid out in one piece.
/// </summary>
/// <remarks>
/// <para>
/// Each test lays one paragraph out twice — once with chunking off, once with chunks small enough
/// to put a seam every few lines — and compares everything a <see cref="ParagraphView"/> answers:
/// size, lines, caret rectangles and movement at every position, hit tests across a grid, selection
/// rectangles across every seam, image boxes, composition carets, and the pixels it draws.
/// Equality is exact, not approximate: the chunked path reproduces Avalonia's own arithmetic in the
/// same order, so any difference at all is a seam that is not where the unchunked layout breaks.
/// </para>
/// <para>
/// A test that compared two unchunked layouts would pass too, so every one also asserts that the
/// chunked view really is in many chunks — or, for the content that must never be chunked, that it
/// is not.
/// </para>
/// </remarks>
public class ParagraphChunkingTests
{
    private const double Width = 300;

    /// <summary>Small enough to put a seam every three or four lines at <see cref="Width"/>.</summary>
    private static readonly ParagraphChunking Small = new(0, 240);

    private static readonly MarkSet Bold = new() { Style = TextStyle.Bold };

    [AvaloniaFact]
    public void PlainTextMatchesAcrossEverySeam() =>
        AssertEquivalent(Paragraph(Growth.Words(3_000)));

    [AvaloniaFact]
    public void PlainProseMatchesAcrossEverySeam() =>
        AssertEquivalent(Paragraph(Words(3_000)));

    [AvaloniaFact]
    public void FormattedTextMatchesAcrossEverySeam() =>
        AssertEquivalent(Paragraph(EveryOtherWord(Words(3_000), Bold)));

    [AvaloniaFact]
    public void MixedSizesAndStylesMatchAcrossEverySeam()
    {
        var text = Words(3_000);
        var spans = new List<ValueSpan<MarkSet>>();
        var styles = new[]
        {
            new MarkSet { FontSize = 22 },
            new MarkSet { Style = TextStyle.Italic | TextStyle.Underline },
            new MarkSet { FontSize = 9, Style = TextStyle.Bold },
            new MarkSet { Foreground = new Rgba(200, 0, 0, 255) },
        };
        var i = 0;

        foreach (var (start, length) in WordRanges(text))
        {
            if (i % 3 == 0)
            {
                spans.Add(new ValueSpan<MarkSet>(start, length, styles[(i / 3) % styles.Length]));
            }

            i++;
        }

        AssertEquivalent(Paragraph(InlineContent.Create(text, spans, [])));
    }

    [AvaloniaFact]
    public void SurrogatePairsAndCombiningMarksMatchAcrossEverySeam()
    {
        var text = new StringBuilder();
        var word = 0;

        while (text.Length < 3_000)
        {
            text.Append((word % 4) switch
            {
                0 => "smile\U0001F600",
                1 => "cafe\u0301",
                2 => "\U0001F44D\U0001F3FDok",
                _ => "plain",
            }).Append(word % 7 == 0 ? "\U0001F600 " : " ");
            word++;
        }

        AssertEquivalent(Paragraph(InlineContent.FromText(text.ToString())));
    }

    [AvaloniaFact]
    public void ImagesMatchAcrossEverySeam()
    {
        var content = InlineContent.Empty;
        var words = Words(2_600).Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            content = content.Concat(InlineContent.FromText(words[i] + " "));

            if (i % 9 == 4)
            {
                // Some shorter than a line and some taller, so both line-height paths are seen.
                var height = i % 18 == 4 ? 40 : 10;

                content = content
                    .Concat(InlineContent.FromEmbed(new ImageEmbed($"img{i}.png", 24, height)))
                    .Concat(InlineContent.FromText(" "));
            }
        }

        AssertEquivalent(Paragraph(content));
    }

    [AvaloniaFact]
    public void AWordLongerThanAChunkMatches()
    {
        var text = Words(900) + new string('w', 700) + " " + Words(1_400);

        AssertEquivalent(Paragraph(InlineContent.FromText(text)));
    }

    [AvaloniaTheory]
    [InlineData(TextAlign.Center)]
    [InlineData(TextAlign.Right)]
    public void AlignedTextMatchesAcrossEverySeam(TextAlign align) =>
        AssertEquivalent(Paragraph(EveryOtherWord(Words(3_000), Bold)).WithAlign(align));

    [AvaloniaFact]
    public void JustifiedTextIsNotChunked()
    {
        // Avalonia measures a justified layout's width before stretching its lines, which a chunk
        // cannot reproduce, so a justified paragraph stays in one piece. See ParagraphChunks.
        AssertEquivalent(Paragraph(Words(3_000)).WithAlign(TextAlign.Justify), expectChunks: false);
    }

    [AvaloniaFact]
    public void RightToLeftTextIsNotChunked()
    {
        // Numbers and punctuation between Hebrew words: a line starting with either takes its
        // direction from the Hebrew on the line before, which a chunk starting there would not
        // see. Measured with the check removed: the caret at such a seam moved from x=268 to x=0.
        var random = new Random(7);
        var text = new StringBuilder();

        while (text.Length < 3_000)
        {
            text.Append(random.Next(4) switch
            {
                0 => "\u05e9\u05dc\u05d5\u05dd",
                1 => "12",
                2 => "--",
                _ => "\u05d0\u05d1\u05d2\u05d3\u05d4",
            }).Append(' ');
        }

        AssertEquivalent(Paragraph(InlineContent.FromText(text.ToString())), expectChunks: false);
    }

    [AvaloniaTheory]
    [InlineData("\u0661\u0662")]
    [InlineData("\u202B")]
    [InlineData("\u2067")]
    [InlineData("\U00010900")]
    public void OneRightToLeftOrEmbeddingCharacterAnywhereStopsChunking(string character)
    {
        var view = Measured(Paragraph(InlineContent.FromText(Words(2_000) + character + Words(1_000))), Small);

        Assert.Null(view.Chunks);
    }

    [AvaloniaFact]
    public void LeftToRightTextOutsideLatinIsStillChunked()
    {
        var view = Measured(
            Paragraph(InlineContent.FromText(Words(1_500) + " \u4e2d\u6587 \u0939\u093f\u0928\u094d\u0926\u0940 \u0391\u03b2 " + Words(1_500))),
            Small);

        Assert.True(view.Chunks is { Count: > 5 });
    }

    [AvaloniaFact]
    public void AShortParagraphKeepsOneLayout()
    {
        var view = Measured(Paragraph(Words(3_000)), new ParagraphChunking(3_000, 240));

        Assert.Null(view.Chunks);
        Assert.NotNull(view.Layout);
    }

    [AvaloniaFact]
    public void AnIndentedListItemMatches()
    {
        var plain = Measured(Paragraph(Words(3_000)).WithIndentLevel(2), ParagraphChunking.Never, lead: true);
        var chunked = Measured(Paragraph(Words(3_000)).WithIndentLevel(2), Small, lead: true);

        AssertSame(plain, chunked, expectChunks: true);
    }

    [AvaloniaFact]
    public void AWidthChangeLaysOutAgainAndStillMatches()
    {
        var paragraph = Paragraph(EveryOtherWord(Words(3_000), Bold));
        var chunked = Measured(paragraph, Small);

        chunked.Measure(220);

        var plain = new ParagraphView(paragraph, Styles(), chunking: ParagraphChunking.Never);

        plain.Measure(220);

        AssertSame(plain, chunked, expectChunks: true);
    }

    [AvaloniaFact]
    public void ACompositionAtEverySeamMatches()
    {
        var paragraph = Paragraph(EveryOtherWord(Words(2_000), Bold));
        var probe = Measured(paragraph, Small);
        var seams = probe.Chunks!.Starts.Skip(1).ToArray();

        Assert.True(seams.Length > 5);

        foreach (var seam in seams)
        {
            foreach (var at in new[] { seam - 1, seam, seam + 1 })
            {
                foreach (var preedit in new[]
                {
                    new Preedit("\u65e5\u672c", at),
                    new Preedit(string.Concat(Enumerable.Repeat("composing ", 12)), at, 37),
                })
                {
                    var plain = Measured(paragraph, ParagraphChunking.Never);
                    var chunked = Measured(paragraph, Small);

                    plain.SetPreedit(preedit);
                    chunked.SetPreedit(preedit);
                    plain.Measure(Width);
                    chunked.Measure(Width);

                    AssertSame(plain, chunked, expectChunks: true, everyPosition: false);
                    Assert.Equal(plain.GetPreeditCaretRect(), chunked.GetPreeditCaretRect());

                    plain.SetPreedit(null);
                    chunked.SetPreedit(null);
                    plain.Measure(Width);
                    chunked.Measure(Width);

                    AssertSame(plain, chunked, expectChunks: true, everyPosition: false);
                }
            }
        }
    }

    [AvaloniaFact]
    public void TypingThenComposingFurtherOnBeforeOneMeasureMatches()
    {
        var paragraph = Paragraph(EveryOtherWord(Words(2_400), Bold));
        var chunked = Measured(paragraph, Small);
        var seams = chunked.Chunks!.Starts.Skip(4).Take(8).ToArray();

        foreach (var seam in seams)
        {
            // A keystroke committed near the start and a composition begun further on, laid out
            // together: the change runs from the keystroke to the end of the composition.
            var typed = new ParagraphNode(chunked.Paragraph.Content.Insert(30, InlineContent.FromText("ab")));
            var preedit = new Preedit("\u65e5\u672c\u8a9e", seam + 2);

            chunked.Update(typed);
            chunked.SetPreedit(preedit);
            chunked.Measure(Width);

            var plain = Measured(typed, ParagraphChunking.Never);

            plain.SetPreedit(preedit);
            plain.Measure(Width);

            AssertSame(plain, chunked, expectChunks: true, everyPosition: false);
            Assert.Equal(plain.GetPreeditCaretRect(), chunked.GetPreeditCaretRect());

            chunked.SetPreedit(null);
            chunked.Measure(Width);
        }
    }

    [AvaloniaFact]
    public void TheChangeBetweenTwoLayoutsLeavesOnlyIdenticalTextOutsideIt()
    {
        // The contract chunk reuse rests on: everything before Start, and everything from OldEnd
        // in the old layout's text against everything from NewEnd in the new one, is the same
        // text with the same formatting, composition included.
        var random = new Random(99);
        var styles = new TextStyleResolver(new Typeface("Segoe UI"), 14, Brushes.Black);
        var checkedCases = 0;

        for (var i = 0; i < 400; i++)
        {
            var before = EveryOtherWord(Words(200 + random.Next(200)), Bold);
            var at = random.Next(1, before.Length);
            var after = (i % 5) switch
            {
                0 => before.Insert(random.Next(before.Length), InlineContent.FromText("ab")),

                // A letter doubled: the text before and after the change overlap by one, and the
                // change must still not run backwards.
                4 => before.Insert(at, InlineContent.FromText(before.Text[(at - 1)..at])),
                1 => before.Remove(random.Next(before.Length - 10), random.Next(1, 10)),
                2 => before.ApplyMarks(random.Next(100), random.Next(1, 90), new MarkSet { FontSize = 20 }, MarkFields.FontSize),
                _ => before,
            };
            Preedit? Composition(InlineContent content) =>
                random.Next(3) == 0 ? null : new Preedit("\u65e5\u672c\u8a9e", random.Next(content.Length + 1));

            var was = Composition(before);
            var now = Composition(after);
            var a = new ParagraphChunks.Key(before, was, 1, TextAlignment.Left, 0, styles);
            var b = new ParagraphChunks.Key(after, now, 1, TextAlignment.Left, 0, styles);

            if (ParagraphChunks.Change.Between(a, b) is not { } change)
            {
                Assert.True(ReferenceEquals(before, after) && was == now);
                continue;
            }

            var (oldText, oldMarks) = LaidOut(before, was);
            var (newText, newMarks) = LaidOut(after, now);

            Assert.InRange(change.Start, 0, Math.Min(change.OldEnd, change.NewEnd));
            Assert.Equal(oldText[..change.Start], newText[..change.Start]);
            Assert.Equal(oldText[change.OldEnd..], newText[change.NewEnd..]);
            Assert.Equal(oldMarks[..change.Start], newMarks[..change.Start]);
            Assert.Equal(oldMarks[change.OldEnd..], newMarks[change.NewEnd..]);
            checkedCases++;
        }

        Assert.True(checkedCases > 300);

        // The text a layout is given, and the formatting of each position in it.
        static (string Text, MarkSet[] Marks) LaidOut(InlineContent content, Preedit? preedit)
        {
            var text = preedit is null ? content.Text : content.Text.Insert(preedit.Offset, preedit.Text);
            var marks = Enumerable.Range(0, content.Length).Select(content.MarkAt).ToList();

            if (preedit is not null)
            {
                marks.InsertRange(preedit.Offset, Enumerable.Repeat(MarkSet.Empty, preedit.Length));
            }

            return (text, [.. marks]);
        }
    }

    [AvaloniaFact]
    public void EditsAtEverySeamMatchAFreshLayout()
    {
        var paragraph = Paragraph(EveryOtherWord(Words(2_400), Bold));
        var chunked = Measured(paragraph, Small);
        var edits = 0;

        // One view taken through every edit, so each relayout starts from chunks an earlier one
        // reused, and each is checked against a fresh unchunked layout of the same text.
        foreach (var round in Enumerable.Range(0, 7))
        {
            var seams = chunked.Chunks!.Starts.Skip(1).Where((_, i) => i % 2 == round % 2).ToArray();

            foreach (var seam in seams.Take(6))
            {
                var content = chunked.Paragraph.Content;

                if (seam + 12 >= content.Length || seam < 12)
                {
                    continue;
                }

                var next = round switch
                {
                    // Typing at the start of a chunk's first line.
                    0 => content.Insert(seam, InlineContent.FromText("x")),

                    // A space at the seam moves the break after the line before it.
                    1 => content.Insert(seam, InlineContent.FromText(" ")),

                    // Deleting the first word of a chunk lets the line before take the next one.
                    2 => content.Remove(seam, Math.Max(1, content.Text.IndexOf(' ', seam) - seam)),

                    // Backspace over the space that ended the previous chunk.
                    3 => content.Remove(seam - 1, 1),

                    // A long word typed just before the seam.
                    4 => content.Insert(seam - 2, InlineContent.FromText("extraordinarily")),

                    // A deletion spanning the seam.
                    5 => content.Remove(seam - 10, 20),

                    // Formatting across the seam, with no change to the text.
                    _ => content.ApplyMarks(seam - 8, 16, new MarkSet { FontSize = 20 }, MarkFields.FontSize),
                };

                chunked.Update(new ParagraphNode(next));
                chunked.Measure(Width);

                var plain = Measured(new ParagraphNode(next), ParagraphChunking.Never);

                AssertSame(plain, chunked, expectChunks: true, everyPosition: false);
                edits++;
            }
        }

        Assert.True(edits > 20, $"only {edits} edits were tried");
    }

    [AvaloniaFact]
    public void FormattingChangedAcrossManySeamsMatchesAFreshLayout()
    {
        var text = Words(2_400);
        var chunked = Measured(Paragraph(text), Small);
        var bold = new MarkSet { Style = TextStyle.Bold };
        var steps = new Func<InlineContent, InlineContent>[]
        {
            // A span that did not exist, across a dozen chunks.
            content => content.ApplyMarks(100, 1_900, bold, MarkFields.Bold),

            // The same span gone again: the new text has no span to say where the change starts.
            content => content.ApplyMarks(100, 1_900, MarkSet.Empty, MarkFields.Bold),

            // A span whose edges stay put while its formatting changes.
            content => content.ApplyMarks(300, 1_500, new MarkSet { FontSize = 18 }, MarkFields.FontSize),
            content => content.ApplyMarks(300, 1_500, new MarkSet { FontSize = 11 }, MarkFields.FontSize),
            content => content.ApplyMarks(0, content.Length, MarkSet.Empty, MarkFields.FontSize),

            // Colour moves no line, so the lines meet the old ones at once and only the pixels say
            // whether the chunks kept were the right ones.
            content => content.ApplyMarks(150, 1_800, new MarkSet { Foreground = new Rgba(200, 0, 0, 255) }, MarkFields.Foreground),
            content => content.ApplyMarks(150, 1_800, MarkSet.Empty, MarkFields.Foreground),
        };

        foreach (var step in steps)
        {
            var next = step(chunked.Paragraph.Content);

            chunked.Update(new ParagraphNode(next));
            chunked.Measure(Width);

            AssertSame(Measured(new ParagraphNode(next), ParagraphChunking.Never), chunked, expectChunks: true, everyPosition: false);
        }
    }

    [AvaloniaFact]
    public void SeveralChangesBeforeOneMeasureMatchAFreshLayout()
    {
        var content = InlineContent.FromText(Words(1_200));

        content = content
            .Concat(InlineContent.FromEmbed(new ImageEmbed("late.png", 20, 10)))
            .Concat(InlineContent.FromText(" " + Words(1_200)));

        var chunked = Measured(Paragraph(content), Small);
        var image = content.NextPlaceholderFrom(0);

        // Typed near the start, a stretch recoloured, then the image further on resized, all
        // before the next layout: one change to lay out again from, whose tail must start after
        // the last of them — which here is only an embed, behind text that has not changed.
        var typed = content.Insert(40, InlineContent.FromText("typed "));
        var coloured = typed.ApplyMarks(
            400, 300, new MarkSet { Foreground = new Rgba(0, 0, 200, 255) }, MarkFields.Foreground);
        var resized = coloured.Replace(image + 6, 1, InlineContent.FromEmbed(new ImageEmbed("late.png", 90, 18)));

        chunked.Update(new ParagraphNode(typed));
        chunked.Update(new ParagraphNode(coloured));
        chunked.Update(new ParagraphNode(resized));
        chunked.Measure(Width);

        AssertSame(Measured(new ParagraphNode(resized), ParagraphChunking.Never), chunked, expectChunks: true, everyPosition: false);
    }

    [AvaloniaFact]
    public void ResizingAnImageNearASeamMatchesAFreshLayout()
    {
        var content = InlineContent.Empty;
        var words = Words(2_400).Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            content = content
                .Concat(InlineContent.FromText(words[i] + " "))
                .Concat(i % 6 == 3 ? InlineContent.FromEmbed(new ImageEmbed($"img{i}.png", 20, 10)) : InlineContent.Empty)
                .Concat(i % 6 == 3 ? InlineContent.FromText(" ") : InlineContent.Empty);
        }

        var chunked = Measured(Paragraph(content), Small);
        var resized = 0;

        foreach (var seam in chunked.Chunks!.Starts.Skip(1).ToArray())
        {
            var current = chunked.Paragraph.Content;
            var at = current.NextPlaceholderFrom(seam);

            if (at < 0)
            {
                break;
            }

            // The text is identical; only the embed behind one placeholder changes, which grows
            // its line and moves every line below it.
            Assert.True(current.TryGetEmbedAt(at, out var embed));

            var next = current.Replace(
                at, 1, InlineContent.FromEmbed(new ImageEmbed(((ImageEmbed)embed).Source, 60, 45)));

            chunked.Update(new ParagraphNode(next));
            chunked.Measure(Width);

            AssertSame(Measured(new ParagraphNode(next), ParagraphChunking.Never), chunked, expectChunks: true, everyPosition: false);
            resized++;
        }

        Assert.True(resized > 5, $"only {resized} images were resized");
    }

    [AvaloniaFact]
    public void AnEditLaysOutAgainOnlyNearIt()
    {
        const int window = 400;
        var view = Measured(Paragraph(EveryOtherWord(Words(20_000), Bold)), new ParagraphChunking(0, window));
        var laidOut = new List<long>();

        for (var i = 0; i < 40; i++)
        {
            var content = view.Paragraph.Content;
            var seam = view.Chunks!.Starts.ElementAt(3 + ((i * 7) % (view.Chunks.Count - 6)));
            var at = seam + (i % 4 switch { 0 => 0, 1 => 1, 2 => 30, _ => 140 });

            WorkCounters.CharactersLaidOut = 0;

            view.Update(new ParagraphNode((i % 5) switch
            {
                0 => content.Insert(at, InlineContent.FromText("x")),
                1 => content.Insert(at, InlineContent.FromText("extraordinarily ")),
                2 => content.Remove(at, 1),
                3 => content.Remove(at, 7),
                _ => content.Insert(at, InlineContent.FromText(" ")),
            }));
            view.Measure(Width);

            laidOut.Add(WorkCounters.CharactersLaidOut);
        }

        // Usually the chunk the edit is in and the one before or after it. An edit can shift the
        // lines after it for as long as greedy wrapping takes to settle, and those are laid out
        // again because they really did change, so the bound is on the whole run of edits rather
        // than on each. Measured: 43,600 characters for these forty; with the stop at the next
        // unchanged chunk taken away, three of them ran to the end of the paragraph and the total
        // was 120,000.
        Assert.True(
            laidOut.All(count => count > 0) && laidOut.Sum() <= laidOut.Count * 4 * window,
            string.Join(", ", laidOut));

        var plain = Measured(new ParagraphNode(view.Paragraph.Content), ParagraphChunking.Never);

        AssertSame(plain, view, expectChunks: true, everyPosition: false);
    }

    [AvaloniaFact]
    public void TheDefaultChunksALongParagraphAndNotAShortOne()
    {
        var shortView = Measured(Paragraph(Words(ParagraphChunking.Default.Threshold)), ParagraphChunking.Default);
        var longView = Measured(Paragraph(Words(ParagraphChunking.Default.Threshold + 1)), ParagraphChunking.Default);

        Assert.Null(shortView.Chunks);
        Assert.NotNull(longView.Chunks);
    }

    [AvaloniaFact]
    public void ResizingAnImageThatStartsAChunkMatchesAFreshLayout()
    {
        // The change is the one position the placeholder holds, and the chunk starting there is the
        // first one at or past the change's start. It looked at the image, so it is laid out again
        // rather than kept.
        var content = InlineContent.Empty;
        var words = Words(2_400).Split(' ');

        for (var i = 0; i < words.Length; i++)
        {
            content = content
                .Concat(InlineContent.FromText(words[i] + " "))
                .Concat(InlineContent.FromEmbed(new ImageEmbed($"img{i}.png", 20, 10)))
                .Concat(InlineContent.FromText(" "));
        }

        var chunked = Measured(Paragraph(content), Small);
        var resized = new HashSet<int>();

        for (var round = 0; round < 6; round++)
        {
            var current = chunked.Paragraph.Content;
            var seam = chunked.Chunks!.Starts.Skip(1)
                .FirstOrDefault(start => !resized.Contains(start) && current.TryGetEmbedAt(start, out _), -1);

            if (seam < 0)
            {
                break;
            }

            Assert.True(current.TryGetEmbedAt(seam, out var embed));

            var next = current.Replace(
                seam, 1, InlineContent.FromEmbed(new ImageEmbed(((ImageEmbed)embed).Source, 60, 45)));

            chunked.Update(new ParagraphNode(next));
            chunked.Measure(Width);

            AssertSame(Measured(new ParagraphNode(next), ParagraphChunking.Never), chunked, expectChunks: true, everyPosition: false);
            resized.Add(seam);
        }

        // How many chunk starts land exactly on an image depends on the platform's font metrics:
        // three on Windows, one on Linux CI. One is all the regression needs - but none would
        // prove nothing, so that still fails.
        Assert.True(resized.Count > 0, "no chunk started at an image");
    }

    [AvaloniaFact]
    public void ARightToLeftCompositionStopsChunking()
    {
        // Composed text is laid out with the paragraph, so a right-to-left composition makes a
        // seam inexact as surely as right-to-left text does.
        var paragraph = Paragraph(Words(3_000));
        var preedit = new Preedit("\u05e9\u05dc\u05d5\u05dd 12", 1_500);
        var plain = Measured(paragraph, ParagraphChunking.Never);
        var chunked = Measured(paragraph, Small);

        Assert.NotNull(chunked.Chunks);

        plain.SetPreedit(preedit);
        chunked.SetPreedit(preedit);
        plain.Measure(Width);
        chunked.Measure(Width);

        AssertSame(plain, chunked, expectChunks: false, everyPosition: false);
    }

    private static TextStyleResolver Styles() => new(new Typeface("Segoe UI"), 14, Brushes.Black);

    private static ParagraphNode Paragraph(string text) => ParagraphNode.FromText(text);

    private static ParagraphNode Paragraph(InlineContent content) => new(content);

    private static ParagraphView Measured(ParagraphNode paragraph, ParagraphChunking chunking, bool lead = false)
    {
        var view = new ParagraphView(paragraph, Styles(), chunking: chunking);

        if (lead)
        {
            view.SetLead(2, new ListMarker(ListKind.Ordered, 12));
        }

        view.Measure(Width);

        return view;
    }

    private static void AssertEquivalent(ParagraphNode paragraph, bool expectChunks = true) =>
        AssertSame(
            Measured(paragraph, ParagraphChunking.Never),
            Measured(paragraph, Small),
            expectChunks);

    /// <summary>Asserts that two views of the same paragraph answer everything alike.</summary>
    private static void AssertSame(ParagraphView plain, ParagraphView chunked, bool expectChunks, bool everyPosition = true)
    {
        Assert.Null(plain.Chunks);

        if (expectChunks)
        {
            Assert.True(
                chunked.Chunks is { Count: > 5 },
                $"the paragraph was laid out in {chunked.Chunks?.Count ?? 1} pieces");
        }
        else
        {
            Assert.Null(chunked.Chunks);
        }

        var size = plain.ContentSize;

        Assert.Equal(size, chunked.ContentSize);
        Assert.Equal(plain.Size, chunked.Size);
        Assert.Equal(plain.LineCount, chunked.LineCount);

        for (var line = 0; line < plain.LineCount; line++)
        {
            Assert.Equal(plain.LineStart(line), chunked.LineStart(line));
            Assert.Equal(plain.LineEnd(line), chunked.LineEnd(line));

            foreach (var x in new[] { -10, 0, 3.5, 40, 151.25, 260, Width + 30 })
            {
                Assert.Equal(plain.PositionAtLineX(line, x), chunked.PositionAtLineX(line, x));
            }
        }

        var positions = everyPosition
            ? Enumerable.Range(0, size + 1)
            : Near(chunked).Concat(Enumerable.Range(0, size + 1).Where(p => p % 37 == 0)).Distinct();

        foreach (var p in positions)
        {
            Assert.True(Same(plain.GetCaretRect(p), chunked.GetCaretRect(p)), $"caret at {p}: {plain.GetCaretRect(p)} vs {chunked.GetCaretRect(p)}");
            Assert.True(plain.NextCaretPosition(p) == chunked.NextCaretPosition(p), $"next from {p}");
            Assert.True(plain.PreviousCaretPosition(p) == chunked.PreviousCaretPosition(p), $"previous from {p}");
            Assert.True(plain.BackspacePosition(p) == chunked.BackspacePosition(p), $"backspace from {p}");
            Assert.True(plain.LineIndexAt(p, true) == chunked.LineIndexAt(p, true), $"line forward at {p}");
            Assert.True(plain.LineIndexAt(p, false) == chunked.LineIndexAt(p, false), $"line backward at {p}");
            Assert.True(Same(plain.GetImageRect(p), chunked.GetImageRect(p)), $"image at {p}: {plain.GetImageRect(p)} vs {chunked.GetImageRect(p)}");
        }

        var height = plain.Size.Height;

        foreach (var y in HitRows(plain, height))
        {
            for (var x = -8.0; x < Width + 24; x += 6.5)
            {
                var point = new Point(x, y);

                Assert.True(plain.HitTest(point) == chunked.HitTest(point), $"hit at {point}");
            }
        }

        foreach (var (from, to) in Ranges(chunked, size))
        {
            var expected = plain.GetSelectionRects(from, to);
            var actual = chunked.GetSelectionRects(from, to);

            Assert.True(expected.Count == actual.Count && expected.Zip(actual).All(r => Same(r.First, r.Second)), $"selection {from}..{to}: {expected.Count} vs {actual.Count} rectangles");
        }

        Assert.Equal(Pixels(plain), Pixels(chunked));
    }

    /// <summary>
    /// Whether two rectangles are the same to well below anything that can be drawn.
    /// </summary>
    /// <remarks>
    /// A chunk adds its own offset to x and y, so the same edge can come out of a different sum:
    /// on macOS a caret was 14.000000000000004 one way and 14.000000000000002 the other. A
    /// billionth of a DIP is not a layout difference; anything that is one still fails here, and
    /// the pixels are compared exactly.
    /// </remarks>
    private static bool Same(Rect? a, Rect? b) =>
        a is not { } x || b is not { } y
            ? a is null && b is null
            : Math.Abs(x.X - y.X) < 1e-9
                && Math.Abs(x.Y - y.Y) < 1e-9
                && Math.Abs(x.Width - y.Width) < 1e-9
                && Math.Abs(x.Height - y.Height) < 1e-9;

    /// <summary>Positions on and around every seam.</summary>
    private static IEnumerable<int> Near(ParagraphView view)
    {
        if (view.Chunks is not { } chunks)
        {
            return [];
        }

        return chunks.Starts
            .SelectMany(seam => Enumerable.Range(seam - 3, 7))
            .Where(p => p >= 0 && p <= view.ContentSize);
    }

    private static IEnumerable<double> HitRows(ParagraphView view, double height)
    {
        yield return -5;
        yield return height + 5;

        for (var line = 0; line < view.LineCount; line++)
        {
            var caret = view.GetCaretRect(view.LineStart(line));

            yield return caret.Y;
            yield return caret.Y + (caret.Height / 2);
            yield return caret.Bottom - 0.01;
        }
    }

    private static IEnumerable<(int From, int To)> Ranges(ParagraphView chunked, int size)
    {
        yield return (0, size);

        foreach (var seam in Near(chunked).Where(p => p > 0))
        {
            foreach (var reach in new[] { 1, 5, 60 })
            {
                yield return (Math.Max(0, seam - reach), Math.Min(size, seam + reach));
                yield return (seam, Math.Min(size, seam + reach));
                yield return (Math.Max(0, seam - reach), seam);
            }
        }

        var random = new Random(1234);

        for (var i = 0; i < 40; i++)
        {
            var a = random.Next(size + 1);
            var b = random.Next(size + 1);

            yield return (a, b);
        }
    }

    private static uint[] Pixels(ParagraphView view)
    {
        var size = new PixelSize((int)Math.Ceiling(view.Size.Width) + 40, (int)Math.Ceiling(view.Size.Height) + 2);

        using var bitmap = new RenderTargetBitmap(size);

        using (var context = bitmap.CreateDrawingContext())
        {
            view.Render(context, new Point(30, 0));
        }

        var pixels = RenderedPixels.Argb(bitmap);

        Assert.Contains(pixels, p => p != 0);

        return pixels;
    }

    /// <summary>Words of irregular length, so that seams land on every kind of word.</summary>
    private static string Words(int length) => Growth.Prose(length);

    private static InlineContent EveryOtherWord(string text, MarkSet mark)
    {
        var spans = new List<ValueSpan<MarkSet>>();
        var i = 0;

        foreach (var (start, length) in WordRanges(text))
        {
            if (i++ % 2 == 1)
            {
                spans.Add(new ValueSpan<MarkSet>(start, length, mark));
            }
        }

        return InlineContent.Create(text, spans, []);
    }

    private static IEnumerable<(int Start, int Length)> WordRanges(string text)
    {
        var start = 0;

        for (var i = 0; i <= text.Length; i++)
        {
            if (i == text.Length || text[i] == ' ')
            {
                if (i > start)
                {
                    yield return (start, i - start);
                }

                start = i + 1;
            }
        }
    }
}
