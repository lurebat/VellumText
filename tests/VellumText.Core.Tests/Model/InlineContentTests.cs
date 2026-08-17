namespace VellumText.Tests.Model;

// Mark and embed collections are compared via ToList(). ImmutableArray<T> implements
// IEquatable<ImmutableArray<T>> as reference equality on the inner array, so passing two
// ImmutableArrays (or two arrays) to Assert.Equal binds the scalar overload and compares
// references - which fails for equal content while printing both sides identically.

public class InlineContentTests
{
    private static readonly MarkSet Bold = new(TextStyle.Bold);
    private static readonly MarkSet Italic = new(TextStyle.Italic);
    private static readonly MarkSet BoldItalic = new(TextStyle.Bold | TextStyle.Italic);

    private static InlineContent Content(string text, params ValueSpan<MarkSet>[] marks) =>
        InlineContent.Create(text, marks, []);

    [Fact]
    public void Empty_has_nothing_in_it()
    {
        Assert.True(InlineContent.Empty.IsEmpty);
        Assert.Equal(0, InlineContent.Empty.Length);
        Assert.Empty(InlineContent.Empty.Marks.ToList());
        Assert.Empty(InlineContent.Empty.Embeds.ToList());
    }

    [Fact]
    public void FromText_with_no_mark_stores_no_spans()
    {
        var content = InlineContent.FromText("hello");

        Assert.Equal("hello", content.Text);
        Assert.Empty(content.Marks.ToList());
        Assert.Equal(5, content.Length);
    }

    [Fact]
    public void FromText_refuses_a_bare_placeholder()
    {
        // A placeholder with no embed behind it would desynchronize every offset that
        // follows it, and nothing downstream could detect the mismatch.
        Assert.Throws<ArgumentException>(() => InlineContent.FromText("a\uFFFCb"));
    }

    // --- canonical form -------------------------------------------------------------

    [Fact]
    public void A_span_that_overrides_nothing_is_not_stored()
    {
        var content = Content("hello", new ValueSpan<MarkSet>(0, 5, MarkSet.Empty));

        Assert.Empty(content.Marks.ToList());
    }

    [Fact]
    public void An_empty_span_is_not_stored()
    {
        var content = Content("hello", new ValueSpan<MarkSet>(2, 0, Bold));

        Assert.Empty(content.Marks.ToList());
    }

    [Fact]
    public void Spans_are_sorted_regardless_of_input_order()
    {
        var content = Content(
            "abcdef",
            new ValueSpan<MarkSet>(4, 2, Italic),
            new ValueSpan<MarkSet>(0, 2, Bold));

        Assert.Equal(
            [new ValueSpan<MarkSet>(0, 2, Bold), new ValueSpan<MarkSet>(4, 2, Italic)],
            content.Marks.ToList());
    }

    [Fact]
    public void Adjacent_equal_spans_merge()
    {
        var content = Content(
            "abcdef",
            new ValueSpan<MarkSet>(0, 3, Bold),
            new ValueSpan<MarkSet>(3, 3, Bold));

        Assert.Equal([new ValueSpan<MarkSet>(0, 6, Bold)], content.Marks.ToList());
    }

    [Fact]
    public void Adjacent_unequal_spans_do_not_merge()
    {
        var content = Content(
            "abcdef",
            new ValueSpan<MarkSet>(0, 3, Bold),
            new ValueSpan<MarkSet>(3, 3, Italic));

        Assert.Equal(2, content.Marks.Length);
    }

    [Fact]
    public void Three_adjacent_equal_spans_merge_into_one()
    {
        var content = Content(
            "abcdef",
            new ValueSpan<MarkSet>(0, 2, Bold),
            new ValueSpan<MarkSet>(2, 2, Bold),
            new ValueSpan<MarkSet>(4, 2, Bold));

        Assert.Equal([new ValueSpan<MarkSet>(0, 6, Bold)], content.Marks.ToList());
    }

    [Fact]
    public void Equal_spans_separated_by_a_gap_stay_separate()
    {
        var content = Content(
            "abcdef",
            new ValueSpan<MarkSet>(0, 2, Bold),
            new ValueSpan<MarkSet>(4, 2, Bold));

        Assert.Equal(2, content.Marks.Length);
    }

