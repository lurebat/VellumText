using System.Collections.Immutable;
using FsCheck.Xunit;

namespace VellumText.Tests;

/// <summary>
/// <see cref="ReplaceTextStep"/> and <see cref="Transaction.ReplaceText"/>: many replacements of
/// paragraph text in one rebuild, which must be the per-replacement steps they replace in every
/// respect except cost.
/// </summary>
/// <remarks>
/// <para>
/// The specification is the old path — one <see cref="ReplaceStep"/> per replacement, back to
/// front, in one transaction — and it is written out below as <see cref="PerReplacement"/> rather
/// than reached through anything the new code shares. Compared over generated documents: the
/// document produced, whether anything was refused, where every position maps with either
/// association and whether it is reported deleted, the selection afterwards, and what undo and
/// redo restore, selection included.
/// </para>
/// <para>
/// Two generators. One replaces what a search finds, with the formatting of each match's first
/// character, which is Replace All exactly; its queries are short strings over a two-letter
/// alphabet, so matches abut, sit at both edges of paragraphs and cover whole ones. The other
/// replaces arbitrary ranges, so it reaches what a search cannot: empty ranges, ranges that touch,
/// formatted and embedded content, ranges cutting a surrogate pair.
/// </para>
/// </remarks>
public class ReplaceTextStepTests
{
    private static readonly MarkSet Bold = new() { Style = TextStyle.Bold };

    private static readonly MarkSet Linked = new() { Link = new LinkMark("https://example.com/") };

    // ---------------------------------------------------------------- the step on its own

