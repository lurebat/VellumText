namespace VellumText.Tests.Model;

public class NodeSizeTests
{
    // The position scheme is the pivotal choice in the whole model, so the arithmetic is
    // spelled out by hand here rather than derived from the implementation.

    [Fact]
    public void A_paragraph_is_its_text_plus_two_boundaries()
    {
        var paragraph = ParagraphNode.FromText("hi");

        Assert.Equal(2, paragraph.ContentSize);
        Assert.Equal(4, paragraph.NodeSize);
    }

    [Fact]
    public void An_empty_paragraph_still_occupies_its_boundaries()
    {
        Assert.Equal(0, ParagraphNode.Empty.ContentSize);
        Assert.Equal(2, ParagraphNode.Empty.NodeSize);
    }

    [Fact]
    public void An_embed_counts_as_one_position_like_a_character()
    {
        var paragraph = new ParagraphNode(
            InlineContent.FromText("a").Concat(InlineContent.FromEmbed(new ImageEmbed("c.png"))));

        Assert.Equal(2, paragraph.ContentSize);
    }

    [Fact]
    public void A_leaf_occupies_exactly_one_position()
    {
        Assert.Equal(0, RuleNode.Instance.ContentSize);
        Assert.Equal(1, RuleNode.Instance.NodeSize);
        Assert.True(RuleNode.Instance.IsLeaf);

        var image = new BlockImageNode(new ImageEmbed("cat.png"));
        Assert.Equal(1, image.NodeSize);
        Assert.True(image.IsLeaf);
    }

    [Fact]
    public void A_document_is_the_sum_of_what_its_blocks_occupy()
    {
        // "hi" -> 2 + 2 boundaries = 4; the rule adds 1.
        var doc = new DocumentNode([ParagraphNode.FromText("hi"), RuleNode.Instance]);

        Assert.Equal(5, doc.ContentSize);
    }

    [Fact]
    public void The_empty_document_holds_one_empty_paragraph()
    {
        Assert.Equal(2, DocumentNode.Empty.ContentSize);
        Assert.Single(DocumentNode.Empty.Blocks);
    }

    [Fact]
    public void Nesting_accumulates_two_positions_per_level()
    {
        // paragraph "a" = 1 + 2 = 3; item = 3 + 2 = 5; list = 5 + 2 = 7.
        var list = new ListNode([ListItemNode.FromText("a")]);

        Assert.Equal(3, list.Items[0].ContentSize);
        Assert.Equal(5, list.Items[0].NodeSize);
        Assert.Equal(5, list.ContentSize);
        Assert.Equal(7, list.NodeSize);
        Assert.Equal(7, new DocumentNode([list]).ContentSize);
    }

    [Fact]
    public void A_table_accumulates_three_levels_of_boundaries()
    {
        // paragraph "x" = 3; cell = 5; row = 7; table = 9.
        var table = new TableNode([new TableRowNode([TableCellNode.FromText("x")])]);

        Assert.Equal(3, table.Rows[0].Cells[0].ContentSize);
        Assert.Equal(5, table.Rows[0].ContentSize);
        Assert.Equal(7, table.ContentSize);
        Assert.Equal(9, table.NodeSize);
    }

    [Fact]
    public void Size_is_the_sum_of_what_is_inside_it_at_every_level()
    {
        // The stored size is the one number everything else is arithmetic on, so it is checked
        // against an independent walk rather than against itself.
        static int Recompute(Node node) => node switch
        {
            ParagraphNode paragraph => paragraph.Content.Length + 2,
            _ when node.IsLeaf => 1,
            _ => node.Children.Sum(Recompute) + 2,
        };

        var doc = DocumentNode.FromParagraphs("one", "two", "three");

        Assert.Equal(5 + 5 + 7, doc.ContentSize);
        Assert.Equal(doc.Children.Sum(child => child.NodeSize), doc.ContentSize);

        foreach (var node in Sample.Nested.Children)
        {
            Assert.Equal(Recompute(node), node.NodeSize);
        }
    }
}