    [Fact]
    public void The_same_logical_content_built_differently_compares_equal()
    {
        // This is the property the undo round-trip test depends on: documents that render
        // identically must compare identically, whatever route produced them.
        var built = Content(
            "abcdef",
            new ValueSpan<MarkSet>(3, 3, Bold),
            new ValueSpan<MarkSet>(0, 3, Bold));

        var edited = InlineContent.FromText("abc", Bold).Concat(InlineContent.FromText("def", Bold));

        Assert.Equal(built, edited);
        Assert.Equal(built.GetHashCode(), edited.GetHashCode());
        Assert.Equal([new ValueSpan<MarkSet>(0, 6, Bold)], edited.Marks.ToList());
    }

    [Fact]
    public void Overlapping_spans_are_rejected()
    {
        var ex = Assert.Throws<ArgumentException>(() => Content(
            "abcdef",
            new ValueSpan<MarkSet>(0, 4, Bold),
            new ValueSpan<MarkSet>(2, 4, Italic)));

        Assert.Contains("overlap", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_span_past_the_end_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Content("abc", new ValueSpan<MarkSet>(1, 5, Bold)));
    }

    [Fact]
    public void A_span_on_empty_text_is_rejected_rather_than_ignored()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Content("", new ValueSpan<MarkSet>(0, 3, Bold)));
    }

    // --- lookups --------------------------------------------------------------------

    [Fact]
    public void MarkAt_finds_the_covering_span()
    {
        var content = Content(
            "abcdef",
            new ValueSpan<MarkSet>(0, 2, Bold),
            new ValueSpan<MarkSet>(4, 2, Italic));

        Assert.Equal(Bold, content.MarkAt(0));
        Assert.Equal(Bold, content.MarkAt(1));
        Assert.Equal(MarkSet.Empty, content.MarkAt(2));
        Assert.Equal(MarkSet.Empty, content.MarkAt(3));
        Assert.Equal(Italic, content.MarkAt(4));
        Assert.Equal(Italic, content.MarkAt(5));
    }

    [Fact]
    public void MarkAt_rejects_an_offset_past_the_last_character()
    {
        var content = InlineContent.FromText("abc", Bold);

        Assert.Throws<ArgumentOutOfRangeException>(() => content.MarkAt(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => content.MarkAt(-1));
    }

    [Fact]
    public void MarkForInsertionAt_inherits_from_the_character_before_the_caret()
    {
        var content = Content("abcdef", new ValueSpan<MarkSet>(0, 3, Bold));

        Assert.Equal(Bold, content.MarkForInsertionAt(3));       // end of the bold run
        Assert.Equal(MarkSet.Empty, content.MarkForInsertionAt(4));
        Assert.Equal(MarkSet.Empty, content.MarkForInsertionAt(6)); // end of the text
    }

    [Fact]
    public void MarkForInsertionAt_zero_looks_forward_because_nothing_precedes_it()
    {
        var content = Content("abcdef", new ValueSpan<MarkSet>(0, 3, Bold));

        Assert.Equal(Bold, content.MarkForInsertionAt(0));
    }

    [Fact]
    public void MarkForInsertionAt_on_empty_content_is_empty()
    {
        Assert.Equal(MarkSet.Empty, InlineContent.Empty.MarkForInsertionAt(0));
    }

    // --- surrogate pairs ------------------------------------------------------------

    [Fact]
    public void The_middle_of_a_surrogate_pair_is_not_a_valid_boundary()
    {
        var content = InlineContent.FromText("a\U0001F600b"); // a + emoji (2 units) + b

        Assert.True(content.IsValidBoundary(0));
        Assert.True(content.IsValidBoundary(1));
        Assert.False(content.IsValidBoundary(2)); // between the surrogates
        Assert.True(content.IsValidBoundary(3));
        Assert.True(content.IsValidBoundary(4));
    }

    [Fact]
    public void Out_of_range_offsets_are_not_valid_boundaries()
    {
        var content = InlineContent.FromText("abc");

        Assert.False(content.IsValidBoundary(-1));
        Assert.False(content.IsValidBoundary(4));
        Assert.True(content.IsValidBoundary(3));
    }

    [Fact]
    public void Editing_across_half_a_surrogate_pair_is_refused()
    {
        // "never produces invalid UTF-16" is enforced at the lowest level that can see the
        // text, so every operation layered on top inherits it.
        var content = InlineContent.FromText("a\U0001F600b");

        Assert.Throws<ArgumentException>(() => content.Substring(0, 2));
        Assert.Throws<ArgumentException>(() => content.Remove(2, 1));
        Assert.Throws<ArgumentException>(
            () => content.ApplyMarks(1, 1, Bold, MarkFields.Bold));
    }

    [Fact]
    public void An_unpaired_surrogate_is_not_text_and_is_refused()
    {
        // Without this the "never invalid UTF-16" guarantee starts out already broken, and
        // IsValidBoundary becomes a promise the type cannot keep.
        Assert.Throws<ArgumentException>(() => InlineContent.FromText("\uD83D"));
        Assert.Throws<ArgumentException>(() => InlineContent.FromText("\uDE00"));
        Assert.Throws<ArgumentException>(() => InlineContent.FromText("a\uD83Db"));
        Assert.Throws<ArgumentException>(() => InlineContent.FromText("ab\uDE00"));
    }

    [Fact]
    public void A_formatting_span_may_not_end_inside_a_surrogate_pair()
    {
        // Half a character cannot be bold.
        Assert.Throws<ArgumentException>(() => InlineContent.Create(
            "\U0001F600", [new ValueSpan<MarkSet>(0, 1, Bold)], []));

        Assert.Throws<ArgumentException>(() => InlineContent.Create(
            "a\U0001F600b", [new ValueSpan<MarkSet>(2, 2, Bold)], []));

        // The pair taken whole is fine.
        Assert.Equal(
            Bold, InlineContent.Create(
                "a\U0001F600b", [new ValueSpan<MarkSet>(1, 3, Bold)], []).MarkAt(1));
    }

    // --- substring ------------------------------------------------------------------

    [Fact]
    public void Substring_clips_the_spans_it_crosses()
    {
        var content = Content("abcdefgh", new ValueSpan<MarkSet>(2, 4, Bold)); // [2..6)

        var slice = content.Substring(4, 4); // [4..8) -> bold covers its first two

        Assert.Equal("efgh", slice.Text);
        Assert.Equal([new ValueSpan<MarkSet>(0, 2, Bold)], slice.Marks.ToList());
    }

    [Fact]
    public void Substring_of_nothing_is_empty()
    {
        Assert.Equal(InlineContent.Empty, InlineContent.FromText("abc", Bold).Substring(1, 0));
    }

    [Fact]
    public void Substring_of_everything_is_equal_to_the_original()
    {
        var content = Content("abcdef", new ValueSpan<MarkSet>(1, 2, Bold));

        Assert.Equal(content, content.Substring(0, content.Length));
    }

    [Fact]
    public void Substring_rejects_a_range_past_the_end()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => InlineContent.FromText("abc").Substring(2, 5));
    }

    // --- replace --------------------------------------------------------------------

    [Fact]
    public void Replace_shifts_the_spans_that_follow_it()
    {
        var content = Content("abcdef", new ValueSpan<MarkSet>(4, 2, Bold)); // "ef" bold

        var result = content.Replace(0, 1, InlineContent.FromText("XYZ")); // +2 units

        Assert.Equal("XYZbcdef", result.Text);
        Assert.Equal([new ValueSpan<MarkSet>(6, 2, Bold)], result.Marks.ToList());
    }

    [Fact]
    public void Replace_splits_a_span_it_lands_inside()
    {
        var content = InlineContent.FromText("abcdef", Bold);

        var result = content.Replace(2, 2, InlineContent.FromText("XY"));

        Assert.Equal("abXYef", result.Text);
        Assert.Equal(
            [new ValueSpan<MarkSet>(0, 2, Bold), new ValueSpan<MarkSet>(4, 2, Bold)],
            result.Marks.ToList());
    }

    [Fact]
    public void Replace_merges_at_the_seam_when_the_marks_agree()
    {
        // Typing a bold character inside a bold word must not leave three spans behind, or
        // the same visible document would have several representations.
        var content = InlineContent.FromText("abcdef", Bold);

        var result = content.Replace(3, 0, InlineContent.FromText("X", Bold));

        Assert.Equal("abcXdef", result.Text);
        Assert.Equal([new ValueSpan<MarkSet>(0, 7, Bold)], result.Marks.ToList());
    }

    [Fact]
    public void Remove_deletes_spans_wholly_inside_the_range()
    {
        var content = Content("abcdefgh", new ValueSpan<MarkSet>(3, 2, Bold));

        var result = content.Remove(2, 4);

        Assert.Equal("abgh", result.Text);
        Assert.Empty(result.Marks.ToList());
    }

    [Fact]
    public void Remove_rejoins_the_marks_that_meet_across_the_gap()
    {
        var content = Content(
            "abcdef",
            new ValueSpan<MarkSet>(0, 2, Bold),
            new ValueSpan<MarkSet>(4, 2, Bold));

        var result = content.Remove(2, 2);

        Assert.Equal("abef", result.Text);
        Assert.Equal([new ValueSpan<MarkSet>(0, 4, Bold)], result.Marks.ToList());
    }

    [Fact]
    public void Insert_and_Concat_agree_with_Replace()
    {
        var content = InlineContent.FromText("abc", Bold);
        var extra = InlineContent.FromText("def", Italic);

        Assert.Equal(content.Replace(3, 0, extra), content.Insert(3, extra));
        Assert.Equal(content.Replace(3, 0, extra), content.Concat(extra));
    }

    [Fact]
    public void Replacing_everything_leaves_only_the_replacement()
    {
        var content = InlineContent.FromText("abc", Bold);
        var replacement = InlineContent.FromText("xyz", Italic);

        Assert.Equal(replacement, content.Replace(0, content.Length, replacement));
    }

    // --- applying marks -------------------------------------------------------------

    [Fact]
    public void ApplyMarks_marks_a_previously_unmarked_range()
    {
        var content = InlineContent.FromText("abcdef");

        var result = content.ApplyMarks(2, 2, Bold, MarkFields.Bold);

        Assert.Equal([new ValueSpan<MarkSet>(2, 2, Bold)], result.Marks.ToList());
    }

    [Fact]
    public void ApplyMarks_leaves_the_text_and_embeds_alone()
    {
        var content = InlineContent.FromText("abcdef");

        var result = content.ApplyMarks(2, 2, Bold, MarkFields.Bold);

        Assert.Equal(content.Text, result.Text);
    }

    [Fact]
    public void ApplyMarks_adds_to_existing_formatting_rather_than_replacing_it()
    {
        var content = InlineContent.FromText("abcdef", Italic);

        var result = content.ApplyMarks(0, 6, Bold, MarkFields.Bold);

        Assert.Equal([new ValueSpan<MarkSet>(0, 6, BoldItalic)], result.Marks.ToList());
    }

    [Fact]
    public void ApplyMarks_spanning_marked_and_unmarked_text_produces_the_right_pieces()
    {
        var content = Content("abcdef", new ValueSpan<MarkSet>(0, 3, Italic));

        var result = content.ApplyMarks(0, 6, Bold, MarkFields.Bold);

        Assert.Equal(
            [new ValueSpan<MarkSet>(0, 3, BoldItalic), new ValueSpan<MarkSet>(3, 3, Bold)],
            result.Marks.ToList());
    }

    [Fact]
    public void ApplyMarks_merges_with_a_neighbour_it_now_matches()
    {
        var content = Content("abcdef", new ValueSpan<MarkSet>(0, 3, Bold));

        var result = content.ApplyMarks(3, 3, Bold, MarkFields.Bold);

        Assert.Equal([new ValueSpan<MarkSet>(0, 6, Bold)], result.Marks.ToList());
    }

    [Fact]
    public void Removing_the_only_mark_leaves_no_spans_at_all()
    {
        var content = InlineContent.FromText("abcdef", Bold);

        var result = content.ApplyMarks(0, 6, MarkSet.Empty, MarkFields.Bold);

        Assert.Empty(result.Marks.ToList());
        Assert.Equal(InlineContent.FromText("abcdef"), result);
    }

    [Fact]
    public void Removing_a_mark_from_the_middle_splits_the_span()
    {
        var content = InlineContent.FromText("abcdef", Bold);

        var result = content.ApplyMarks(2, 2, MarkSet.Empty, MarkFields.Bold);

        Assert.Equal(
            [new ValueSpan<MarkSet>(0, 2, Bold), new ValueSpan<MarkSet>(4, 2, Bold)],
            result.Marks.ToList());
    }

    [Fact]
    public void ApplyMarks_over_nothing_changes_nothing()
    {
        var content = InlineContent.FromText("abcdef", Bold);

        Assert.Equal(content, content.ApplyMarks(3, 0, Italic, MarkFields.Italic));
        Assert.Equal(content, content.ApplyMarks(0, 6, Italic, MarkFields.None));
    }

    [Fact]
    public void ApplyMarks_is_idempotent()
    {
        var content = Content("abcdefgh", new ValueSpan<MarkSet>(1, 3, Italic));

        var once = content.ApplyMarks(2, 4, Bold, MarkFields.Bold);
        var twice = once.ApplyMarks(2, 4, Bold, MarkFields.Bold);

        Assert.Equal(once, twice);
    }

    // --- embeds ---------------------------------------------------------------------

    [Fact]
    public void An_embed_occupies_exactly_one_position()
    {
        var content = InlineContent.FromEmbed(new ImageEmbed("cat.png"));

        Assert.Equal(1, content.Length);
        Assert.Equal("\uFFFC", content.Text);
        Assert.Single(content.Embeds.ToList());
    }

    [Fact]
    public void Placeholders_and_embeds_must_correspond_one_to_one()
    {
        Assert.Throws<ArgumentException>(
            () => InlineContent.Create("a\uFFFCb", [], []));

        Assert.Throws<ArgumentException>(
            () => InlineContent.Create("ab", [], [new ImageEmbed("cat.png")]));
    }

    [Fact]
    public void TryGetEmbedAt_finds_an_embed_only_at_its_placeholder()
    {
        var cat = new ImageEmbed("cat.png");
        var dog = new ImageEmbed("dog.png");
        var content = InlineContent.Create("a\uFFFCb\uFFFC", [], [cat, dog]);

        Assert.True(content.TryGetEmbedAt(1, out var first));
        Assert.Same(cat, first);
        Assert.True(content.TryGetEmbedAt(3, out var second));
        Assert.Same(dog, second);
        Assert.False(content.TryGetEmbedAt(0, out _));
        Assert.False(content.TryGetEmbedAt(2, out _));
    }

    [Fact]
    public void Substring_keeps_the_embeds_its_range_covers()
    {
        var cat = new ImageEmbed("cat.png");
        var dog = new ImageEmbed("dog.png");
        var content = InlineContent.Create("a\uFFFCb\uFFFCc", [], [cat, dog]);

        var slice = content.Substring(2, 3); // "b\uFFFCc"

        Assert.Equal([dog], slice.Embeds.ToList());
        Assert.True(slice.TryGetEmbedAt(1, out var found));
        Assert.Same(dog, found);
    }

    [Fact]
    public void Replace_keeps_embeds_in_placeholder_order_across_the_seam()
    {
        // Embeds are positional - the i-th embed belongs to the i-th placeholder - so an
        // insertion in the middle has to interleave them, not append.
        var cat = new ImageEmbed("cat.png");
        var dog = new ImageEmbed("dog.png");
        var fish = new ImageEmbed("fish.png");

        var content = InlineContent.Create("\uFFFCx\uFFFC", [], [cat, dog]);
        var result = content.Replace(1, 1, InlineContent.FromEmbed(fish));

        Assert.Equal("\uFFFC\uFFFC\uFFFC", result.Text);
        Assert.Equal([cat, fish, dog], result.Embeds.ToList());
    }

    [Fact]
    public void Removing_an_embeds_placeholder_removes_the_embed()
    {
        var cat = new ImageEmbed("cat.png");
        var dog = new ImageEmbed("dog.png");
        var content = InlineContent.Create("\uFFFCx\uFFFC", [], [cat, dog]);

        var result = content.Remove(0, 1);

        Assert.Equal("x\uFFFC", result.Text);
        Assert.Equal([dog], result.Embeds.ToList());
    }

    [Fact]
    public void An_embed_can_carry_marks_like_any_other_character()
    {
        var content = InlineContent.FromEmbed(new ImageEmbed("cat.png"), Bold);

        Assert.Equal(Bold, content.MarkAt(0));
    }

    [Fact]
    public void ToPlainText_substitutes_the_fallback_text()
    {
        var content = InlineContent.Create(
            "see \uFFFC here",
            [],
            [new ImageEmbed("cat.png", altText: "a cat")]);

        Assert.Equal("see a cat here", content.ToPlainText());
    }

    [Fact]
    public void ToPlainText_of_text_without_embeds_is_the_text()
    {
        Assert.Equal("hello", InlineContent.FromText("hello", Bold).ToPlainText());
    }

    // --- equality -------------------------------------------------------------------

    [Fact]
    public void Equality_covers_text_marks_and_embeds()
    {
        Assert.Equal(InlineContent.FromText("abc", Bold), InlineContent.FromText("abc", Bold));
        Assert.NotEqual(InlineContent.FromText("abc", Bold), InlineContent.FromText("abd", Bold));
        Assert.NotEqual(InlineContent.FromText("abc", Bold), InlineContent.FromText("abc", Italic));

        Assert.NotEqual(
            InlineContent.FromEmbed(new ImageEmbed("cat.png")),
            InlineContent.FromEmbed(new ImageEmbed("dog.png")));

        Assert.Equal(
            InlineContent.FromEmbed(new ImageEmbed("cat.png")),
            InlineContent.FromEmbed(new ImageEmbed("cat.png")));
    }

    [Fact]
    public void An_edit_and_its_inverse_restore_the_exact_original()
    {
        // A miniature of the undo round-trip property, at the level where representation
        // uniqueness is actually decided.
        var original = Content(
            "The quick brown fox",
            new ValueSpan<MarkSet>(4, 5, Bold),
            new ValueSpan<MarkSet>(10, 5, Italic));

        var removed = original.Substring(4, 5);
        var edited = original.Remove(4, 5);
        var restored = edited.Insert(4, removed);

        Assert.Equal(original, restored);
        Assert.Equal(original.Marks, restored.Marks.ToList());
    }
}