    [Fact]
    public void ItReplacesEachRangeAndLeavesTheRest()
    {
        var doc = DocumentNode.FromParagraphs("one cat two cat", "three", "cat");
        var step = Step((5, 8, "dog"), (13, 16, "x"), (25, 28, "cow"));

        var result = step.Apply(doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.Equal(DocumentNode.FromParagraphs("one dog two x", "three", "cow"), result.Doc);
        Assert.Same(doc.Blocks[1], result.Doc!.Blocks[1]);
    }

    [Fact]
    public void ItKeepsTheParagraphsKindAlignmentAndIndent()
    {
        var heading = new ParagraphNode(InlineContent.FromText("a cat"), ParagraphKind.Heading2, TextAlign.Center, 2);
        var doc = new DocumentNode([heading]);

        var result = Step((3, 6, "dog")).Apply(doc);

        Assert.Equal(heading.WithContent(InlineContent.FromText("a dog")), result.Doc!.Blocks[0]);
    }

    [Fact]
    public void ItReachesParagraphsInsideListsAndTables()
    {
        var doc = new DocumentNode(
        [
            new ListNode([ListItemNode.FromText("cat"), new ListItemNode([ParagraphNode.FromText("x"), new ListNode([ListItemNode.FromText("cat")])])]),
            new TableNode([new TableRowNode([TableCellNode.FromText("cat"), TableCellNode.FromText("cat")])]),
        ]);
        var matches = DocumentSearch.Find(doc, "cat");
        var step = new ReplaceTextStep([.. matches.Select(m => new TextReplacement(m.From, m.To, InlineContent.FromText("dog")))]);

        var result = step.Apply(doc);

        Assert.True(result.IsOk, result.Failure);
        Assert.Equal(4, matches.Length);
        Assert.Empty(DocumentSearch.Find(result.Doc!, "cat"));
        Assert.Equal(4, DocumentSearch.Find(result.Doc!, "dog").Length);
    }

    [Fact]
    public void NoReplacementsIsTheSameDocument()
    {
        var doc = DocumentNode.FromParagraphs("hello");

        Assert.Same(doc, new ReplaceTextStep([]).Apply(doc).Doc);
        Assert.False(EditorState.Create(doc).Transaction().ReplaceText([]).ChangedDoc);
    }

    public static TheoryData<string, ReplaceTextStep, string> Refusals() => new()
    {
        { "no list", new ReplaceTextStep(default), "no list of replacements" },
        { "no content", new ReplaceTextStep([new TextReplacement(1, 2, null!)]), "has no content" },
        { "negative start", Step((-1, 2, "x")), "is not inside one paragraph's text" },
        { "inverted", Step((3, 2, "x")), "is not inside one paragraph's text" },
        { "past the end", Step((1, 99, "x")), "is not inside one paragraph's text" },
        { "across two paragraphs", Step((2, 9, "x")), "is not inside one paragraph's text" },
        { "between blocks", Step((7, 8, "x")), "is not in a paragraph's text" },
        { "out of order", Step((9, 10, "x"), (1, 2, "y")), "out of order with, overlaps or touches" },
        { "overlapping", Step((1, 4, "x"), (3, 5, "y")), "out of order with, overlaps or touches" },
        { "touching", Step((1, 3, "x"), (3, 5, "y")), "out of order with, overlaps or touches" },
        { "the same insertion twice", Step((2, 2, "x"), (2, 2, "y")), "out of order with, overlaps or touches" },
        { "a later one past its paragraph", Step((1, 2, "x"), (3, 9, "y")), "is not inside one paragraph's text" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void ItRefusesWhatItCannotDoAsAFailureNotAnException(string why, ReplaceTextStep step, string reason)
    {
        var doc = DocumentNode.FromParagraphs("hello", "world");

        var result = step.Apply(doc);

        Assert.False(result.IsOk, why);
        Assert.Contains(reason, result.Failure);
    }

    [Fact]
    public void ARangeEdgeInsideASurrogatePairIsRefused()
    {
        var doc = DocumentNode.FromParagraphs("a\U0001F600b");

        var result = Step((2, 3, "x")).Apply(doc);

        Assert.False(result.IsOk);
        Assert.Contains("surrogate", result.Failure);
    }

    /// <remarks>
    /// Cutting from the middle of one pair to the middle of the next and putting nothing there
    /// leaves the first pair's high half against the second's low half — well-formed text, and a
    /// character nobody typed. Only the range check sees it.
    /// </remarks>
    [Fact]
    public void ARangeFromInsideOnePairToInsideAnotherIsRefusedThoughWhatIsLeftIsWellFormed()
    {
        var doc = DocumentNode.FromParagraphs("a\U0001F600b\U0001F601c");

        var result = Step((3, 6, string.Empty)).Apply(doc);

        Assert.False(result.IsOk);
        Assert.Contains("splits a surrogate pair", result.Failure);
    }

    [Fact]
    public void ItsMapHasOneRangePerReplacement()
    {
        var map = Step((1, 3, "xyz"), (5, 5, "q"), (9, 12, string.Empty)).GetMap();

        Assert.Equal(3, map.RangeCount);
        Assert.Equal(new ReplacedRange(1, 2, 3), map.RangeAt(0));
        Assert.Equal(new ReplacedRange(5, 0, 1), map.RangeAt(1));
        Assert.Equal(new ReplacedRange(9, 3, 0), map.RangeAt(2));
    }

    [Fact]
    public void ItsInverseIsTheSameKindOfStepHoldingWhatWasDisplaced()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create("bold cat and cat", [new ValueSpan<MarkSet>(0, 8, Bold)], [])),
            ParagraphNode.FromText("cat"),
        ]);
        var step = Step((6, 9, "a dog"), (14, 17, string.Empty), (19, 22, "c"));

        var after = step.Apply(doc).Doc!;
        var inverse = Assert.IsType<ReplaceTextStep>(step.Invert(doc));