public class NodeEqualityTests
{
    [Fact]
    public void Identical_documents_built_separately_compare_equal()
    {
        // Records compare ImmutableArray fields by reference, so this equality is written by
        // hand. If it regressed, the undo round-trip property test would fail for every
        // input and the cause would be nowhere near the failure.
        var a = DocumentNode.FromParagraphs("one", "two");
        var b = DocumentNode.FromParagraphs("one", "two");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void Differing_text_compares_unequal()
    {
        Assert.NotEqual(DocumentNode.FromParagraphs("one"), DocumentNode.FromParagraphs("two"));
    }

    [Fact]
    public void Differing_structure_compares_unequal()
    {
        Assert.NotEqual(
            DocumentNode.FromParagraphs("one", "two"),
            DocumentNode.FromParagraphs("onetwo"));
    }

    [Fact]
    public void Paragraph_attributes_participate_in_equality()
    {
        var plain = ParagraphNode.FromText("x");

        Assert.NotEqual(plain, plain.WithKind(ParagraphKind.Heading1));
        Assert.NotEqual(plain, plain.WithAlign(TextAlign.Center));
        Assert.NotEqual(plain, plain.WithIndentLevel(1));
        Assert.Equal(plain, plain.WithKind(ParagraphKind.Body));
    }

    [Fact]
    public void Cell_attributes_participate_in_equality()
    {
        var cell = TableCellNode.FromText("x");

        Assert.NotEqual(cell, new TableCellNode(cell.Blocks, columnSpan: 2));
        Assert.NotEqual(cell, new TableCellNode(cell.Blocks, background: Rgba.White));
        Assert.NotEqual(cell, new TableCellNode(cell.Blocks, isHeader: true));
        Assert.Equal(cell, new TableCellNode(cell.Blocks));
    }

    [Fact]
    public void Column_widths_participate_in_equality()
    {
        var rows = new[] { new TableRowNode([TableCellNode.FromText("x")]) };

        Assert.Equal(new TableNode(rows, [10d]), new TableNode(rows, [10d]));
        Assert.NotEqual(new TableNode(rows, [10d]), new TableNode(rows, [20d]));
        Assert.NotEqual(new TableNode(rows, [10d]), new TableNode(rows));
    }

    [Fact]
    public void List_attributes_participate_in_equality()
    {
        var items = new[] { ListItemNode.FromText("x") };

        Assert.NotEqual(new ListNode(items), new ListNode(items, ListKind.Ordered));
        Assert.NotEqual(new ListNode(items, ListKind.Ordered, 1), new ListNode(items, ListKind.Ordered, 5));
    }

    [Fact]
    public void Nodes_of_different_types_are_never_equal()
    {
        Assert.NotEqual<Node>(ParagraphNode.Empty, RuleNode.Instance);
        Assert.NotEqual<Node>(RuleNode.Instance, new BlockImageNode(new ImageEmbed("c.png")));
    }

    [Fact]
    public void A_node_equals_itself_and_not_null()
    {
        var doc = DocumentNode.Empty;

        Assert.Equal(doc, doc);
        Assert.False(doc.Equals(null));
        Assert.False(doc.Equals("not a node"));
    }

    [Fact]
    public void Equality_reaches_all_the_way_down_a_deep_tree()
    {
        static DocumentNode Build(string deepText) => new(
        [
            new TableNode(
            [
                new TableRowNode(
                [
                    new TableCellNode([new ListNode([ListItemNode.FromText(deepText)])]),
                ]),
            ]),
        ]);

        Assert.Equal(Build("same"), Build("same"));
        Assert.NotEqual(Build("same"), Build("different"));
    }
}

public class NodeConstructionTests
{
    [Fact]
    public void WithChildren_rejects_a_child_of_the_wrong_type()
    {
        var row = new TableRowNode([TableCellNode.FromText("x")]);

        var ex = Assert.Throws<ArgumentException>(
            () => row.WithChildren([ParagraphNode.Empty]));

        Assert.Contains("TableCellNode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WithChildren_accepts_a_child_of_the_right_type()
    {
        var doc = DocumentNode.FromParagraphs("a");

        var replaced = doc.WithChildren([ParagraphNode.FromText("b")]);

        Assert.Equal(DocumentNode.FromParagraphs("b"), replaced);
    }

    [Fact]
    public void An_attribute_outside_its_enum_is_refused()
    {
        // A cast is not a check, and SetBlockAttrStep is not the only way in.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ParagraphNode(InlineContent.Empty, (ParagraphKind)999));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ParagraphNode(InlineContent.Empty, align: (TextAlign)999));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ListNode([new ListItemNode([ParagraphNode.Empty])], (ListKind)999));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BlockImageNode(new ImageEmbed("c.png"), (TextAlign)999));
    }

    [Fact]
    public void WithChildren_recomputes_the_size()
    {
        var doc = DocumentNode.FromParagraphs("a");

        Assert.Equal(3, doc.ContentSize);
        Assert.Equal(6, doc.WithChildren([ParagraphNode.FromText("abcd")]).ContentSize);
    }

    [Fact]
    public void Nodes_that_cannot_hold_children_say_so()
    {
        Assert.Throws<InvalidOperationException>(() => ParagraphNode.Empty.WithChildren([]));
        Assert.Throws<InvalidOperationException>(() => RuleNode.Instance.WithChildren([]));
        Assert.Throws<InvalidOperationException>(
            () => new BlockImageNode(new ImageEmbed("c.png")).WithChildren([]));
    }

    [Fact]
    public void Null_children_are_refused()
    {
        Assert.Throws<ArgumentException>(() => new DocumentNode([null!]));
        Assert.Throws<ArgumentException>(() => new ListItemNode([null!]));
        Assert.Throws<ArgumentException>(() => new TableRowNode([null!]));
    }

    [Fact]
    public void Paragraph_refuses_a_negative_indent()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ParagraphNode(InlineContent.Empty, indentLevel: -1));
    }

