namespace Vellum.Tests.State;

/// <summary>
/// Deleting a range whose two ends are at different depths.
/// </summary>
/// <remarks>
/// <para>
/// One <see cref="ReplaceStep"/> cannot express this and correctly refuses to try, and the
/// refusal used to reach the user as a select-all that could not be typed over, a Backspace that
/// did nothing and a paste that vanished. What is asserted here is the shape of the document
/// afterwards, because the interesting part is not that something was removed but that the tree
/// closed back up over the hole.
/// </para>
/// <para>
/// The first block's structure is what survives: selecting from inside a bullet into a paragraph
/// and deleting leaves a bullet, not a paragraph. That is a choice, and it is the one every
/// editor a user has met makes.
/// </para>
/// </remarks>
public class DeleteRangeTests
{
    private static DocumentNode ListThenParagraph() =>
        new([
            new ListNode([new ListItemNode([ParagraphNode.FromText("ab")])]),
            ParagraphNode.FromText("cd"),
        ]);

    private static DocumentNode Apply(DocumentNode doc, int from, int to)
    {
        var state = EditorState.Create(doc);
        var transaction = state.Transaction();

        transaction.DeleteRange(from, to);

        Assert.Empty(transaction.Failures);

        return state.Apply(transaction).Doc;
    }

    [Fact]
    public void ARangeRunningOutOfAListIntoAParagraphIsRemoved()
    {
        // The whole document: from the start of "ab" to the end of "cd".
        var doc = Apply(ListThenParagraph(), 3, 11);

        var list = Assert.IsType<ListNode>(Assert.Single(doc.Blocks));
        var item = Assert.IsType<ListItemNode>(Assert.Single(list.Items));

        Assert.Equal(string.Empty, ((ParagraphNode)item.Blocks[0]).Content.Text);
    }

    [Fact]
    public void WhatFallsOutsideTheRangeSurvivesOnBothSides()
    {
        // From between "a" and "b" to between "c" and "d": "b" and "c" go, "a" and "d" stay,
        // and the two halves end up in the same paragraph.
        var doc = Apply(ListThenParagraph(), 4, 10);

        var list = Assert.IsType<ListNode>(Assert.Single(doc.Blocks));
        var item = Assert.IsType<ListItemNode>(Assert.Single(list.Items));

        Assert.Equal("ad", ((ParagraphNode)item.Blocks[0]).Content.Text);
    }

    [Fact]
    public void AParagraphRunningIntoAListKeepsBeingAParagraph()
    {
        var doc = Apply(
            new DocumentNode([
                ParagraphNode.FromText("cd"),
                new ListNode([new ListItemNode([ParagraphNode.FromText("ab")])]),
            ]),
            2,
            8);

        var paragraph = Assert.IsType<ParagraphNode>(Assert.Single(doc.Blocks));

        Assert.Equal("cb", paragraph.Content.Text);
    }

    [Fact]
    public void ASiblingTheRangeNeverReachedIsNotTakenWithIt()
    {
        // The end of the range is inside the first of two bullets. Removing the block it sits in
        // must not take the second one, which the range never touched.
        var doc = Apply(
            new DocumentNode([
                ParagraphNode.FromText("cd"),
                new ListNode([
                    new ListItemNode([ParagraphNode.FromText("ab")]),
                    new ListItemNode([ParagraphNode.FromText("ef")]),
                ]),
            ]),
            2,
            8);

        Assert.Equal(2, doc.Blocks.Length);

        var list = Assert.IsType<ListNode>(doc.Blocks[1]);

        Assert.Equal("ef", ((ParagraphNode)Assert.Single(list.Items).Blocks[0]).Content.Text);
    }

    [Fact]
    public void WholeBlocksBetweenTheTwoEndsGoTo()
    {
        var doc = Apply(
            new DocumentNode([
                new ListNode([new ListItemNode([ParagraphNode.FromText("ab")])]),
                ParagraphNode.FromText("middle"),
                ParagraphNode.FromText("cd"),
            ]),
            4,
            18);

        var list = Assert.IsType<ListNode>(Assert.Single(doc.Blocks));

        Assert.Equal("ad", ((ParagraphNode)Assert.Single(list.Items).Blocks[0]).Content.Text);
    }

    [Fact]
    public void ARangeWhoseEndsAreAtTheSameDepthIsStillAnOrdinaryDelete()
    {
        var doc = Apply(
            new DocumentNode([ParagraphNode.FromText("ab"), ParagraphNode.FromText("cd")]),
            2,
            6);

        Assert.Equal("ad", ((ParagraphNode)Assert.Single(doc.Blocks)).Content.Text);
    }

    [Fact]
    public void DeletingACrossDepthRangeCanBeUndoneExactly()
    {
        // Four steps go on the stack instead of one, so this is the assertion that they compose
        // into a single reversible edit rather than four separate ones.
        var before = ListThenParagraph();
        var state = EditorState.Create(before);
        var transaction = state.Transaction();

        transaction.DeleteRange(4, 10);

        var after = state.Apply(transaction);
        var undo = after.Transaction();

        foreach (var step in transaction.Invert())
        {
            undo.Step(step);
        }

        Assert.Empty(undo.Failures);
        Assert.Equal(before, after.Apply(undo).Doc);
    }
}