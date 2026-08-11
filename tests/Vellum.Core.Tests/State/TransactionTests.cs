namespace Vellum.Tests.State;

/// <summary>Assembling and applying transactions.</summary>
public class TransactionTests
{
    private static EditorState Start() =>
        EditorState.Create(new DocumentNode(
        [
            ParagraphNode.FromText("hello"),
            ParagraphNode.FromText("world"),
        ]));

    /// <summary>A paragraph followed by a list, so that structural mismatches are reachable.</summary>
    private static EditorState Nested() =>
        EditorState.Create(new DocumentNode(
        [
            ParagraphNode.FromText("hello"),
            new ListNode([new ListItemNode([ParagraphNode.FromText("x")])]),
        ]));

    [Fact]
    public void AFreshStateHasACaretInTheFirstParagraph()
    {
        Assert.Equal(new TextSelection(1, 1), Start().Selection);
        Assert.Null(Start().StoredMarks);
    }

    [Fact]
    public void ApplyingATransactionProducesANewState()
    {
        var before = Start();
        var tr = before.Transaction().InsertText(1, "X");
        var after = before.Apply(tr);

        Assert.NotSame(before, after);
        Assert.Equal("hello", ((ParagraphNode)before.Doc.Children[0]).Content.Text);
        Assert.Equal("Xhello", ((ParagraphNode)after.Doc.Children[0]).Content.Text);
    }

    [Fact]
    public void EachStepSeesTheDocumentTheOneBeforeItLeft()
    {
        // Two insertions at the same original position: the second is expressed against the
        // document as it stands, which is what lets commands be composed without arithmetic.
        var before = Start();
        var tr = before.Transaction().InsertText(1, "ab").InsertText(3, "cd");

        Assert.Equal("abcdhello", ((ParagraphNode)tr.Doc.Children[0]).Content.Text);
    }

    [Fact]
    public void DeletingAcrossTwoParagraphsMergesThem()
    {
        // Not a failure: this is what selecting across a paragraph break and pressing Delete
        // is supposed to do.
        var tr = Start().Transaction().Delete(3, 9);

        Assert.True(tr.ChangedDoc);
        Assert.Single(tr.Doc.Children);
        Assert.Equal("heorld", ((ParagraphNode)tr.Doc.Children[0]).Content.Text);
    }

    [Fact]
    public void AStepThatCannotBeAppliedIsRecordedRatherThanThrown()
    {
        // Position 3 is in a paragraph one level deep and 11 is in one three levels deep, so
        // there is no single seam to join and the raw step declines rather than guessing.
        var tr = Nested().Transaction().Delete(3, 11);

        Assert.False(tr.ChangedDoc);
        Assert.Single(tr.Failures);
        Assert.Empty(tr.Steps);
    }

    [Fact]
    public void AFailedStepDoesNotStopTheOnesAfterIt()
    {
        var tr = Nested().Transaction().Delete(3, 11).InsertText(1, "X");

        Assert.Single(tr.Failures);
        Assert.Single(tr.Steps);
        Assert.Equal("Xhello", ((ParagraphNode)tr.Doc.Children[0]).Content.Text);
    }

    [Fact]
    public void TheSelectionFollowsTheEditWhenItIsNotSetExplicitly()
    {
        var before = EditorState.Create(
            new DocumentNode([ParagraphNode.FromText("hello")]),
            new TextSelection(3, 3));

        var after = before.Apply(before.Transaction().InsertText(1, "abc"));

        Assert.Equal(new TextSelection(6, 6), after.Selection);
    }

    [Fact]
    public void AnExplicitSelectionWins()
    {
        var before = Start();
        var tr = before.Transaction().InsertText(1, "abc").SetSelection(new TextSelection(2, 2));

        Assert.Equal(new TextSelection(2, 2), before.Apply(tr).Selection);
    }

    [Fact]
    public void TheMappingCoversEveryStep()
    {
        var tr = Start().Transaction().InsertText(1, "ab").InsertText(1, "cd");

        Assert.Equal(2, tr.Mapping.Count);
        Assert.Equal(9, tr.Mapping.Map(5));
    }

    [Fact]
    public void ATransactionFromAnotherStateIsRefused()
    {
        var a = Start();
        var b = Start();

        Assert.Throws<ArgumentException>(() => b.Apply(a.Transaction().InsertText(1, "X")));
    }

    [Fact]
    public void AnEmptyTransactionIsHarmless()
    {
        var before = Start();
        var after = before.Apply(before.Transaction());

        Assert.Same(before.Doc, after.Doc);
        Assert.Equal(before.Selection, after.Selection);
    }

    [Fact]
    public void ArmedFormattingSurvivesATransactionThatChangesNothing()
    {
        var before = Start();
        var armed = before.Apply(
            before.Transaction().SetStoredMarks(new MarkSet { Style = TextStyle.Bold }));

        Assert.NotNull(armed.StoredMarks);

        var still = armed.Apply(armed.Transaction().SetSelection(new TextSelection(2, 2)));

        Assert.NotNull(still.StoredMarks);
    }

    [Fact]
    public void ArmedFormattingIsSpentTheMomentSomethingIsTyped()
    {
        var before = Start();
        var armed = before.Apply(
            before.Transaction().SetStoredMarks(new MarkSet { Style = TextStyle.Bold }));

        var typed = armed.Apply(
            armed.Transaction().InsertText(1, "X", armed.StoredMarks!.Value));

        Assert.Null(typed.StoredMarks);
        Assert.True(((ParagraphNode)typed.Doc.Children[0]).Content.MarkAt(0).Has(TextStyle.Bold));
    }

    [Fact]
    public void InvertingATransactionUndoesItExactly()
    {
        var before = Start();
        var tr = before.Transaction()
            .InsertText(1, "abc")
            .Delete(12, 14)
            .Step(new AddMarkStep(1, 4, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold));

        Assert.Equal(3, tr.Steps.Length);

        var doc = tr.Doc;

        foreach (var step in tr.Invert())
        {
            var result = step.Apply(doc);

            Assert.True(result.IsOk, result.Failure);
            doc = result.Doc!;
        }

        Assert.Equal(before.Doc, doc);
    }

    [Fact]
    public void InvertingIsInReverseOrder()
    {
        // Inverting in order would apply the undo of the first step to a document that still has
        // the second step's changes in it, which is a different document than it was built for.
        var before = Start();
        var tr = before.Transaction().InsertText(1, "ab").InsertText(1, "cd");
        var inverted = tr.Invert();

        var doc = inverted[0].Apply(tr.Doc).Doc!;
        doc = inverted[1].Apply(doc).Doc!;

        Assert.Equal(before.Doc, doc);
    }

    [Fact]
    public void TheKindAndTimeAreCarried()
    {
        var when = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var tr = Start().Transaction().InsertText(1, "X").As(TransactionKind.Typing).At(when);

        Assert.Equal(TransactionKind.Typing, tr.Kind);
        Assert.Equal(when, tr.Time);
    }

    [Fact]
    public void ASelectionWhoseParagraphIsDeletedStillLandsSomewhereValid()
    {
        var before = EditorState.Create(
            new DocumentNode([ParagraphNode.FromText("hello"), ParagraphNode.FromText("world")]),
            new TextSelection(2, 4));

        var after = before.Apply(before.Transaction().Delete(1, 6));

        Assert.InRange(after.Selection.Head, 0, after.Doc.ContentSize);
        Assert.True(after.Doc.Resolve(after.Selection.Head).IsInText);
    }
}