    [Fact]
    public void Cell_spans_must_be_at_least_one()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TableCellNode([ParagraphNode.Empty], rowSpan: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TableCellNode([ParagraphNode.Empty], columnSpan: 0));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void Column_widths_must_be_positive_and_finite(double width)
    {
        var rows = new[] { new TableRowNode([TableCellNode.FromText("x")]) };

        Assert.Throws<ArgumentOutOfRangeException>(() => new TableNode(rows, [width]));
    }

    [Fact]
    public void Withers_preserve_the_attributes_they_do_not_change()
    {
        var paragraph = new ParagraphNode(
            InlineContent.FromText("x"), ParagraphKind.Quote, TextAlign.Right, 2);

        var changed = paragraph.WithContent(InlineContent.FromText("y"));

        Assert.Equal(ParagraphKind.Quote, changed.Kind);
        Assert.Equal(TextAlign.Right, changed.Align);
        Assert.Equal(2, changed.IndentLevel);
    }

    [Fact]
    public void Cell_withers_preserve_spans_and_background()
    {
        var cell = new TableCellNode(
            [ParagraphNode.FromText("x")], rowSpan: 2, columnSpan: 3,
            background: Rgba.White, isHeader: true);

        var changed = (TableCellNode)cell.WithChildren([ParagraphNode.FromText("y")]);

        Assert.Equal(2, changed.RowSpan);
        Assert.Equal(3, changed.ColumnSpan);
        Assert.Equal(Rgba.White, changed.Background);
        Assert.True(changed.IsHeader);
    }
}

public class DocumentSchemaTests
{
    [Fact]
    public void A_plain_document_is_valid()
    {
        Assert.True(DocumentSchema.IsValid(DocumentNode.Empty));
        Assert.True(DocumentSchema.IsValid(DocumentNode.FromParagraphs("one", "two")));
    }