        Assert.Equal(
            new ReplaceTextStep(
            [
                new TextReplacement(6, 11, InlineContent.Create("cat", [new ValueSpan<MarkSet>(0, 3, Bold)], [])),
                new TextReplacement(16, 16, InlineContent.FromText("cat")),
                new TextReplacement(18, 19, InlineContent.FromText("cat")),
            ]),
            inverse);
        Assert.Equal(doc, inverse.Apply(after).Doc);
    }

    [Fact]
    public void InvertingAgainstADocumentItDoesNotApplyToThrows()
    {
        var step = Step((1, 99, "x"));

        var ex = Assert.Throws<InvalidOperationException>(() => step.Invert(DocumentNode.FromParagraphs("hello")));

        Assert.Contains("is not inside one paragraph's text", ex.Message);
    }

    [Fact]
    public void TwoStepsWithEqualReplacementsAreEqual()
    {
        var a = Step((1, 2, "x"), (4, 5, "y"));
        var b = Step((1, 2, "x"), (4, 5, "y"));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, Step((1, 2, "x"), (4, 5, "z")));
        Assert.NotEqual(a, Step((1, 2, "x")));
        Assert.NotEqual(a.GetHashCode(), Step((1, 2, "x")).GetHashCode());
    }

    [Fact]
    public void RebasingMovesEachRangeAndDropsTheOnesThatWereDeleted()
    {
        var step = Step((2, 4, "x"), (8, 10, "y"), (14, 16, "z"));

        // Two inserted before everything, and the second range deleted outright.
        var mapping = Mapping.Of(new StepMap(1, 0, 2), new StepMap(9, 4, 0));

        var rebased = Assert.IsType<ReplaceTextStep>(step.Map(mapping));

        Assert.Equal(Step((4, 6, "x"), (12, 14, "z")), rebased);
        Assert.Null(Step((8, 10, "y")).Map(Mapping.Of(new StepMap(7, 5, 0))));
    }

    [Fact]
    public void RebasingBindsEachRangesEndsOutwards()
    {
        // Content inserted exactly at both ends of a range stays outside it.
        var rebased = Assert.IsType<ReplaceTextStep>(
            Step((3, 5, "x")).Map(Mapping.Of(new StepMap(5, 0, 1), new StepMap(3, 0, 1))));

        Assert.Equal(Step((4, 6, "x")), rebased);
    }

    [Fact]
    public void RebasingKeepsARangeWithOnlyOneEndDeleted()
    {
        // [2, 4) deleted takes the range's start with it but not its end, so what is left of the
        // range is still there to replace.
        var rebased = Assert.IsType<ReplaceTextStep>(Step((3, 6, "x")).Map(Mapping.Of(new StepMap(2, 2, 0))));

        Assert.Equal(Step((2, 4, "x")), rebased);
    }

    [Fact]
    public void RebasingAnInsertionAtWhichSomethingWasInsertedKeepsItEmpty()
    {
        // The start binds after the inserted text and the end before it, so the end would come
        // out in front of the start; the range closes up at the start instead.
        var rebased = Assert.IsType<ReplaceTextStep>(Step((3, 3, "x")).Map(Mapping.Of(new StepMap(3, 0, 2))));

        Assert.Equal(Step((5, 5, "x")), rebased);
    }

    // ---------------------------------------------------------------- batching

    [Fact]
    public void ReplacementsThatDoNotTouchAreOneStep()
    {
        var batches = ReplaceTextStep.Batches(Step((1, 3, "x"), (4, 6, "y"), (9, 10, "z")).Replacements);

        Assert.Equal(Step((1, 3, "x"), (4, 6, "y"), (9, 10, "z")), Assert.Single(batches));
    }

    [Fact]
    public void ARunOfTouchingReplacementsAlternatesBetweenTwoSteps()
    {
        // Moved by what the first step replaced before it: "ab" for two characters is +0, then
        // "abc" for two is +1 more.
        var batches = ReplaceTextStep.Batches(
            Step((1, 3, "ab"), (3, 5, "cde"), (5, 7, "abc"), (7, 9, "f"), (12, 13, "g")).Replacements);

        Assert.Equal(2, batches.Count);
        Assert.Equal(Step((1, 3, "ab"), (5, 7, "abc"), (12, 13, "g")), batches[0]);
        Assert.Equal(Step((3, 5, "cde"), (8, 10, "f")), batches[1]);
    }

    [Fact]
    public void ReplacementsThatOnlyMeetOnceWhatIsBetweenThemIsDeletedAreKeptApart()
    {
        // The second and fourth do not touch, but the third — between them — is deleted by the
        // first step, after which they would.
        var batches = ReplaceTextStep.Batches(
            Step((1, 2, string.Empty), (2, 3, "x"), (3, 4, string.Empty), (4, 5, "y")).Replacements);

        Assert.Equal(3, batches.Count);
        Assert.Equal(Step((1, 2, string.Empty), (3, 4, string.Empty)), batches[0]);
        Assert.Equal(Step((1, 2, "x")), batches[1]);
        Assert.Equal(Step((2, 3, "y")), batches[2]);
    }

    /// <remarks>
    /// Where an insertion lands against a neighbour it touches depends on the order the two are
    /// applied in, so a list holding one keeps the old order: one step per replacement, last first.
    /// </remarks>
    [Fact]
    public void AListWithAnInsertionIsOneStepPerReplacementBackToFront()
    {
        var batches = ReplaceTextStep.Batches(Step((2, 2, "a"), (2, 2, "bb"), (5, 7, "c")).Replacements);

        Assert.Equal([Step((5, 7, "c")), Step((2, 2, "bb")), Step((2, 2, "a"))], batches);
    }

    [Fact]
    public void AnOverlappingListIsLeftForTheStepToRefuse()
    {
        var batches = ReplaceTextStep.Batches(Step((1, 4, "x"), (2, 3, "y")).Replacements);

        Assert.Equal(Step((1, 4, "x"), (2, 3, "y")), Assert.Single(batches));
        Assert.False(Assert.Single(batches).Apply(DocumentNode.FromParagraphs("hello")).IsOk);
    }

    // ---------------------------------------------------------------- equivalence

    [Property(MaxTest = 300)]
    public void ReplacingWhatASearchFindsIsThePerMatchSteps(int seed)
    {
        var random = new Random(seed);
        var doc = Documents.Generate(random);
        var query = Words.Next(random, 1, 3);
        var options = new SearchOptions { MatchCase = random.Next(2) == 0, WholeWord = random.Next(4) == 0 };
        var replacement = random.Next(5) == 0 ? string.Empty : Words.Next(random, 0, 5);

        var replacements = DocumentSearch.Find(doc, query, options)
            .Select(match => new TextReplacement(
                match.From, match.To, InlineContent.FromText(replacement, MarkAtMatch(doc, match))))
            .ToArray();

        AssertSameAsPerReplacement(EditorState.Create(doc, Documents.AnySelection(random, doc)), replacements);
    }

    [Property(MaxTest = 300)]
    public void ReplacingArbitraryRangesIsThePerReplacementSteps(int seed)
    {
        var random = new Random(seed);
        var doc = Documents.Generate(random);

        AssertSameAsPerReplacement(
            EditorState.Create(doc, Documents.AnySelection(random, doc)),
            Documents.Ranges(random, doc));
    }

    /// <summary>
    /// The generators are only worth what they reach. A fixed sample of seeds has to produce
    /// adjacent matches, empty replacements, touching ranges, more than one step, and refusals, or
    /// the properties above are passing over a document of nothing in particular.
    /// </summary>
    [Fact]
    public void TheGeneratedCasesReachTheShapesThatMatter()
    {
        int adjacentMatches = 0, touchingRanges = 0, severalSteps = 0, refused = 0, longParagraphs = 0, applied = 0;

        for (var seed = 0; seed < 300; seed++)
        {
            var random = new Random(seed);
            var doc = Documents.Generate(random);
            var matches = DocumentSearch.Find(doc, Words.Next(random, 1, 3));

            adjacentMatches += matches.Zip(matches.Skip(1)).Count(pair => pair.First.To == pair.Second.From);
            longParagraphs += doc.Blocks.OfType<ParagraphNode>().Count(p => p.Content.Length >= 200);

            var ranges = Documents.Ranges(new Random(seed), doc);

            touchingRanges += ranges.Zip(ranges.Skip(1)).Count(pair => pair.First.To == pair.Second.From);
            severalSteps += ReplaceTextStep.Batches(ranges).Count > 1 ? 1 : 0;

            var tr = EditorState.Create(doc).Transaction().ReplaceText(ranges);

            refused += tr.Failures.IsEmpty ? 0 : 1;
            applied += tr.Failures.IsEmpty && tr.ChangedDoc ? 1 : 0;
        }

        Assert.True(adjacentMatches >= 20, $"{adjacentMatches} adjacent matches");
        Assert.True(touchingRanges >= 20, $"{touchingRanges} touching ranges");
        Assert.True(severalSteps >= 20, $"{severalSteps} cases needing several steps");
        Assert.True(longParagraphs >= 20, $"{longParagraphs} long paragraphs");
        Assert.True(refused >= 10, $"{refused} refused");
        Assert.True(applied >= 150, $"{applied} applied");
    }

    [Property(MaxTest = 300)]
    public void ReplacingManyIsReplacingEachBackToFront(int seed)
    {
        var random = new Random(seed);
        var content = Documents.Inline(random, random.Next(0, 40));
        var edits = new List<(int Start, int Length, InlineContent Content)>();
        var cursor = 0;

        while (cursor <= content.Length && random.Next(4) != 0)
        {
            var start = cursor + random.Next(0, Math.Min(4, content.Length - cursor + 1));
            var length = random.Next(0, Math.Min(4, content.Length - start + 1));

            if (!content.IsValidBoundary(start) || !content.IsValidBoundary(start + length))
            {
                break;
            }

            edits.Add((start, length, Documents.Inline(random, random.Next(0, 4))));
            cursor = start + length;
        }

        var expected = content;

        for (var i = edits.Count - 1; i >= 0; i--)
        {
            expected = expected.Replace(edits[i].Start, edits[i].Length, edits[i].Content);
        }

        Assert.Equal(expected, content.ReplaceMany(edits));
        Assert.Equal(
            edits.Select(e => content.Substring(e.Start, e.Length)),
            content.Excerpts([.. edits.Select(e => (e.Start, e.Length))]));
    }

    // ---------------------------------------------------------------- cost

    /// <remarks>
    /// The defect this step exists for: one match in each of 8,000 paragraphs was 8,000 steps,
    /// each copying the document's block array and every map before it — 1.6 GB, and as much to
    /// undo. One step copies the block array once.
    /// </remarks>
    [Fact]
    public void ReplacingAMatchInEveryParagraphAndUndoingItIsLinear()
    {
        const int Paragraphs = 8_000;

        var doc = new DocumentNode(Enumerable.Range(0, Paragraphs)
            .Select(i => (BlockNode)ParagraphNode.FromText($"Paragraph {i} with enough words in it to wrap once."))
            .ToArray());
        var state = EditorState.Create(doc);
        var replacements = DocumentSearch.Find(doc, "enough")
            .Select(m => new TextReplacement(m.From, m.To, InlineContent.FromText("plenty")))
            .ToArray();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var tr = state.Transaction().ReplaceText(replacements);
        var after = state.Apply(tr);
        var history = History.Empty.Record(tr);
        var replaced = GC.GetAllocatedBytesForCurrentThread() - before;

        before = GC.GetAllocatedBytesForCurrentThread();
        var (undone, _) = history.Undo(after);
        var undo = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(doc, undone.Doc);
        Assert.True(replaced < 16_000_000, $"replacing allocated {replaced:N0} bytes");
        Assert.True(undo < 16_000_000, $"undoing allocated {undo:N0} bytes");
    }

    /// <remarks>
    /// The other shape of the same defect: one long paragraph with a match every sixty characters
    /// was rebuilt whole once per match, 832 MB at 100,000 characters. Rewritten once, it is linear
    /// in the paragraph, and so is the undo.
    /// </remarks>
    [Fact]
    public void ReplacingEveryMatchInALongParagraphAndUndoingItIsLinearInItsLength()
    {
        int[] lengths = [25_000, 50_000, 100_000];
        var replaced = new long[lengths.Length];
        var undone = new long[lengths.Length];

        for (var i = 0; i < lengths.Length; i++)
        {
            var text = string.Concat(Enumerable.Range(0, lengths[i] / 6 + 1).Select(w => "word" + (w % 10) + " "))[..lengths[i]];
            var doc = new DocumentNode([ParagraphNode.FromText(text)]);
            var state = EditorState.Create(doc);
            var replacements = DocumentSearch.Find(doc, "word1")
                .Select(m => new TextReplacement(m.From, m.To, InlineContent.FromText("w1")))
                .ToArray();

            var before = GC.GetAllocatedBytesForCurrentThread();
            var tr = state.Transaction().ReplaceText(replacements);
            var after = state.Apply(tr);
            var history = History.Empty.Record(tr);
            replaced[i] = GC.GetAllocatedBytesForCurrentThread() - before;

            before = GC.GetAllocatedBytesForCurrentThread();
            var (back, _) = history.Undo(after);
            undone[i] = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.Equal(doc, back.Doc);
        }

        for (var i = 1; i < lengths.Length; i++)
        {
            Assert.True(replaced[i] <= replaced[i - 1] * 5 / 2, $"replacing grew: {string.Join(", ", replaced)}");
            Assert.True(undone[i] <= undone[i - 1] * 5 / 2, $"undoing grew: {string.Join(", ", undone)}");
        }
    }

    // ---------------------------------------------------------------- the oracle

    /// <summary>The old path: one <see cref="ReplaceStep"/> per replacement, back to front.</summary>
    private static Transaction PerReplacement(EditorState state, IReadOnlyList<TextReplacement> replacements)
    {
        var tr = state.Transaction().As(TransactionKind.Structure);

        for (var i = replacements.Count - 1; i >= 0; i--)
        {
            tr.Replace(replacements[i].From, replacements[i].To, Slice.OfInline(replacements[i].Content));
        }

        return tr;
    }

    private static void AssertSameAsPerReplacement(EditorState state, IReadOnlyList<TextReplacement> replacements)
    {
        var expected = PerReplacement(state, replacements);
        var actual = state.Transaction().As(TransactionKind.Structure).ReplaceText(replacements);

        Assert.Equal(expected.Failures.IsEmpty, actual.Failures.IsEmpty);

        if (!expected.Failures.IsEmpty)
        {
            return;
        }

        Assert.Equal(expected.ChangedDoc, actual.ChangedDoc);
        Assert.Equal(expected.Doc, actual.Doc);

        for (var pos = 0; pos <= state.Doc.ContentSize; pos++)
        {
            foreach (var assoc in (Assoc[])[Assoc.Before, Assoc.After])
            {
                Assert.Equal(expected.Mapping.MapWithResult(pos, assoc), actual.Mapping.MapWithResult(pos, assoc));
            }
        }

        Assert.Equal(expected.SelectionAfter, actual.SelectionAfter);

        if (!actual.ChangedDoc)
        {
            return;
        }

        var (expectedUndone, expectedRedone) = UndoAndRedo(state, expected);
        var (actualUndone, actualRedone) = UndoAndRedo(state, actual);

        Assert.Equal(state.Doc, actualUndone.Doc);
        Assert.Equal(expectedUndone.Selection, actualUndone.Selection);
        Assert.Equal(expected.Doc, actualRedone.Doc);
        Assert.Equal(expectedRedone.Doc, actualRedone.Doc);
        Assert.Equal(expectedRedone.Selection, actualRedone.Selection);
    }

    private static (EditorState Undone, EditorState Redone) UndoAndRedo(EditorState state, Transaction tr)
    {
        var after = state.Apply(tr);
        var history = History.With(HistoryPolicy.Never).Record(tr);
        var (undone, undoneHistory) = history.Undo(after);
        var (redone, _) = undoneHistory.Redo(undone);

        return (undone, redone);
    }

    /// <summary>Replace All's rule: the formatting of the first character the match covers.</summary>
    private static MarkSet MarkAtMatch(DocumentNode doc, SearchMatch match)
    {
        var at = doc.Resolve(match.From);

        return at.Paragraph is { } paragraph && at.ParentOffset < paragraph.Content.Length
            ? paragraph.Content.MarkAt(at.ParentOffset)
            : MarkSet.Empty;
    }

    private static ReplaceTextStep Step(params (int From, int To, string Text)[] replacements) =>
        new([.. replacements.Select(r => new TextReplacement(r.From, r.To, InlineContent.FromText(r.Text)))]);

    /// <summary>Short words over two letters, so that a query matches often and matches abut.</summary>
    private static class Words
    {
        public static string Next(Random random, int min, int max)
        {
            var length = random.Next(min, max + 1);
            var chars = new char[length];

            for (var i = 0; i < length; i++)
            {
                chars[i] = random.Next(5) switch
                {
                    0 => 'A',
                    1 or 2 => 'a',
                    _ => 'b',
                };
            }

            return new string(chars);
        }
    }

    /// <summary>Generated documents, selections and replacement ranges.</summary>
    private static class Documents
    {
        private const string Letters = "aabab Ab ";

        public static DocumentNode Generate(Random random)
        {
            var blocks = new List<BlockNode>();
            var count = random.Next(1, 6);

            for (var i = 0; i < count; i++)
            {
                blocks.Add(random.Next(9) switch
                {
                    0 or 1 or 2 => Paragraph(random, random.Next(0, 16)),
                    3 => Paragraph(random, random.Next(200, 500)),
                    4 => new ListNode(
                        [ListItemNode.FromText(Text(random, 6)), new ListItemNode(
                        [
                            Paragraph(random, 5),
                            new ListNode([new ListItemNode([Paragraph(random, 8)])], ListKind.Ordered),
                        ])]),
                    5 => new TableNode(Enumerable.Range(0, random.Next(1, 3)).Select(_ =>
                        new TableRowNode([new TableCellNode([Paragraph(random, 4)]), new TableCellNode([Paragraph(random, 6), Paragraph(random, 3)])]))),
                    6 => RuleNode.Instance,
                    7 => new ParagraphNode(Inline(random, random.Next(0, 12)), ParagraphKind.Heading1, TextAlign.Right, 1),
                    _ => new ParagraphNode(Inline(random, random.Next(0, 12)), ParagraphKind.Code),
                });
            }

            return new DocumentNode(blocks);
        }

        public static ParagraphNode Paragraph(Random random, int length) => new(Inline(random, length));

        /// <summary>Text with formatting spans, now and then an image, and now and then an emoji.</summary>
        public static InlineContent Inline(Random random, int length)
        {
            var text = new System.Text.StringBuilder();
            var embeds = new List<InlineEmbed>();

            while (text.Length < length)
            {
                switch (random.Next(30))
                {
                    case 0:
                        text.Append("\U0001F600");
                        break;
                    case 1:
                        text.Append(InlineContent.Placeholder);
                        embeds.Add(new ImageEmbed($"image{embeds.Count}.png"));
                        break;
                    default:
                        text.Append(Letters[random.Next(Letters.Length)]);
                        break;
                }
            }

            var spans = new List<ValueSpan<MarkSet>>();
            var at = 0;

            while (at < text.Length)
            {
                var end = Math.Min(text.Length, at + random.Next(1, 8));

                if (end < text.Length && char.IsLowSurrogate(text[end]))
                {
                    end++;
                }

                var mark = random.Next(4) switch { 0 => Bold, 1 => Linked, _ => MarkSet.Empty };

                spans.Add(new ValueSpan<MarkSet>(at, end - at, mark));
                at = end;
            }

            return InlineContent.Create(text.ToString(), spans, embeds);
        }

        /// <summary>A text selection between two positions in paragraph text.</summary>
        public static VellumText.Selection AnySelection(Random random, DocumentNode doc)
        {
            var text = Enumerable.Range(0, doc.ContentSize + 1).Where(p => doc.Resolve(p).IsInText).ToArray();

            if (text.Length == 0)
            {
                return VellumText.Selection.AtStart(doc);
            }

            return new TextSelection(text[random.Next(text.Length)], text[random.Next(text.Length)]);
        }

        /// <summary>
        /// Ranges inside paragraphs, ascending and not overlapping, often touching and now and then
        /// empty. Some cut a surrogate pair, which both paths must refuse.
        /// </summary>
        public static TextReplacement[] Ranges(Random random, DocumentNode doc)
        {
            var ranges = new List<TextReplacement>();

            foreach (var (start, paragraph) in Paragraphs(doc, 0))
            {
                if (random.Next(3) == 0)
                {
                    continue;
                }

                var length = paragraph.Content.Length;
                var allowEmpty = random.Next(5) == 0;
                var offsets = Enumerable.Range(0, 2 * random.Next(1, 5))
                    .Select(_ => random.Next(4) switch
                    {
                        0 => 0,
                        1 => length,
                        _ => random.Next(0, length + 1),
                    })
                    .Order()
                    .ToArray();

                for (var i = 0; i < offsets.Length; i += 2)
                {
                    if (offsets[i] == offsets[i + 1] && !allowEmpty)
                    {
                        continue;
                    }

                    ranges.Add(new TextReplacement(start + offsets[i], start + offsets[i + 1], Inline(random, random.Next(0, 5))));
                }
            }

            return [.. ranges];
        }

        /// <summary>Every paragraph, with the position its content starts at.</summary>
        private static IEnumerable<(int Start, ParagraphNode Paragraph)> Paragraphs(Node node, int contentStart)
        {
            var pos = contentStart;

            foreach (var child in node.Children)
            {
                if (child is ParagraphNode paragraph)
                {
                    yield return (pos + 1, paragraph);
                }
                else
                {
                    foreach (var inner in Paragraphs(child, pos + 1))
                    {
                        yield return inner;
                    }
                }

                pos += child.NodeSize;
            }
        }

        private static string Text(Random random, int length) =>
            new([.. Enumerable.Range(0, length).Select(_ => Letters[random.Next(Letters.Length)])]);
    }
}