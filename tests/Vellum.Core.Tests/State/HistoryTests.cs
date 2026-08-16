namespace Vellum.Tests.State;

/// <summary>Recording, grouping, undoing and redoing edits.</summary>
public class HistoryTests
{
    private static readonly DateTimeOffset T0 = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>"hello" at 1..6, "world" at 8..13.</summary>
    private static EditorState Start() =>
        EditorState.Create(new DocumentNode(
        [
            ParagraphNode.FromText("hello"),
            ParagraphNode.FromText("world"),
        ]));

    /// <summary>A state, its history, and the two operations that keep them together.</summary>
    private sealed class Editor
    {
        public Editor(HistoryPolicy? policy = null)
        {
            State = Start();
            History = policy is null ? History.Empty : History.With(policy.Value);
        }

        public EditorState State { get; private set; }

        public History History { get; set; }

        public DocumentNode Doc => State.Doc;

        public string Text(int block) => ((ParagraphNode)State.Doc.Children[block]).Content.Text;

        public Transaction Begin(TransactionKind kind, TimeSpan at) =>
            State.Transaction().As(kind).At(T0 + at);

        public Editor Commit(Transaction tr)
        {
            History = History.Record(tr);
            State = State.Apply(tr);

            return this;
        }

        public Editor Type(int pos, string text, TimeSpan at)
        {
            Commit(Begin(TransactionKind.Typing, at).InsertText(pos, text));

            return this;
        }

        public Editor Backspace(int pos, TimeSpan at)
        {
            Commit(Begin(TransactionKind.Delete, at).Delete(pos - 1, pos));

            return this;
        }

        public Editor Select(Vellum.Selection selection)
        {
            Commit(State.Transaction().SetSelection(selection));

            return this;
        }

        public Editor Undo()
        {
            (State, History) = History.Undo(State);

            return this;
        }

        public Editor Redo()
        {
            (State, History) = History.Redo(State);

            return this;
        }
    }

    [Fact]
    public void UndoRestoresTheDocumentExactly()
    {
        var editor = new Editor();
        var before = editor.Doc;

        editor.Type(1, "X", TimeSpan.Zero);

        Assert.Equal("Xhello", editor.Text(0));

        editor.Undo();

        Assert.Equal(before, editor.Doc);
    }

    [Fact]
    public void RedoRestoresTheLaterDocumentExactly()
    {
        var editor = new Editor().Type(1, "X", TimeSpan.Zero);
        var after = editor.Doc;

        editor.Undo().Redo();

        Assert.Equal(after, editor.Doc);
    }

