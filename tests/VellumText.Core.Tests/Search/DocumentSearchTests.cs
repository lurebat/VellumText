using System.Collections.Immutable;

namespace VellumText.Tests;

/// <summary>
/// Finding text in a document.
/// </summary>
/// <remarks>
/// The load-bearing claim is not "it finds the word" — it is that what it returns are real
/// document positions, so a replacement applied to them replaces exactly what was found. Most of
/// these tests check the positions by cutting the document at them rather than by asserting
/// numbers, because a number that is wrong in the same way as the code is no evidence.
/// </remarks>
public class DocumentSearchTests
{
    [Fact]
    public void EveryMatchCutsBackToTheQuery()
    {
        var doc = DocumentNode.FromParagraphs(
            "the cat sat on the mat",
            "another cat entirely");

        var matches = DocumentSearch.Find(doc, "cat");

        Assert.Equal(2, matches.Length);

        foreach (var match in matches)
        {
            Assert.Equal("cat", DocumentText.Of(Slice.Cut(doc, match.From, match.To).Content));
        }
    }

    [Fact]
    public void MatchesComeInDocumentOrder()
    {
        var doc = DocumentNode.FromParagraphs("a b a", "a");
        var matches = DocumentSearch.Find(doc, "a");

        Assert.Equal(matches.OrderBy(m => m.From), matches);
        Assert.Equal(3, matches.Length);
    }

