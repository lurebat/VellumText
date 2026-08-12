using System.Text;
using FsCheck;
using FsCheck.Xunit;

namespace Vellum.Tests.State;

/// <summary>
/// The two properties section 8 of the architecture says define "done" for this increment:
/// undo round-trips exactly, and a tracked position agrees with where the content it tracks
/// actually ended up.
/// </summary>
/// <remarks>
/// <para>
/// Random edit scripts are ordinary <c>int[]</c> read four values at a time. That keeps FsCheck's
/// generation and shrinking working out of the box - every array is a valid script, so shrinking
/// never has to produce something the interpreter rejects - and a counterexample arrives as a
/// short list of numbers that reproduces on demand.
/// </para>
/// <para>
/// The scripts are also deliberately allowed to contain edits that cannot be applied. A step that
/// fails is dropped and the run carries on, which is exactly what a command layer will do to it.
/// <see cref="EnoughOfTheGeneratedEditsActuallyApply"/> is what stops that turning the whole file
/// into a test of nothing.
/// </para>
/// <para>
/// Measured, so that "random edits" is not taken on trust. The fixed-seed sample in
/// <see cref="EnoughOfTheGeneratedEditsActuallyApply"/> attempts 4000 edits across eight kinds;
/// 47.1% of them apply, and the least common kind still applies 61 times. The apply rate is
/// below half because the two structural kinds - splitting a paragraph and pasting closed
/// blocks - are pointed at arbitrary positions, and a great many positions are places where a
/// block genuinely cannot go. Those rejections are the model refusing correctly, and the
/// property bodies see them as steps that were dropped.
/// </para>
/// </remarks>
public class ModelPropertyTests
{
    /// <summary>
    /// A character the generated edits never produce, so that finding it in the document is an
    /// independent answer to "where did that position end up".
    /// </summary>
    private const char Marker = '\uE000';

    private const string Alphabet = "abcde";

    /// <summary>
    /// Text the generated edits also insert: an accented letter, a combining mark, a character
    /// outside the basic plane, and a Hebrew pair. Positions count UTF-16 code units, so an
    /// astral character is two of them and a step that cuts one in half produces text that is
    /// not well-formed - which the model refuses, and which the properties must therefore see.
    /// </summary>
    private static readonly string[] Awkward =
        ["\u00e9", "e\u0301", "\U0001F600", "\u05d0\u05d1", "\U0001F468\u200D\U0001F467"];

    /// <summary>
    /// Two levels of nesting and a table, so that positions are exercised against real depth
    /// rather than a flat run of paragraphs. The marker sits in the second paragraph.
    /// </summary>
    private static DocumentNode Seed() => new(
    [
        ParagraphNode.FromText("alpha"),
        ParagraphNode.FromText("be" + Marker + "ta"),
        new ListNode(
        [
            new ListItemNode([ParagraphNode.FromText("one")]),
            new ListItemNode(
            [
                ParagraphNode.FromText("two"),
                new ListNode([new ListItemNode([ParagraphNode.FromText("deep")])]),
            ]),
        ]),
        new TableNode(
        [
            new TableRowNode([TableCellNode.FromText("a"), TableCellNode.FromText("b")]),
            new TableRowNode([TableCellNode.FromText("c"), TableCellNode.FromText("d")]),
        ]),
        ParagraphNode.FromText("omega"),
    ]);

    // ---------------------------------------------------------------- property 1

    [Property(MaxTest = 400)]
    public void UndoingEverythingReturnsTheExactStartingDocument(int[] script)
    {
        var run = Run(script, HistoryPolicy.Never);

        Assert.Equal(run.Start.Doc, run.UndoAll().Doc);
    }

    [Property(MaxTest = 400)]
    public void RedoingEverythingReturnsTheExactFinalDocument(int[] script)
    {
        var run = Run(script, HistoryPolicy.Never);
        var final = run.End.Doc;

        Assert.Equal(final, run.UndoAll().RedoAll().Doc);
    }

