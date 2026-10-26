namespace VellumText.Tests.State;

/// <summary>Recording, grouping, undoing and redoing edits.</summary>
public class HistoryTests
{
    /// <summary>
    /// Two deliberate commands are two undos, even when they touch the same range in quick
    /// succession.
    /// </summary>
    /// <remarks>
    /// Coalescing used to be a block-list — everything folded together except structure and
    /// paste — so bolding a word and then italicising it became a single history event and one
    /// undo removed both. Typing is the case the feature exists for, because a burst of
    /// keystrokes is one action in the user's head; a command they issued twice is two.
    /// </remarks>
    [Fact]
    public void TwoFormattingCommandsAreTwoUndos()
    {
        var state = EditorState.Create(new DocumentNode([ParagraphNode.FromText("hello")]));
        var history = History.Empty;
        var at = DateTime.UtcNow;

        var bold = state.Transaction().As(TransactionKind.Format).At(at)
            .Step(new AddMarkStep(1, 4, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold));

        history = history.Record(bold);
        state = state.Apply(bold);

        // Well inside the coalescing window, which is what made them fold together before.
        var italic = state.Transaction().As(TransactionKind.Format).At(at.AddMilliseconds(100))
            .Step(new AddMarkStep(1, 4, new MarkSet { Style = TextStyle.Italic }, MarkFields.Italic));

        history = history.Record(italic);
        state = state.Apply(italic);

        Assert.Equal(2, history.UndoDepth);

        var (undone, _) = history.Undo(state);
        var mark = ((ParagraphNode)undone.Doc.Blocks[0]).Content.MarkAt(1);

        // The italic goes and the bold stays. Asserting the style rather than the depth alone,
        // because a depth of two with the wrong content undone would be just as broken.
        Assert.Equal(TextStyle.Bold, mark.Style);
    }

    /// <summary>Typing still coalesces, which is the behaviour the feature exists for.</summary>
    [Fact]
    public void TypingStillCoalesces()
    {
        var state = EditorState.Create(new DocumentNode([ParagraphNode.FromText("")]));
        var history = History.Empty;
        var at = DateTime.UtcNow;

        for (var i = 0; i < 3; i++)
        {
            var tr = state.Transaction().As(TransactionKind.Typing).At(at.AddMilliseconds(i * 50));

            tr.InsertText(1 + i, "x");
            history = history.Record(tr);
            state = state.Apply(tr);
        }

        Assert.Equal(1, history.UndoDepth);
    }

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

        public Editor Select(VellumText.Selection selection)
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
        // earlier ones have already moved. A paragraph split in the middle keeps the three edits
        // as three steps: text replacements next to each other fold into one, a split does not.
        var editor = new Editor().Type(1, "abc", TimeSpan.Zero);

        editor.Commit(editor.Begin(TransactionKind.Typing, TimeSpan.FromMilliseconds(50))
            .Replace(4, 4, Slice.ParagraphSplit));
        editor.Type(6, "f", TimeSpan.FromMilliseconds(100));

        Assert.Equal("abc", editor.Text(0));
        Assert.Equal("fhello", editor.Text(1));
        Assert.Equal(3, editor.History.Pending!.Undo.Length);

        editor.Undo();

