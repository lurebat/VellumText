namespace VellumText.Tests.State;

/// <summary>Which state a state was applied from, and how positions moved getting there.</summary>
public class StateLineageTests
{
    [Fact]
    public void ApplyingATransactionRecordsHowItMovedPositions()
    {
        var before = EditorState.Create(new DocumentNode([ParagraphNode.FromText("hello world")]));
        var tr = before.Transaction();

        tr.InsertText(1, "say ");

        var after = before.Apply(tr);
        var mapping = StateLineage.MappingFrom(before, after);

        Assert.NotNull(mapping);

        // "world" started at 7 and now starts four positions later.
        Assert.Equal(11, mapping.Map(7));
    }

    [Fact]
    public void AStateNotAppliedFromAnotherHasNoMappingFromIt()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("hello")]);
        var a = EditorState.Create(doc);
        var b = EditorState.Create(doc);
        var tr = a.Transaction();

        tr.InsertText(1, "x");

        var applied = a.Apply(tr);

        Assert.Null(StateLineage.MappingFrom(b, applied));
        Assert.Null(StateLineage.MappingFrom(applied, a));
        Assert.Null(StateLineage.MappingFrom(a, b));
    }

    [Fact]
    public void UndoAndRedoRecordHowTheyMovedPositions()
    {
        var state = EditorState.Create(new DocumentNode([ParagraphNode.FromText("hello world")]));
        var tr = state.Transaction();

        tr.InsertText(1, "say ");

        var history = History.Empty.Record(tr);
        var typed = state.Apply(tr);
        var (undone, afterUndo) = history.Undo(typed);

        Assert.Equal(7, StateLineage.MappingFrom(typed, undone)?.Map(11));

        var (redone, _) = afterUndo.Redo(undone);

        Assert.Equal(11, StateLineage.MappingFrom(undone, redone)?.Map(7));
    }

    /// <summary>
    /// The lineage is kept off the state, so two states that hold the same things still compare
    /// equal and hash alike however they came about.
    /// </summary>
    [Fact]
    public void StatesThatHoldTheSameThingsStayEqualWhateverTheirLineage()
    {
        var doc = new DocumentNode([ParagraphNode.FromText("hello")]);
        var start = EditorState.Create(doc);
        var selection = TextSelection.Cursor(3);

        var first = start.Apply(start.Transaction().SetSelection(selection));
        var second = start.Apply(start.Transaction().SetSelection(selection));
        var created = EditorState.Create(doc, selection);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.Equal(created, first);
        Assert.Equal(created.GetHashCode(), first.GetHashCode());
    }
}