    [Property(MaxTest = 400)]
    public void TheSameHoldsWhenEditsAreAllowedToGroup(int[] script)
    {
        // Grouping changes how many events there are, not what undoing them means. If it changed
        // the latter, merging two events' steps would be losing or reordering one of them.
        var run = Run(script, HistoryPolicy.Default);
        var final = run.End.Doc;

        Assert.Equal(run.Start.Doc, run.UndoAll().Doc);
        Assert.Equal(final, run.RedoAll().Doc);
    }

    [Property(MaxTest = 200)]
    public void UndoingPartWayAndRedoingBackAgreesAtEveryPoint(int[] script)
    {
        var run = Run(script, HistoryPolicy.Never);
        var forwards = run.Snapshots;

        var editor = run.End;
        var history = run.History;

        for (var i = forwards.Count - 1; i >= 0; i--)
        {
            Assert.Equal(forwards[i], editor.Doc);
            (editor, history) = history.Undo(editor);
        }

        for (var i = 0; i < forwards.Count; i++)
        {
            (editor, history) = history.Redo(editor);
            Assert.Equal(forwards[i], editor.Doc);
        }
    }

    // ---------------------------------------------------------------- property 2

    [Property(MaxTest = 400)]
    public void ATrackedPositionAgreesWithWhereTheMarkerActuallyIs(int[] script)
    {
        // The comparison is against a walk of the finished document that uses none of the mapping
        // code. Checking a mapped position against another mapped position would only prove the
        // arithmetic is self-consistent.
        var state = EditorState.Create(Seed());
        var before = FindMarker(state.Doc);

        Assert.NotNull(before);

        var head = before.Value;
        var tail = before.Value + 1;
        var alive = true;

        foreach (var op in Ops(script))
        {
            var tr = Build(state, op);

            if (tr is null)
            {
                continue;
            }

            state = state.Apply(tr);
            head = tr.Mapping.Map(head, Assoc.After);
            tail = tr.Mapping.Map(tail, Assoc.Before);

            // The marker is one character wide. Its two ends meeting means it was deleted.
            alive &= tail - head == 1;

            var actual = FindMarker(state.Doc);

            if (alive)
            {
                Assert.Equal(actual, head);
            }
            else
            {
                Assert.Null(actual);
            }
        }
    }

    [Property(MaxTest = 400)]
    public void MappingNeverProducesAPositionOutsideTheDocument(int[] script)
    {
        var state = EditorState.Create(Seed());
        var tracked = Enumerable.Range(0, state.Doc.ContentSize + 1).ToArray();

        foreach (var op in Ops(script))
        {
            var tr = Build(state, op);

            if (tr is null)
            {
                continue;
            }

            state = state.Apply(tr);

            for (var i = 0; i < tracked.Length; i++)
            {
                tracked[i] = tr.Mapping.Map(tracked[i], Assoc.After);
            }

            foreach (var pos in tracked)
            {
                Assert.InRange(pos, 0, state.Doc.ContentSize);
            }
        }
    }

    [Property(MaxTest = 400)]
    public void MappingNeverPutsTwoPositionsOutOfOrder(int[] script)
    {
        // Positions collapse together when what lies between them is deleted, but one must never
        // overtake another - a selection would come back inside out.
        var state = EditorState.Create(Seed());
        var tracked = Enumerable.Range(0, state.Doc.ContentSize + 1).ToArray();

        foreach (var op in Ops(script))
        {
            var tr = Build(state, op);

            if (tr is null)
            {
                continue;
            }

            state = state.Apply(tr);

            for (var i = 0; i < tracked.Length; i++)
            {
                tracked[i] = tr.Mapping.Map(tracked[i], Assoc.After);
            }

            for (var i = 1; i < tracked.Length; i++)
            {
                Assert.True(
                    tracked[i - 1] <= tracked[i],
                    $"position {i - 1} landed at {tracked[i - 1]}, past position {i} at {tracked[i]}");
            }
        }
    }