    [Fact]
    public void A_document_with_no_blocks_is_invalid()
    {
        // The caret must always have somewhere to be.
        var violations = DocumentSchema.Validate(new DocumentNode([]));

        Assert.Single(violations);
        Assert.Contains("at least one block", violations[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_containers_are_invalid()
    {
        Assert.False(DocumentSchema.IsValid(new ListNode([])));
        Assert.False(DocumentSchema.IsValid(new ListItemNode([])));
        Assert.False(DocumentSchema.IsValid(new TableNode([])));
        Assert.False(DocumentSchema.IsValid(new TableRowNode([])));
        Assert.False(DocumentSchema.IsValid(new TableCellNode([])));
    }

    [Fact]
    public void Violations_are_reported_rather_than_thrown()
    {
        // Step.Apply has to be able to ask without catching, or it cannot honour its
        // contract of returning a failure instead of producing an invalid document.
        var doc = new DocumentNode([new ListNode([])]);

        var violations = DocumentSchema.Validate(doc);

        Assert.NotEmpty(violations);
    }

    [Fact]
    public void Violations_point_at_where_the_problem_is()
    {
        var doc = new DocumentNode([ParagraphNode.Empty, new ListNode([])]);

        var violations = DocumentSchema.Validate(doc);

        Assert.Equal("doc/1:list", violations[0].Path);
    }

    [Fact]
    public void Validation_reaches_into_nested_structure()
    {
        var doc = new DocumentNode(
        [
            new TableNode([new TableRowNode([new TableCellNode([])])]),
        ]);

        var violations = DocumentSchema.Validate(doc);

        Assert.Single(violations);
        Assert.Equal("doc/0:table/0:table-row/0:table-cell", violations[0].Path);
    }

    [Fact]
    public void Every_problem_is_reported_not_just_the_first()
    {
        var doc = new DocumentNode([new ListNode([]), new TableNode([])]);

        Assert.Equal(2, DocumentSchema.Validate(doc).Length);
    }

    /// <summary>
    /// Validating against a known-good previous document must agree with validating from scratch.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The incremental overload prunes any subtree that is reference-identical to the one it
    /// replaced, on the grounds that it was valid a moment ago and has not been touched. That is
    /// a shortcut through a <em>safety</em> check, so the only acceptable evidence is that it
    /// answers exactly what the exhaustive walk answers. The full walk is therefore the oracle,
    /// which is legitimate here for the same reason it is legitimate for a cache: the contract
    /// is equivalence.
    /// </para>
    /// <para>
    /// The cases deliberately include the ones where pruning could go wrong: an edit that leaves
    /// most of the tree shared, an edit that introduces a violation, an edit that shifts sibling
    /// indices so the pairing goes out of step, and an edit that breaks a rule spanning several
    /// nodes rather than one node's own cardinality.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Incremental_validation_agrees_with_the_full_walk(int shape)
    {
        var previous = new DocumentNode(
        [
            ParagraphNode.FromText("first"),
            new ListNode([new ListItemNode([ParagraphNode.FromText("item")])], ListKind.Unordered),
            new TableNode(
            [
                new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            ]),
            ParagraphNode.FromText("last"),
        ]);

        // Every previous document here must itself be valid, or the precondition does not hold
        // and the comparison would be meaningless rather than merely failing.
        Assert.Empty(DocumentSchema.Validate(previous));

        var blocks = previous.Blocks;

        var updated = shape switch
        {
            // An ordinary text edit: everything but one paragraph stays shared.
            0 => previous.WithChildren([ParagraphNode.FromText("first!"), blocks[1], blocks[2], blocks[3]]),

            // An edit that empties a list, which is a violation the pruned walk must still find.
            1 => previous.WithChildren([blocks[0], new ListNode([], ListKind.Unordered), blocks[2], blocks[3]]),

            // A block removed, so every later sibling shifts and the index pairing goes out of step.
            2 => previous.WithChildren([blocks[0], blocks[2], blocks[3]]),

            // A block inserted, shifting the other way.
            3 => previous.WithChildren([blocks[0], ParagraphNode.FromText("new"), blocks[1], blocks[2], blocks[3]]),

            // A rule that spans nodes rather than one node's own cardinality: the second row is
            // narrower than the first, so the grid no longer tiles.
            4 => previous.WithChildren(
            [
                blocks[0],
                blocks[1],
                new TableNode(
                [
                    new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
                    new TableRowNode([TableCellNode.FromText("c")]),
                ]),
                blocks[3],
            ]),

            // Nothing changed at all: the whole document is shared and nothing should be walked.
            _ => previous,
        };

        // Compared as sequences, deliberately. ImmutableArray<T>.Equals is reference equality on
        // the underlying array, so Assert.Equal on the arrays themselves compares identity: the
        // empty cases would pass because ImmutableArray<T>.Empty is a singleton, and every case
        // that actually found a violation would fail however right the answer was.
        Assert.Equal(
            DocumentSchema.Validate(updated).AsEnumerable(),
            DocumentSchema.Validate(updated, previous).AsEnumerable());
    }

    /// <summary>A step still refuses to produce an invalid document.</summary>
    /// <remarks>
    /// The end-to-end statement of the guarantee the incremental walk must not weaken. Emptying
    /// a list through a step has to fail, however little of the document the step examined.
    /// </remarks>
    [Fact]
    public void A_step_that_would_empty_a_list_still_fails()
    {
        var doc = new DocumentNode(
        [
            ParagraphNode.FromText("before"),
            new ListNode([new ListItemNode([ParagraphNode.FromText("only")])], ListKind.Unordered),
        ]);

        Assert.Empty(DocumentSchema.Validate(doc));

        var emptied = doc.WithChildren([doc.Blocks[0], new ListNode([], ListKind.Unordered)]);

        Assert.NotEmpty(DocumentSchema.Validate(emptied, doc));
    }

    [Fact]
    public void A_rectangular_table_is_valid()
    {
        var table = new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]);

        Assert.True(DocumentSchema.IsValid(table));
    }

    [Fact]
    public void A_ragged_table_is_invalid()
    {
        var table = new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c")]),
        ]);