    [Fact]
    public void UndoingAndRedoingRepeatedlyStaysExact()
    {
        var editor = new Editor();
        var before = editor.Doc;

        editor.Type(1, "X", TimeSpan.Zero);

        var after = editor.Doc;

        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(before, editor.Undo().Doc);
            Assert.Equal(after, editor.Redo().Doc);
        }
    }

    [Fact]
    public void UndoAndRedoMoveTheEventBetweenTheStacks()
    {
        var editor = new Editor().Type(1, "X", TimeSpan.Zero);

        Assert.Equal(1, editor.History.UndoDepth);
        Assert.Equal(0, editor.History.RedoDepth);
        Assert.True(editor.History.CanUndo);
        Assert.False(editor.History.CanRedo);

        editor.Undo();

        Assert.Equal(0, editor.History.UndoDepth);
        Assert.Equal(1, editor.History.RedoDepth);
        Assert.False(editor.History.CanUndo);
        Assert.True(editor.History.CanRedo);
    }

    [Fact]
    public void AnOrdinaryEditClearsTheRedoStack()
    {
        // The recorded way forward describes a document that no longer exists.
        var editor = new Editor()
            .Type(1, "X", TimeSpan.Zero)
            .Undo();

        Assert.Equal(1, editor.History.RedoDepth);

        editor.Type(1, "Y", TimeSpan.FromSeconds(10));

        Assert.Equal(0, editor.History.RedoDepth);
        Assert.Equal("Yhello", editor.Text(0));
    }

    [Fact]
    public void UndoOnAnEmptyStackIsANoOp()
    {
        var state = Start();
        var (after, history) = History.Empty.Undo(state);

        Assert.Same(state, after);
        Assert.Same(History.Empty, history);
    }

    [Fact]
    public void RedoOnAnEmptyStackIsANoOp()
    {
        var state = Start();
        var (after, history) = History.Empty.Redo(state);

        Assert.Same(state, after);
        Assert.Same(History.Empty, history);
    }

    [Fact]
    public void ATransactionThatChangedNothingIsNotRecorded()
    {
        // Moving the caret is not an edit. Recording it would make Ctrl+Z do nothing at all.
        var editor = new Editor().Select(new TextSelection(3, 3));

        Assert.Equal(0, editor.History.UndoDepth);
        Assert.Equal(new TextSelection(3, 3), editor.State.Selection);
    }

    [Fact]
    public void TypingInsideTheWindowUndoesAsOneStep()
    {
        var editor = new Editor()
            .Type(1, "a", TimeSpan.Zero)
            .Type(2, "b", TimeSpan.FromMilliseconds(100))
            .Type(3, "c", TimeSpan.FromMilliseconds(200));

        Assert.Equal("abchello", editor.Text(0));
        Assert.Equal(1, editor.History.UndoDepth);

        editor.Undo();

        Assert.Equal("hello", editor.Text(0));
        Assert.Equal(0, editor.History.UndoDepth);
    }

    [Fact]
    public void TypingOutsideTheWindowStartsANewStep()
    {
        var editor = new Editor()
            .Type(1, "a", TimeSpan.Zero)
            .Type(2, "b", TimeSpan.FromMilliseconds(600));

        Assert.Equal(2, editor.History.UndoDepth);

        editor.Undo();

        Assert.Equal("ahello", editor.Text(0));
    }

    [Fact]
    public void BackspaceCoalescesEvenThoughItMovesWhatCameBefore()
    {
        // Each deletion drags the previous one's range backwards. Comparing raw numbers would
        // find them a character apart every time and never group.
        var editor = new Editor()
            .Backspace(6, TimeSpan.Zero)
            .Backspace(5, TimeSpan.FromMilliseconds(100))
            .Backspace(4, TimeSpan.FromMilliseconds(200));

        Assert.Equal("he", editor.Text(0));
        Assert.Equal(1, editor.History.UndoDepth);

        editor.Undo();

        Assert.Equal("hello", editor.Text(0));
    }

    [Fact]
    public void AnEditExactlyOnTheWindowStillJoins()
    {
        // The window is how long after an edit a following one may still join it, so an edit at
        // exactly that distance is inside it. Getting this backwards splits a run at half a
        // second on the nose, which is precisely where a steady typist keeps landing.
        var editor = new Editor()
            .Type(6, "!", TimeSpan.Zero)
            .Type(7, "?", TimeSpan.FromMilliseconds(500));

        Assert.Equal("hello!?", editor.Text(0));
        Assert.Equal(1, editor.History.UndoDepth);
    }

    [Fact]
    public void AnEditStampedBeforeTheOneItFollowsDoesNotJoinIt()
    {
        // Clocks go backwards - a machine syncs time, or a caller stamps transactions itself.
        // Subtracting then gives a negative gap, which is inside any window, so without an
        // explicit check two edits arbitrarily far apart in time would be grouped.
        var editor = new Editor()
            .Type(6, "!", TimeSpan.FromMilliseconds(400))
            .Type(7, "?", TimeSpan.FromMilliseconds(100));

        Assert.Equal("hello!?", editor.Text(0));
        Assert.Equal(2, editor.History.UndoDepth);
    }

    [Fact]
    public void AMultiStepEditIsRememberedWhereItsStepsEndedUp()    {
        // Two insertions in one transaction: the second happens before the first and pushes it
        // along. The range that reaches history has to be where the text is now, not where the
        // first step found it, or the next keystroke lands outside it and starts a new entry.
        var editor = new Editor();

        editor.Commit(editor.Begin(TransactionKind.Typing, TimeSpan.Zero)
            .InsertText(5, "AB")
            .InsertText(1, "CD"));

        Assert.Equal("CDhellABo", editor.Text(0));

        editor.Type(9, "!", TimeSpan.FromMilliseconds(100));

        Assert.Equal("CDhellAB!o", editor.Text(0));
        Assert.Equal(1, editor.History.UndoDepth);

        editor.Undo();

        Assert.Equal("hello", editor.Text(0));
    }

    [Fact]
    public void ASelectionJumpBetweenTwoEditsPreventsCoalescing()
    {
        // Both are typing, both are inside the window, and the ranges touch. Only the caret
        // having been moved in between separates them.
        var editor = new Editor()
            .Type(1, "a", TimeSpan.Zero)
            .Select(new TextSelection(4, 4))
            .Type(2, "b", TimeSpan.FromMilliseconds(100));

        Assert.Equal(2, editor.History.UndoDepth);
    }

    [Fact]
    public void WithoutTheJumpTheSameTwoEditsDoCoalesce()
    {
        // The control for the test above: everything else about it groups, so the jump is what
        // did the work there.
        var editor = new Editor()
            .Type(1, "a", TimeSpan.Zero)
            .Type(2, "b", TimeSpan.FromMilliseconds(100));

        Assert.Equal(1, editor.History.UndoDepth);
    }

    [Fact]
    public void AnEditSomewhereElseStartsANewStep()
    {
        // Same kind, same window, and the caret was never moved - but the second edit is in the
        // other paragraph, so undoing them together would move text the reader never saw move.
        var editor = new Editor()
            .Type(1, "a", TimeSpan.Zero)
            .Type(9, "b", TimeSpan.FromMilliseconds(100));

        Assert.Equal(2, editor.History.UndoDepth);
    }

    [Theory]
    [InlineData(TransactionKind.Structure)]
    [InlineData(TransactionKind.Paste)]
    public void SomeKindsAreLandmarksAndNeverCoalesce(TransactionKind kind)
    {
        // A reader undoing past a paste expects to arrive exactly there, not inside it.
        var editor = new Editor();

        editor.Commit(editor.Begin(kind, TimeSpan.Zero).InsertText(1, "a"));
        editor.Commit(editor.Begin(kind, TimeSpan.FromMilliseconds(100)).InsertText(2, "b"));

        Assert.Equal(2, editor.History.UndoDepth);
    }

    [Fact]
    public void TypingAndDeletingDoNotCoalesceWithEachOther()
    {
        var editor = new Editor()
            .Type(1, "a", TimeSpan.Zero)
            .Backspace(2, TimeSpan.FromMilliseconds(100));

        Assert.Equal(2, editor.History.UndoDepth);
        Assert.Equal("hello", editor.Text(0));
    }

    [Fact]
    public void TheNeverPolicyMakesEveryTransactionItsOwnStep()
    {
        var editor = new Editor(HistoryPolicy.Never)
            .Type(1, "a", TimeSpan.Zero)
            .Type(2, "b", TimeSpan.FromMilliseconds(10))
            .Type(3, "c", TimeSpan.FromMilliseconds(20));

        Assert.Equal(3, editor.History.UndoDepth);

        editor.Undo();

        Assert.Equal("abhello", editor.Text(0));
    }

    [Fact]
    public void CoalescedStepsUndoInTheOrderTheyHappened()
    {
        // The steps recorded last have to undo first, or they are applied to positions that the
        // earlier ones have already moved.
        var editor = new Editor()
            .Type(1, "abc", TimeSpan.Zero)
            .Type(4, "de", TimeSpan.FromMilliseconds(50))
            .Type(6, "f", TimeSpan.FromMilliseconds(100));

        Assert.Equal("abcdefhello", editor.Text(0));
        Assert.Equal(3, editor.History.Pending!.Undo.Length);

        editor.Undo();

        Assert.Equal("hello", editor.Text(0));
    }

    [Fact]
    public void UndoRestoresTheSelectionTheEditStartedFrom()
    {
        // Undo puts the reader back where they were working. That is not something the steps
        // carry, so it has to be recorded alongside them.
        var editor = new Editor().Select(new TextSelection(1, 4));

        editor.Commit(editor.Begin(TransactionKind.Paste, TimeSpan.Zero)
            .Delete(1, 4)
            .InsertText(1, "WXYZ"));

        Assert.NotEqual(new TextSelection(1, 4), editor.State.Selection);

        editor.Undo();

        Assert.Equal(new TextSelection(1, 4), editor.State.Selection);
    }

    [Fact]
    public void RedoRestoresTheSelectionTheEditEndedAt()
    {
        var editor = new Editor().Select(new TextSelection(1, 4));

        editor.Commit(editor.Begin(TransactionKind.Paste, TimeSpan.Zero)
            .Delete(1, 4)
            .InsertText(1, "WXYZ"));

        var after = editor.State.Selection;

        editor.Undo().Redo();

        Assert.Equal(after, editor.State.Selection);
    }

    [Fact]
    public void ReplayIsNotItselfRecorded()
    {
        // Otherwise Ctrl+Z would undo itself and nothing would ever move.
        var editor = new Editor().Type(1, "X", TimeSpan.Zero).Undo();

        Assert.Equal(0, editor.History.UndoDepth);
        Assert.Equal(1, editor.History.RedoDepth);
    }

    [Fact]
    public void ATransactionMarkedAsReplayIsNotRecorded()
    {
        // An application funnels everything it applies through Record. Undo and Redo keep the
        // stacks themselves, so anything they produce has to pass straight through.
        var editor = new Editor();

        editor.Commit(editor.Begin(TransactionKind.History, TimeSpan.Zero).InsertText(1, "X"));

        Assert.Equal("Xhello", editor.Text(0));
        Assert.Equal(0, editor.History.UndoDepth);
    }

    [Fact]
    public void UndoAndRedoWalkASequenceOfStepsInOrder()
    {
        var editor = new Editor();
        var snapshots = new List<DocumentNode> { editor.Doc };

        // A second apart, so nothing groups and each edit is its own step.
        for (var i = 0; i < 5; i++)
        {
            editor.Type(1, "X", TimeSpan.FromSeconds(i * 2));
            snapshots.Add(editor.Doc);
        }

        Assert.Equal(5, editor.History.UndoDepth);

        for (var i = 4; i >= 0; i--)
        {
            Assert.Equal(snapshots[i], editor.Undo().Doc);
        }

        for (var i = 1; i <= 5; i++)
        {
            Assert.Equal(snapshots[i], editor.Redo().Doc);
        }
    }

    [Fact]
    public void FormattingUndoesWithoutDisturbingTheDocumentAroundIt()
    {
        var editor = new Editor();
        var before = editor.Doc;

        editor.Commit(editor.Begin(TransactionKind.Format, TimeSpan.Zero)
            .Step(new AddMarkStep(1, 4, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold)));

        Assert.NotEqual(before, editor.Doc);

        editor.Undo();

        Assert.Equal(before, editor.Doc);
    }

    [Fact]
    public void HistoryIsImmutableSoAnOldOneStillWorks()
    {
        var editor = new Editor().Type(1, "a", TimeSpan.Zero);
        var early = editor.History;
        var earlyState = editor.State;

        editor.Type(1, "b", TimeSpan.FromSeconds(5));

        Assert.Equal(2, editor.History.UndoDepth);
        Assert.Equal(1, early.UndoDepth);

        var (undone, _) = early.Undo(earlyState);

        Assert.Equal("hello", ((ParagraphNode)undone.Doc.Children[0]).Content.Text);
    }
    /// <summary>Types one separate undo step per call, spaced beyond the coalescing window.</summary>
    /// <remarks>
    /// Each step inserts a different letter at a different position, so undoing them in the wrong
    /// order produces different text. Repeating one character would make the steps commute, and a
    /// test that cannot tell the orders apart cannot tell a reversed stack from a correct one.
    /// </remarks>
    private static Editor Steps(Editor editor, int count)
    {
        for (var i = 0; i < count; i++)
        {
            editor.Type(1 + i, ((char)('a' + i)).ToString(), TimeSpan.FromSeconds(i));
        }

        return editor;
    }

    [Fact]
    public void AnUnboundedHistoryKeepsEveryStep()
    {
        var editor = Steps(new Editor(), 12);

        Assert.Equal(HistoryPolicy.Unlimited, editor.History.Policy.Limit);
        Assert.Equal(12, editor.History.UndoDepth);
    }

    [Fact]
    public void ALimitedHistoryStopsGrowingAtTheLimit()
    {
        var editor = Steps(new Editor(new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 3)), 10);

        Assert.Equal(3, editor.History.UndoDepth);
    }

    [Fact]
    public void ItIsTheOldestStepThatIsDiscarded()
    {
        var editor = Steps(new Editor(new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 3)), 5);

        Assert.Equal("abcdehello", editor.Text(0));

        // Three undos are all that is left, and they peel off the three most recent edits.
        editor.Undo().Undo().Undo();

        Assert.Equal("abhello", editor.Text(0));

        // The two discarded ones are gone for good, not merely unreachable for the moment.
        Assert.False(editor.History.CanUndo);
        Assert.Equal("abhello", editor.Undo().Text(0));
    }

    [Fact]
    public void RedoStillWorksAfterAStepHasBeenDiscarded()
    {
        var editor = Steps(new Editor(new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 3)), 5);
        var after = editor.Doc;

        editor.Undo().Undo().Undo();

        Assert.Equal(3, editor.History.RedoDepth);
        Assert.Equal(after, editor.Redo().Redo().Redo().Doc);
    }

    [Fact]
    public void ALimitOfZeroRecordsNothing()
    {
        var editor = Steps(new Editor(new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 0)), 4);

        Assert.Equal(0, editor.History.UndoDepth);
        Assert.False(editor.History.CanUndo);
        Assert.Equal("abcdhello", editor.Undo().Text(0));
    }

    [Fact]
    public void SettingALimitOfZeroClearsBothStacks()
    {
        var editor = Steps(new Editor(), 2);

        editor.Undo();

        Assert.Equal(1, editor.History.RedoDepth);

        editor.History = editor.History.WithPolicy(
            new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 0));

        Assert.False(editor.History.CanUndo);
        Assert.False(editor.History.CanRedo);
    }

    [Fact]
    public void LoweringTheLimitTrimsARedoStackTooDeepForIt()
    {
        var editor = Steps(new Editor(), 5);

        editor.Undo().Undo().Undo().Undo().Undo();

        Assert.Equal(5, editor.History.RedoDepth);

        editor.History = editor.History.WithPolicy(
            new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 2));

        Assert.Equal(2, editor.History.RedoDepth);
    }

    [Fact]
    public void CoalescedTypingCostsOneStepNotOnePerCharacter()
    {
        var editor = new Editor(new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 2));

        for (var i = 0; i < 6; i++)
        {
            editor.Type(1 + i, "x", TimeSpan.FromMilliseconds(i * 10));
        }

        Assert.Equal(1, editor.History.UndoDepth);
        Assert.Equal("hello", editor.Undo().Text(0));
    }

    [Fact]
    public void SettingALimitTrimsTheStackImmediately()
    {
        var editor = Steps(new Editor(), 5);

        editor.History = editor.History.WithPolicy(
            new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: 2));

        Assert.Equal(2, editor.History.UndoDepth);

        // And it is the newest two that survived.
        Assert.Equal("abchello", editor.Undo().Undo().Text(0));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(int.MinValue)]
    public void ALimitBelowUnlimitedIsRefused(int limit)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => History.With(new HistoryPolicy(TimeSpan.FromMilliseconds(500), Limit: limit)));
    }
}