    [Property(MaxTest = 300)]
    public void EveryPositionAPositionMapsToCanBeResolved(int[] script)
    {
        var state = EditorState.Create(Seed());
        var tracked = Enumerable.Range(0, state.Doc.ContentSize + 1).ToArray();

        foreach (var op in Ops(script))
        {
            var tr = Build(state, op);

            if (tr is null)
            {
                continue;
            }

            state = state.Apply(tr);

            for (var i = 0; i < tracked.Length; i++)
            {
                tracked[i] = tr.Mapping.Map(tracked[i], Assoc.After);

                // Resolving is what every command does with a mapped position first thing.
                var resolved = state.Doc.Resolve(tracked[i]);

                Assert.Equal(tracked[i], resolved.Pos);

                // Echoing the number back proves only that nothing threw. What matters is that
                // the context it came back with actually contains the position: a command reads
                // Parent and ParentOffset and would otherwise be told about the wrong node.
                Assert.InRange(tracked[i], 0, state.Doc.ContentSize);
                Assert.InRange(
                    tracked[i],
                    resolved.Start(resolved.Depth),
                    resolved.End(resolved.Depth));
                Assert.Equal(tracked[i] - resolved.Start(resolved.Depth), resolved.ParentOffset);
            }
        }
    }

    [Property(MaxTest = 300)]
    public void TheSelectionAlwaysSurvivesTheEditsUnderneathIt(int[] script)
    {
        var state = EditorState.Create(Seed());

        foreach (var op in Ops(script))
        {
            var tr = Build(state, op);

            if (tr is null)
            {
                continue;
            }

            state = state.Apply(tr);

            Assert.InRange(state.Selection.From, 0, state.Doc.ContentSize);
            Assert.InRange(state.Selection.To, state.Selection.From, state.Doc.ContentSize);
        }
    }

    [Property(MaxTest = 300)]
    public void TheDocumentIsStillValidAfterEveryEdit(int[] script)
    {
        // Step.Apply is supposed to fail rather than produce a document that breaks the schema.
        var state = EditorState.Create(Seed());

        foreach (var op in Ops(script))
        {
            var tr = Build(state, op);

            if (tr is null)
            {
                continue;
            }

            state = state.Apply(tr);

            var violations = DocumentSchema.Validate(state.Doc);

            Assert.True(
                violations.IsEmpty,
                $"after {op}: {string.Join("; ", violations)}");
        }
    }

    [Property(MaxTest = 400)]
    public void EveryStepsMapAgreesWithTheDocumentThatStepMade(int[] script)
    {
        // A step's map is a promise about the document the step produced. If the two ever
        // disagree by even one position, every position mapped through that step afterwards is
        // wrong, and the step's own inverse will not fit the document it is asked to undo. The
        // disagreement is silent everywhere else, so it is checked here on every generated edit.
        var state = EditorState.Create(Seed());

        foreach (var op in Ops(script))
        {
            var before = state.Doc;
            var tr = Build(state, op);

            if (tr is null)
            {
                continue;
            }

            state = state.Apply(tr);

            var claimed = 0;

            foreach (var map in tr.Mapping.Maps)
            {
                claimed += map.SizeDelta;
            }

            Assert.True(
                claimed == state.Doc.ContentSize - before.ContentSize,
                $"after {op}: maps claim {claimed}, document moved by "
                + $"{state.Doc.ContentSize - before.ContentSize}");
        }
    }

    // ---------------------------------------------------------------- non-vacuity