public class ImageEmbedTests
{
    [Fact]
    public void Rejects_a_blank_source()
    {
        Assert.Throws<ArgumentException>(() => new ImageEmbed("  "));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-4d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rejects_a_nonsensical_dimension(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageEmbed("cat.png", width: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ImageEmbed("cat.png", height: value));
    }

    [Fact]
    public void With_expression_validates_too()
    {
        var embed = new ImageEmbed("cat.png");

        Assert.Throws<ArgumentOutOfRangeException>(() => embed with { Width = -1 });
    }

    [Fact]
    public void Blank_alt_text_normalizes_to_null()
    {
        Assert.Null(new ImageEmbed("cat.png", altText: "   ").AltText);
    }

    [Fact]
    public void Falls_back_to_alt_text_in_plain_text()
    {
        Assert.Equal("a cat", new ImageEmbed("cat.png", altText: "a cat").PlainTextFallback);
        Assert.Equal("", new ImageEmbed("cat.png").PlainTextFallback);
    }

    [Fact]
    public void Equality_is_structural()
    {
        Assert.Equal(new ImageEmbed("cat.png", 10, 20), new ImageEmbed("cat.png", 10, 20));
        Assert.NotEqual(new ImageEmbed("cat.png", 10, 20), new ImageEmbed("cat.png", 10, 21));
    }

    [Fact]
    public void TheNextPlaceholderIsFoundWithoutScanningTheText()
    {
        // Used by the text formatter, which asks per line. A scan of the text would make laying
        // out one paragraph quadratic in its length rather than in the number of embeds.
        var content = InlineContent.Create(
            $"a{InlineContent.Placeholder}b{InlineContent.Placeholder}c",
            [],
            [new ImageEmbed("1.png"), new ImageEmbed("2.png")]);

        Assert.Equal(1, content.NextPlaceholderFrom(0));
        Assert.Equal(1, content.NextPlaceholderFrom(1));
        Assert.Equal(3, content.NextPlaceholderFrom(2));
        Assert.Equal(3, content.NextPlaceholderFrom(3));
        Assert.Equal(-1, content.NextPlaceholderFrom(4));
        Assert.Equal(-1, content.NextPlaceholderFrom(9999));
    }

    [Fact]
    public void ContentWithNoEmbedsHasNoPlaceholderAnywhere()
    {
        Assert.Equal(-1, InlineContent.FromText("plain").NextPlaceholderFrom(0));
        Assert.Equal(-1, InlineContent.Empty.NextPlaceholderFrom(0));
    }

    [Fact]
    public void TheLookupAgreesWithScanningTheTextItReplaces()
    {
        // The old implementation scanned with IndexOf. Anywhere the two disagreed would be a
        // rendering bug, so they are checked against each other across every offset.
        var content = InlineContent.Create(
            $"{InlineContent.Placeholder}ab{InlineContent.Placeholder}{InlineContent.Placeholder}cd",
            [],
            [new ImageEmbed("1.png"), new ImageEmbed("2.png"), new ImageEmbed("3.png")]);

        for (var i = 0; i <= content.Length; i++)
        {
            Assert.Equal(
                content.Text.IndexOf(InlineContent.Placeholder, Math.Min(i, content.Length)),
                content.NextPlaceholderFrom(i));
        }
    }
}