        Assert.Equal("hello", editor.Text(0));
    }

    /// <remarks>
    /// F-12. Each keystroke's inverse used to be kept as its own step, so Ctrl+Z after a sentence
    /// typed without a pause replayed every keystroke — each a rebuild of the document's spine,
    /// 0.6 s for 100 characters at 50,000 paragraphs. Neighbouring text replacements in one
    /// paragraph are one replacement, so that is what is kept.
    /// </remarks>
    [Fact]
    public void ATypingBurstUndoesAndRedoesWithOneStep()
    {
        var editor = new Editor();
        var pos = 1;

        for (var i = 0; i < 40; i++)
        {
            // An astral character now and then: two code units, one keystroke.
            var typed = i % 7 == 0 ? "\U0001F600" : "x";

            editor.Type(pos, typed, TimeSpan.FromMilliseconds(i * 10));
            pos += typed.Length;
        }

        var typedDoc = editor.Doc;

        Assert.Equal(1, editor.History.UndoDepth);
        Assert.Single(editor.History.Pending!.Undo);

        editor.Undo();

        Assert.Equal(Start().Doc, editor.Doc);

        editor.Redo();

        Assert.Equal(typedDoc, editor.Doc);
        Assert.Single(editor.History.Pending!.Undo);
    }

    /// <remarks>
    /// Backspace inverts into insertions that land one before the other; holding it down over
    /// formatted text has to give the formatting back with the characters.
    /// </remarks>
    [Fact]
    public void ABackspaceBurstOverFormattedTextUndoesWithOneStep()
    {
        var editor = new Editor();

        editor.Commit(editor.Begin(TransactionKind.Format, TimeSpan.Zero)
            .Step(new AddMarkStep(2, 4, new MarkSet { Style = TextStyle.Bold }, MarkFields.Bold)));

        var formatted = editor.Doc;

        for (var pos = 6; pos > 1; pos--)
        {
            editor.Backspace(pos, TimeSpan.FromSeconds(1) + TimeSpan.FromMilliseconds(10 * (6 - pos)));
        }

        Assert.Equal(string.Empty, editor.Text(0));
        Assert.Single(editor.History.Pending!.Undo);

        editor.Undo();

        Assert.Equal(formatted, editor.Doc);
    }

    /// <remarks>Forward Delete inverts into insertions that all land at the caret.</remarks>
    [Fact]
    public void AForwardDeleteBurstUndoesWithOneStep()
    {
        var editor = new Editor();

        for (var i = 0; i < 4; i++)
        {
            editor.Commit(editor.Begin(TransactionKind.Delete, TimeSpan.FromMilliseconds(i * 10))
                .Delete(2, 3));
        }

        Assert.Equal("h", editor.Text(0));
        Assert.Single(editor.History.Pending!.Undo);

        editor.Undo();

        Assert.Equal(Start().Doc, editor.Doc);
    }

    /// <remarks>
    /// Folding two steps into one is only shown to mean the same thing for replacements inside
    /// one paragraph's text. A structural step — a split, a join — keeps its own step even when it
    /// is coalesced into a burst of typing, and the text on either side of it is folded no
    /// further than up to it.
    /// </remarks>
    [Fact]
    public void AStructuralStepInABurstIsNotFoldedIntoTheText()
    {
        var editor = new Editor()
            .Type(6, "a", TimeSpan.Zero)
            .Type(7, "b", TimeSpan.FromMilliseconds(10));

        editor.Commit(editor.Begin(TransactionKind.Typing, TimeSpan.FromMilliseconds(20))
            .Replace(8, 8, Slice.ParagraphSplit));
        editor.Type(10, "c", TimeSpan.FromMilliseconds(30))
            .Type(11, "d", TimeSpan.FromMilliseconds(40));

        Assert.Equal("helloab", editor.Text(0));
        Assert.Equal("cd", editor.Text(1));
        Assert.Equal(3, editor.History.Pending!.Undo.Length);

        editor.Undo();

        Assert.Equal(Start().Doc, editor.Doc);
    }

    /// <remarks>
    /// Typing over a selection is a deletion and an insertion in one transaction. The keystrokes
    /// after it fold into its insertion; the deletion stays, because it is not next to them in
    /// the order the steps are undone.
    /// </remarks>
    [Fact]
    public void TypingOverASelectionKeepsTheDeletionAndFoldsTheRest()
    {
        var editor = new Editor();

        editor.Commit(editor.Begin(TransactionKind.Typing, TimeSpan.Zero)
            .Delete(2, 5)
            .InsertText(2, "a"));

        for (var i = 1; i < 5; i++)
        {
            editor.Type(2 + i, "a", TimeSpan.FromMilliseconds(i * 10));
        }

        Assert.Equal("haaaaao", editor.Text(0));
        Assert.Equal(2, editor.History.Pending!.Undo.Length);

        editor.Undo();

        Assert.Equal(Start().Doc, editor.Doc);
    }

    /// <summary>
    /// Random bursts of typing, Backspace and Delete at a moving caret, some of them formatted,
    /// some astral, some crossing a paragraph boundary: every undo lands exactly on the document
    /// its event started from, and redoing all of it lands on the end.
    /// </summary>
    /// <remarks>
    /// The folding has to be invisible except in how many steps are replayed, and the property
    /// tests in <c>ModelPropertyTests</c> rarely produce a burst, because their edits jump about.
    /// These do not jump: each edit starts where the last one left the caret, which is the only
    /// way edits coalesce.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void FoldedBurstsUndoToExactlyWhereEachEventStarted(int seed)
    {
        var random = new Random(seed);
        var editor = new Editor();
        var starts = new List<DocumentNode>();
        var caret = 3;
        var clock = TimeSpan.Zero;
        var bold = new MarkSet { Style = TextStyle.Bold };

        for (var i = 0; i < 300; i++)
        {
            // Mostly inside the window, sometimes a pause that starts a new event.
            clock += TimeSpan.FromMilliseconds(random.Next(10) == 0 ? 900 : 40);

            var tr = editor.State.Transaction().At(T0 + clock);
            var size = editor.Doc.ContentSize;

            if (random.Next(15) == 0)
            {
                caret = editor.Doc.Resolve(random.Next(size + 1)) is { IsInText: true } at
                    ? at.Pos
                    : caret;
            }

            switch (random.Next(4))
            {
                case 0:
                case 1:
                    var text = random.Next(5) == 0 ? "\U0001F600" : ((char)('a' + random.Next(26))).ToString();

                    tr.As(TransactionKind.Typing)
                        .InsertText(caret, text, random.Next(3) == 0 ? bold : MarkSet.Empty);
                    caret += text.Length;
                    break;

                case 2:
                    var back = BackOneCharacter(editor.Doc, caret);

                    tr.As(TransactionKind.Delete).Delete(back, caret);
                    caret = tr.Failures.IsEmpty ? back : caret;
                    break;

                default:
                    tr.As(TransactionKind.Delete).Delete(caret, ForwardOneCharacter(editor.Doc, caret));
                    break;
            }

            if (!tr.ChangedDoc)
            {
                continue;
            }

            tr.SetSelection(TextSelection.Cursor(Math.Min(caret, tr.Doc.ContentSize)));

            var before = editor.Doc;
            var depth = editor.History.UndoDepth;

            editor.Commit(tr);

            if (editor.History.UndoDepth > depth)
            {
                starts.Add(before);
            }

            caret = editor.State.Selection.Head;
        }

        var end = editor.Doc;

        for (var i = starts.Count - 1; i >= 0; i--)
        {
            editor.Undo();
            Assert.Equal(starts[i], editor.Doc);
        }

        while (editor.History.CanRedo)
        {
            editor.Redo();
        }

        Assert.Equal(end, editor.Doc);
    }

    /// <summary>One character back from a position, or the paragraph boundary before it.</summary>
    private static int BackOneCharacter(DocumentNode doc, int pos)
    {
        var at = doc.Resolve(pos);

        if (at.ParentOffset == 0)
        {
            // A Backspace at the start of a paragraph joins it to the one before.
            return Math.Max(0, pos - 2);
        }

        var text = at.Paragraph!.Content.Text;

        return pos - (at.ParentOffset >= 2 && char.IsLowSurrogate(text[at.ParentOffset - 1]) ? 2 : 1);
    }

    /// <summary>One character forward from a position, or the paragraph boundary after it.</summary>
    private static int ForwardOneCharacter(DocumentNode doc, int pos)
    {
        var at = doc.Resolve(pos);
        var text = at.Paragraph!.Content.Text;

        if (at.ParentOffset == text.Length)
        {
            return Math.Min(doc.ContentSize, pos + 2);
        }

        return pos + (char.IsHighSurrogate(text[at.ParentOffset]) ? 2 : 1);
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