    [Fact]
    public void EnoughOfTheGeneratedEditsActuallyApply()
    {
        // Everything above tolerates steps that cannot be applied. Without this, a bug that made
        // every step fail would turn the whole file green.
        var random = new Random(20240101);
        var attempted = 0;
        var applied = 0;
        var kinds = new int[8];

        for (var run = 0; run < 400; run++)
        {
            var state = EditorState.Create(Seed());
            var script = new int[40];

            for (var i = 0; i < script.Length; i++)
            {
                script[i] = random.Next(int.MinValue, int.MaxValue);
            }

            foreach (var op in Ops(script))
            {
                attempted++;

                var tr = Build(state, op);

                if (tr is null)
                {
                    continue;
                }

                applied++;
                kinds[op.Kind]++;
                state = state.Apply(tr);
            }
        }

        var ratio = (double)applied / attempted;

        Assert.True(
            ratio > 0.4,
            $"only {applied} of {attempted} generated edits applied ({ratio:P0})");

        for (var kind = 0; kind < kinds.Length; kind++)
        {
            Assert.True(kinds[kind] > 50, $"edit kind {kind} applied only {kinds[kind]} times");
        }
    }

    [Fact]
    public void GeneratedScriptsSometimesDeleteTheMarkerAndSometimesDoNot()
    {
        // The tracked-position property says different things in the two cases. If one of them
        // never happened it would be testing half of what it claims.
        var random = new Random(20240102);
        var survived = 0;
        var deleted = 0;

        for (var run = 0; run < 400; run++)
        {
            var state = EditorState.Create(Seed());
            var script = new int[40];

            for (var i = 0; i < script.Length; i++)
            {
                script[i] = random.Next(int.MinValue, int.MaxValue);
            }

            foreach (var op in Ops(script))
            {
                var tr = Build(state, op);

                if (tr is not null)
                {
                    state = state.Apply(tr);
                }
            }

            if (FindMarker(state.Doc) is null)
            {
                deleted++;
            }
            else
            {
                survived++;
            }
        }

        Assert.True(survived > 20, $"the marker survived only {survived} of 400 runs");
        Assert.True(deleted > 20, $"the marker was deleted in only {deleted} of 400 runs");
    }

    [Fact]
    public void TheMarkerWalkerAgreesWithHandCountedPositions()
    {
        // The walker is what the tracked-position property is measured against, so it is pinned
        // against arithmetic done by hand rather than against the code it is checking.
        //
        //   0 <p> 1 a 2 b 3 </p>       paragraph "ab" occupies 0..4
        //   4 <p> 5 x 6 M 7 y 8 </p>   the marker is the 2nd character, so the position
        //                              immediately before it is 4 + 1 + 1 = 6
        var doc = new DocumentNode(
        [
            ParagraphNode.FromText("ab"),
            ParagraphNode.FromText("x" + Marker + "y"),
        ]);

        Assert.Equal(6, FindMarker(doc));

        //   0 <list> 1 <item> 2 <p> 3 M 4 </p> 5 </item> 6 </list>
        var nested = new DocumentNode(
        [
            new ListNode([new ListItemNode([ParagraphNode.FromText(Marker.ToString())])]),
        ]);

        Assert.Equal(3, FindMarker(nested));
        Assert.Null(FindMarker(new DocumentNode([ParagraphNode.FromText("none")])));
    }

    // ---------------------------------------------------------------- the machinery

    /// <summary>One generated edit, before it has been turned into steps.</summary>
    private readonly record struct Op(int Kind, int A, int B, int C)
    {
        public override string ToString() => $"op({Kind}, {A}, {B}, {C})";
    }

    /// <summary>A run replayed to the end, undone to the start, and left there.</summary>
    private sealed class Session
    {
        private readonly List<DocumentNode> _snapshots = [];

        public Session(HistoryPolicy policy)
        {
            State = EditorState.Create(Seed());
            History = History.With(policy);
            Start = State;
        }

        public EditorState Start { get; }

        public EditorState State { get; private set; }

        public History History { get; private set; }

        public EditorState End => State;

        public DocumentNode Doc => State.Doc;

        public IReadOnlyList<DocumentNode> Snapshots => _snapshots;