    /// <remarks>
    /// The reason the search reads <c>Content.Text</c> rather than the plain-text projection. An
    /// embed is one position and its alt text is many, so searching the projection would put every
    /// position after an embed out by the difference — and the first thing done with a position is
    /// to delete the text at it.
    /// </remarks>
    [Fact]
    public void PositionsSurviveAnEmbedEarlierInTheParagraph()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.Create(
                $"a {InlineContent.Placeholder} needle",
                [],
                [new ImageEmbed("cat.png", altText: "a long alt text")])),
        ]);

        var match = Assert.Single(DocumentSearch.Find(doc, "needle"));

        Assert.Equal("needle", DocumentText.Of(Slice.Cut(doc, match.From, match.To).Content));
    }

    [Fact]
    public void ItFindsTextNestedInATable()
    {
        var doc = new DocumentNode([
            new ParagraphNode(InlineContent.FromText("above")),
            new TableNode([
                new TableRowNode([
                    new TableCellNode([new ParagraphNode(InlineContent.FromText("needle here"))]),
                ]),
            ]),
        ]);

        var match = Assert.Single(DocumentSearch.Find(doc, "needle"));

        Assert.Equal("needle", DocumentText.Of(Slice.Cut(doc, match.From, match.To).Content));
    }

    /// <remarks>
    /// The positions between two blocks are boundary tokens, not text. A match covering them is
    /// not a range anything could replace.
    /// </remarks>
    [Fact]
    public void NoMatchSpansTwoBlocks()
    {
        var doc = DocumentNode.FromParagraphs("ab", "cd");

        Assert.Empty(DocumentSearch.Find(doc, "bc"));
    }

    [Fact]
    public void ItIsCaseInsensitiveUnlessAsked()
    {
        var doc = DocumentNode.FromParagraphs("Cat cat CAT");

        Assert.Equal(3, DocumentSearch.Find(doc, "cat").Length);
        Assert.Single(DocumentSearch.Find(doc, "cat", new SearchOptions { MatchCase = true }));
    }

    [Fact]
    public void WholeWordRejectsAMatchInsideAWord()
    {
        var doc = DocumentNode.FromParagraphs("cat scatter cat_x cat9 cat");
        var options = new SearchOptions { WholeWord = true };

        Assert.Equal(2, DocumentSearch.Find(doc, "cat", options).Length);
    }

    /// <remarks>
    /// Rejecting a match must step past its start, not past its length, or a match that begins
    /// inside a rejected one is skipped with it. The query overlaps itself at a shift of two, so
    /// stepping by its length lands past the real match rather than on it.
    /// </remarks>
    [Fact]
    public void WholeWordStillFindsTheWordAfterARejectedOne()
    {
        var doc = DocumentNode.FromParagraphs("ba a a");
        var options = new SearchOptions { WholeWord = true };

        var match = Assert.Single(DocumentSearch.Find(doc, "a a", options));

        Assert.Equal("a a", DocumentText.Of(Slice.Cut(doc, match.From, match.To).Content));
        Assert.Equal(4, match.From);
    }

    /// <remarks>
    /// The simpler case, where the rejected match and the real one do not overlap.
    /// </remarks>
    [Fact]
    public void WholeWordLooksPastAWordThatMerelyContainsTheQuery()
    {
        var doc = DocumentNode.FromParagraphs("scat cat");
        var options = new SearchOptions { WholeWord = true };

        var match = Assert.Single(DocumentSearch.Find(doc, "cat", options));

        Assert.Equal("cat", DocumentText.Of(Slice.Cut(doc, match.From, match.To).Content));
        Assert.Equal(6, match.From);
    }

    /// <remarks>
    /// F-70. A letter outside the basic plane is two code units, and neither half of a surrogate
    /// pair is a letter, so judging the neighbour one <c>char</c> at a time took 𝐀 (U+1D400,
    /// mathematical bold capital A) and 𐐀 (U+10400, Deseret) for word boundaries. The neighbours
    /// are whole characters now. An astral character that is not a letter, an emoji, is still a
    /// boundary, and an astral digit is still part of the word.
    /// </remarks>
    [Theory]
    [InlineData("\U0001D400cat", 0)]
    [InlineData("cat\U0001D400", 0)]
    [InlineData("\U00010400cat\U00010400", 0)]
    [InlineData("cat\U0001D7CE", 0)]
    [InlineData("\U0001F600cat\U0001F600", 1)]
    [InlineData("\U0001D400 cat \U0001D400", 1)]
    [InlineData("cat", 1)]
    public void WholeWordJudgesItsNeighboursAsWholeCharacters(string text, int expected)
    {
        var doc = DocumentNode.FromParagraphs(text);

        Assert.Equal(
            expected,
            DocumentSearch.Find(doc, "cat", new SearchOptions { WholeWord = true }).Length);
    }

    /// <remarks>
    /// Overlapping matches cannot both be replaced. Three would be worse than one.
    /// </remarks>
    [Fact]
    public void MatchesDoNotOverlap()
    {
        var doc = DocumentNode.FromParagraphs("aaaa");

        Assert.Equal(2, DocumentSearch.Find(doc, "aa").Length);
    }

    [Fact]
    public void AnEmptyQueryMatchesNothing()
    {
        Assert.Empty(DocumentSearch.Find(DocumentNode.FromParagraphs("anything"), string.Empty));
    }

    [Fact]
    public void AQueryLongerThanTheTextMatchesNothing()
    {
        Assert.Empty(DocumentSearch.Find(DocumentNode.FromParagraphs("ab"), "abcdef"));
    }

    /// <remarks>
    /// <para>
    /// A match is handed straight to a replacement, so a match that is not its own length would
    /// corrupt the document. Culture-sensitive comparison treats a soft hyphen as having no
    /// weight, so it finds a nine-code-unit query inside a ten-code-unit run and reports an index
    /// with no length to go with it; replacing that range leaves the stray character behind.
    /// Ordinal folds per code unit and cannot change a length.
    /// </para>
    /// <para>
    /// Measured on this machine (en-IL, ICU): <c>"co\u00ADoperate".IndexOf("cooperate", ...)</c>
    /// is 0 for both culture-sensitive comparisons and -1 for both ordinal ones. The German sharp
    /// s that the documentation usually cites does <em>not</em> discriminate under ICU.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComparisonNeverChangesALength(bool matchCase)
    {
        var doc = DocumentNode.FromParagraphs("co\u00ADoperate cooperate");
        var options = new SearchOptions { MatchCase = matchCase };

        var match = Assert.Single(DocumentSearch.Find(doc, "cooperate", options));

        Assert.Equal(9, match.Length);
        Assert.Equal("cooperate", DocumentText.Of(Slice.Cut(doc, match.From, match.To).Content));
    }

    /// <remarks>
    /// The whole point of the position arithmetic. A replacement applied to what the search
    /// returned has to leave the document saying what a reader would expect.
    /// </remarks>
    [Fact]
    public void ReplacingEveryMatchBackToFrontRewritesTheDocument()
    {
        var doc = DocumentNode.FromParagraphs("the cat sat", "on the cat");
        var state = EditorState.Create(doc);
        var matches = DocumentSearch.Find(doc, "cat");
        var transaction = state.Transaction();

        // Back to front, so that an earlier replacement does not move a later match.
        foreach (var match in matches.Reverse())
        {
            transaction.Replace(
                match.From, match.To, Slice.OfInline(InlineContent.FromText("dog")));
        }

        var after = state.Apply(transaction);

        Assert.Empty(transaction.Failures);
        Assert.Equal("the dog sat\non the dog", DocumentText.Of(after.Doc));
    }

    [Fact]
    public void NextFromTakesTheMatchAtOrAfterAPosition()
    {
        var matches = ImmutableArray.Create(
            new SearchMatch(10, 13), new SearchMatch(20, 23), new SearchMatch(30, 33));

        Assert.Equal(0, DocumentSearch.NextFrom(matches, 0));
        Assert.Equal(1, DocumentSearch.NextFrom(matches, 11));
        Assert.Equal(1, DocumentSearch.NextFrom(matches, 20));
        Assert.Equal(0, DocumentSearch.NextFrom(matches, 31));
        Assert.Equal(-1, DocumentSearch.NextFrom(matches, 31, wrap: false));
    }

    [Fact]
    public void PreviousFromTakesTheMatchEndingAtOrBeforeAPosition()
    {
        var matches = ImmutableArray.Create(
            new SearchMatch(10, 13), new SearchMatch(20, 23), new SearchMatch(30, 33));

        Assert.Equal(2, DocumentSearch.PreviousFrom(matches, 40));
        Assert.Equal(0, DocumentSearch.PreviousFrom(matches, 20));
        Assert.Equal(0, DocumentSearch.PreviousFrom(matches, 13));
        Assert.Equal(2, DocumentSearch.PreviousFrom(matches, 12));
        Assert.Equal(-1, DocumentSearch.PreviousFrom(matches, 12, wrap: false));
    }

    [Fact]
    public void SteppingThroughNothingFindsNothing()
    {
        Assert.Equal(-1, DocumentSearch.NextFrom([], 0));
        Assert.Equal(-1, DocumentSearch.PreviousFrom([], 0));
    }

    [Fact]
    public void ItRefusesNull()
    {
        Assert.Throws<ArgumentNullException>(() => DocumentSearch.Find(null!, "a"));
        Assert.Throws<ArgumentNullException>(
            () => DocumentSearch.Find(DocumentNode.Empty, null!));
        Assert.Throws<ArgumentNullException>(() => DocumentSearch.NextFrom(null!, 0));
        Assert.Throws<ArgumentNullException>(() => DocumentSearch.PreviousFrom(null!, 0));
    }
}