        var violations = DocumentSchema.Validate(table);

        Assert.Single(violations);
        Assert.Contains("No cell covers column 1", violations[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_merged_cell_counts_as_the_columns_it_covers()
    {
        // Measuring cells rather than spans would report this correct table as ragged.
        var table = new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2)]),
        ]);

        Assert.True(DocumentSchema.IsValid(table));
    }

    [Fact]
    public void A_cell_spanning_rows_fills_the_row_below_it()
    {
        // The second row carries one cell because the first row's cell is still occupying
        // column 0. Summing column spans per row would call this ragged; it is not.
        var table = new TableNode(
        [
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                 TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c")]),
        ]);

        Assert.True(DocumentSchema.IsValid(table));
    }

    [Fact]
    public void A_row_that_overflows_past_a_spanning_cell_is_invalid()
    {
        // Only one column is free in the second row, so the extra cell widens the table to
        // three and leaves the first row a column short.
        var table = new TableNode(
        [
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                 TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]);

        Assert.False(DocumentSchema.IsValid(table));
    }

    [Fact]
    public void A_row_span_may_not_hang_off_the_bottom_of_the_table()
    {
        var table = new TableNode(
        [
            new TableRowNode(
                [new TableCellNode([ParagraphNode.FromText("tall")], rowSpan: 2),
                 TableCellNode.FromText("b")]),
        ]);

        var violations = DocumentSchema.Validate(table);

        Assert.Contains(
            violations, v => v.Message.Contains("only 1 row", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cell_is_never_placed_on_top_of_one_that_is_already_there()
    {
        // Columns 0 and 2 are held by cells spanning down from the first row, so the wide
        // cell in the second row has no room and must not be allowed to overwrite them.
        var table = new TableNode(
        [
            new TableRowNode(
            [
                new TableCellNode([ParagraphNode.FromText("l")], rowSpan: 2),
                TableCellNode.FromText("m"),
                new TableCellNode([ParagraphNode.FromText("r")], rowSpan: 2),
            ]),
            new TableRowNode([new TableCellNode([ParagraphNode.FromText("wide")], columnSpan: 2)]),
        ]);

        var grid = TableGrid.Build(0, table);
        var right = grid.At(0, 2)!.Value.Pos;

        Assert.Equal(right, grid.At(1, 2)!.Value.Pos);
        Assert.False(DocumentSchema.IsValid(table));
    }

    [Fact]
    public void Column_widths_must_match_the_column_count()
    {
        var rows = new[]
        {
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
        };

        Assert.True(DocumentSchema.IsValid(new TableNode(rows, [10d, 20d])));
        Assert.True(DocumentSchema.IsValid(new TableNode(rows)));

        var violations = DocumentSchema.Validate(new TableNode(rows, [10d]));
        Assert.Single(violations);
        Assert.Contains("1 column width(s)", violations[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deeply_nested_valid_document_passes()
    {
        var doc = new DocumentNode(
        [
            new TableNode(
            [
                new TableRowNode(
                [
                    new TableCellNode(
                    [
                        new ListNode([ListItemNode.FromText("nested")], ListKind.Ordered, 3),
                        ParagraphNode.FromText("after"),
                    ]),
                ]),
            ]),
            RuleNode.Instance,
        ]);

        Assert.True(DocumentSchema.IsValid(doc));
    }
}