        public void Commit(Transaction tr)
        {
            History = History.Record(tr);
            State = State.Apply(tr);
            _snapshots.Add(State.Doc);
        }

        public Session UndoAll()
        {
            while (History.CanUndo)
            {
                (State, History) = History.Undo(State);
            }

            return this;
        }

        public Session RedoAll()
        {
            while (History.CanRedo)
            {
                (State, History) = History.Redo(State);
            }

            return this;
        }
    }

    private static Session Run(int[] script, HistoryPolicy policy)
    {
        var session = new Session(policy);
        var tick = 0;

        foreach (var op in Ops(script))
        {
            var tr = Build(session.State, op);

            if (tr is null)
            {
                continue;
            }

            // Times close enough together that the default policy actually groups some of them,
            // and far enough apart that it does not group all of them.
            tr.At(DateTimeOffset.UnixEpoch + TimeSpan.FromMilliseconds(tick * 200));
            tick++;

            session.Commit(tr);
        }

        return session;
    }

    private static IEnumerable<Op> Ops(int[] script)
    {
        ArgumentNullException.ThrowIfNull(script);

        for (var i = 0; i + 3 < script.Length; i += 4)
        {
            yield return new Op(Mod(script[i], 8), script[i + 1], script[i + 2], script[i + 3]);
        }
    }

    /// <summary>
    /// Turns a generated op into a transaction, or null if it does not amount to a change.
    /// </summary>
    /// <remarks>
    /// Positions are folded into range rather than rejected, so that shrinking a counterexample
    /// never turns it into a script the interpreter refuses.
    /// </remarks>
    private static Transaction? Build(EditorState state, Op op)
    {
        var size = state.Doc.ContentSize;
        var tr = state.Transaction();
        var a = Mod(op.A, size + 1);
        var b = Mod(op.B, size + 1);
        var from = Math.Min(a, b);
        var to = Math.Max(a, b);

        switch (op.Kind)
        {
            case 0:
                tr.As(TransactionKind.Typing).InsertText(a, Word(op.C, 1));
                break;

            case 1:
                tr.As(TransactionKind.Typing).InsertText(a, Word(op.C, 4));
                break;

            case 2:
                tr.As(TransactionKind.Delete).Delete(from, to);
                break;

            case 3:
                tr.As(TransactionKind.Paste)
                    .Delete(from, to)
                    .InsertText(from, Word(op.C, 3));
                break;

            case 4:
                tr.As(TransactionKind.Format).Step(Mod(op.C, 2) == 0
                    ? new AddMarkStep(from, to, MarkAt(op.C), FieldAt(op.C))
                    : new RemoveMarkStep(from, to, FieldAt(op.C)));
                break;

            case 5:
                var blocks = BlockPositions(state.Doc);
                var pos = blocks[Mod(op.A, blocks.Count)];

                tr.As(TransactionKind.Structure)
                    .Step(new SetBlockAttrStep(pos, AttrAt(op.C), AttrValue(op.C)));
                break;

            case 6:
                // Enter: an open slice that splits whatever paragraph it lands in. The one
                // structural edit a reader makes constantly, and the one whose size arithmetic
                // depends on open depths rather than on node sizes alone.
                tr.As(TransactionKind.Structure).Replace(from, to, Slice.ParagraphSplit);
                break;

            case 7:
                // Whole blocks arriving closed, which is what a paste of several paragraphs is,
                // and an embed, which is one position holding something that is not text. Closed
                // blocks only fit where a block fits, so that half aims at block boundaries; the
                // embed is inline and goes wherever the generator points it.
                var into = BlockPositions(state.Doc);
                var edge = into[Mod(op.A, into.Count)];

                tr.As(TransactionKind.Paste).Replace(
                    Mod(op.C, 3) == 2 ? from : edge,
                    Mod(op.C, 3) == 2 ? to : edge,
                    Mod(op.C, 3) switch
                    {
                        0 => Slice.OfBlocks(
                            ParagraphNode.FromText(Word(op.C, 3)),
                            ParagraphNode.FromText(Word(op.C + 1, 3))),
                        1 => Slice.OfBlocks(
                            new ListNode([new ListItemNode([ParagraphNode.FromText(Word(op.C, 2))])])),
                        _ => Slice.OfInline(
                            InlineContent.FromEmbed(new ImageEmbed($"img{Mod(op.C, 3)}.png"))),
                    });
                break;

            default:
                throw new InvalidOperationException($"unreachable kind {op.Kind}");
        }

        return tr.ChangedDoc ? tr : null;
    }

    /// <summary>Which block attribute a generated structure edit sets.</summary>
    private static BlockAttr AttrAt(int seed) => Mod(seed, 4) switch
    {
        0 => BlockAttr.Align,
        1 => BlockAttr.ParagraphKind,
        2 => BlockAttr.IndentLevel,
        _ => BlockAttr.ListKind,
    };

    private static int AttrValue(int seed) => AttrAt(seed) switch
    {
        BlockAttr.Align => Mod(seed, 5),
        BlockAttr.ParagraphKind => Mod(seed / 7, 5),
        BlockAttr.IndentLevel => Mod(seed / 7, 4),
        _ => Mod(seed / 7, 3),
    };

    private static MarkSet MarkAt(int seed) => Mod(seed, 4) switch
    {
        0 => new MarkSet { Style = TextStyle.Bold },
        1 => new MarkSet { Style = TextStyle.Italic },
        2 => new MarkSet { FontSize = 10 + Mod(seed, 8) },
        _ => new MarkSet { Foreground = Rgba.FromRgb((byte)Mod(seed, 255), 0, 0) },
    };

    private static MarkFields FieldAt(int seed) => Mod(seed, 4) switch
    {
        0 => MarkFields.Bold,
        1 => MarkFields.Italic,
        2 => MarkFields.FontSize,
        _ => MarkFields.Foreground,
    };

    private static string Word(int seed, int maxLength)
    {
        // Every fourth word is one of the awkward ones, so that the properties are exercised
        // against text where a code unit is not a character.
        if (Mod(seed, 4) == 3)
        {
            return Awkward[Mod(seed / 4, Awkward.Length)];
        }

        var length = 1 + Mod(seed, maxLength);
        var text = new StringBuilder(length);

        for (var i = 0; i < length; i++)
        {
            text.Append(Alphabet[Mod(seed / (i + 1) + i, Alphabet.Length)]);
        }

        return text.ToString();
    }

    /// <summary>Every position immediately before a block, at any depth.</summary>
    private static List<int> BlockPositions(DocumentNode doc)
    {
        var positions = new List<int>();

        Walk(doc, 0);

        return positions;

        void Walk(Node node, int contentStart)
        {
            var pos = contentStart;

            foreach (var child in node.Children)
            {
                if (!child.IsLeaf)
                {
                    positions.Add(pos);
                    Walk(child, pos + 1);
                }

                pos += child.NodeSize;
            }
        }
    }

    /// <summary>
    /// Where the marker is, found by walking the finished document. Uses no mapping code.
    /// </summary>
    private static int? FindMarker(Node node, int contentStart = 0)
    {
        if (node is ParagraphNode paragraph)
        {
            var index = paragraph.Content.Text.IndexOf(Marker, StringComparison.Ordinal);

            return index < 0 ? null : contentStart + index;
        }

        var pos = contentStart;

        foreach (var child in node.Children)
        {
            if (!child.IsLeaf && FindMarker(child, pos + 1) is { } found)
            {
                return found;
            }

            pos += child.NodeSize;
        }

        return null;
    }

    private static int Mod(int value, int modulus)
    {
        var result = value % modulus;

        return result < 0 ? result + modulus : result;
    }
}
