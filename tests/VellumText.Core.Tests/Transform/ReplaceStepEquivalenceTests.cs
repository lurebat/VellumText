namespace VellumText.Tests;

/// <summary>
/// Applying a step used to cut the document in two at the root, splice the slice into the left
/// half and splice the right half back on. It now finds the deepest ancestor whose child list
/// survives the edit unchanged in length, does the cutting and splicing inside that one child,
/// and rebuilds only the spine above it. That is a change of cost and must be no change of
/// answer at all.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ReplaceStep"/> is the primitive every edit goes through, so a disagreement here is
/// not a wrong number on a screen — it is a document quietly rearranged under the user. The old
/// algorithm is therefore the specification, and it is written out below rather than called: an
/// equivalence test that invokes the implementation twice proves nothing at all.
/// </para>
/// <para>
/// The reference is built from the public model API only, which is what a second implementation
/// would have had to use. That is a feature: it shares no cutting, splicing or index arithmetic
/// with the code under test, so an off-by-one in the new spine walk cannot be mirrored into the
/// oracle and cancel out.
/// </para>
/// <para>
/// Every range of every shape is tried, against several slices. The interesting positions are
/// exactly the ones a hand-written case list forgets — the boundary between two blocks, position
/// zero, <c>ContentSize</c>, the seam between two table cells, and a range spanning a list item
/// into the paragraph after the list. The pivot the new path computes is a different depth for
/// each of those, and a wrong one shows up as an answer that differs from the reference rather
/// than as a crash.
/// </para>
/// <para>
/// What is compared is whether the step succeeded and, if it did, the document it produced.
/// Failure <em>wording</em> is deliberately not compared: where the pivot lands at the depth the
/// slice's own content arrives at, the old path rejects illegal content through the node
/// constructor ("Expected BlockNode but found …") while the new one rejects it at the seam ("The
/// slice cannot be joined …"). Both refuse the same edits; only the sentence differs.
/// </para>
/// </remarks>
public class ReplaceStepEquivalenceTests
{
    public static TheoryData<string, string> EveryShapeAndSlice()
    {
        var data = new TheoryData<string, string>();

        foreach (var shape in Shapes.Keys)
        {
            foreach (var slice in Slices.Keys)
            {
                data.Add(shape, slice);
            }
        }

        return data;
    }

    public static TheoryData<string> EveryShape()
    {
        var data = new TheoryData<string>();

        foreach (var shape in Shapes.Keys)
        {
            data.Add(shape);
        }

        return data;
    }

    private static Dictionary<string, DocumentNode> Shapes { get; } = new()
    {
        ["one empty paragraph"] = DocumentNode.Empty,
        ["flat paragraphs"] = new DocumentNode(
        [
            ParagraphNode.FromText("first"),
            ParagraphNode.FromText("second"),
            ParagraphNode.FromText("third"),
        ]),
        ["a heading above paragraphs"] = new DocumentNode(
        [
            new ParagraphNode(InlineContent.FromText("title"), ParagraphKind.Heading1),
            ParagraphNode.FromText("body"),
            ParagraphNode.FromText("more"),
        ]),
        ["a rule between paragraphs"] = new DocumentNode(
        [
            ParagraphNode.FromText("above"),
            RuleNode.Instance,
            ParagraphNode.FromText("below"),
        ]),
        ["a list"] = new DocumentNode(
        [
            new ListNode([ListItemNode.FromText("one"), ListItemNode.FromText("two")]),
        ]),
        ["a list between paragraphs"] = new DocumentNode(
        [
            ParagraphNode.FromText("before"),
            new ListNode([ListItemNode.FromText("one"), ListItemNode.FromText("two")]),
            ParagraphNode.FromText("after"),
        ]),
        ["a nested list"] = new DocumentNode(
        [
            new ListNode(
            [
                ListItemNode.FromText("outer"),
                new ListItemNode(
                [
                    ParagraphNode.FromText("holder"),
                    new ListNode([ListItemNode.FromText("inner")]),
                ]),
            ]),
        ]),
        ["a two-by-two table"] = new DocumentNode([Table(rows: 2, columns: 2)]),
        ["a table with one row"] = new DocumentNode([Table(rows: 1, columns: 2)]),
        ["a table between paragraphs"] = new DocumentNode(
        [
            ParagraphNode.FromText("before"),
            Table(rows: 2, columns: 2),
            ParagraphNode.FromText("after"),
        ]),
        ["a table inside a list"] = new DocumentNode(
        [
            new ListNode(
            [
                new ListItemNode([ParagraphNode.FromText("holder"), Table(rows: 2, columns: 2)]),
                ListItemNode.FromText("after"),
            ]),
        ]),
        ["a multi-block list item"] = new DocumentNode(
        [
            new ListNode(
            [
                new ListItemNode(
                [
                    ParagraphNode.FromText("head"),
                    ParagraphNode.FromText("tail"),
                ]),
                ListItemNode.FromText("next"),
            ]),
        ]),
        ["the nested sample"] = Sample.Nested,
    };

    private static Dictionary<string, Slice> Slices { get; } = new()
    {
        // A deletion. The slice supplies nothing, so the step has to merge every level itself.
        ["nothing"] = Slice.Empty,

        // Typing: one inline node, open at both ends. This is the shape the fast path exists for.
        ["one character"] = Slice.OfText("x"),

        // Longer text, still one node — the pivot is the same but the sizes are not.
        ["several characters"] = Slice.OfText("inserted"),

        // Enter. Two nodes, so the container's child list genuinely grows and the pivot must
        // refuse to sit at the depth those two arrive at.
        ["a paragraph split"] = Slice.ParagraphSplit,

        // A closed block. It cannot go anywhere a paragraph's content can, so most ranges reject
        // it — and both paths have to reject exactly the same ones.
        ["a rule"] = Slice.OfBlocks(RuleNode.Instance),

        // Two closed blocks at once.
        ["two paragraphs"] = Slice.OfBlocks(
            ParagraphNode.FromText("alpha"),
            ParagraphNode.FromText("beta")),

        // A closed list. Legal at the top level, illegal nearly everywhere else.
        ["a list"] = Slice.OfBlocks(new ListNode([ListItemNode.FromText("pasted")])),
    };

    [Theory]
    [MemberData(nameof(EveryShapeAndSlice))]
    public void EveryRangeProducesWhatTheOldCutAndSpliceProduced(string shape, string slice)
    {
        var doc = Shapes[shape];
        var content = Slices[slice];

        for (var from = 0; from <= doc.ContentSize; from++)
        {
            for (var to = from; to <= doc.ContentSize; to++)
            {
                AssertSame(doc, new ReplaceStep(from, to, content));
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void CuttingARangeOutAndPuttingItBackAgreesWithTheOldPath(string shape)
    {
        // The slice a range produces is open to exactly the depth that range reaches, which is
        // the case the fixed slices above cannot reach: a range spanning a list boundary yields
        // a slice open one level on one side and two on the other. Putting it back is an
        // identity, and both paths must agree that it is.
        var doc = Shapes[shape];

        for (var from = 0; from <= doc.ContentSize; from++)
        {
            for (var to = from; to <= doc.ContentSize; to++)
            {
                AssertSame(doc, new ReplaceStep(from, to, Slice.Cut(doc, from, to)));
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void ASecondEditOnTopOfTheFirstStillAgrees(string shape)
    {
        // One edit rebuilds the spine and leaves every other node shared. The next edit resolves
        // positions into that half-new tree, which is where a spine rebuilt at the wrong index
        // would first be visible: the document is still valid, just no longer the one it should
        // be. Applying two in a row is what the editor actually does.
        var doc = Shapes[shape];

        for (var from = 0; from <= doc.ContentSize; from++)
        {
            var first = new ReplaceStep(from, from, Slice.OfText("a")).Apply(doc);

            if (!first.IsOk)
            {
                continue;
            }

            var once = first.Doc!;

            for (var to = 0; to <= once.ContentSize; to++)
            {
                AssertSame(once, new ReplaceStep(to, to, Slice.OfText("b")));
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryShape))]
    public void EveryShapeIsSchemaValid(string shape)
    {
        // Agreement about a document the schema would reject would be agreement about a shape
        // that can never reach a step.
        Assert.Empty(DocumentSchema.Validate(Shapes[shape]));
    }

    private static void AssertSame(DocumentNode doc, ReplaceStep step)
    {
        var expected = Reference.Apply(doc, step);
        var actual = step.Apply(doc);

        Assert.True(
            expected.IsOk == actual.IsOk,
            $"[{step.From}, {step.To}) disagreed: reference {expected}, implementation {actual}.");

        if (expected.IsOk)
        {
            Assert.True(
                expected.Doc!.Equals(actual.Doc),
                $"[{step.From}, {step.To}) produced a different document.");
        }
    }

    private static TableNode Table(int rows, int columns) =>
        new(Enumerable
            .Range(0, rows)
            .Select(r => new TableRowNode(Enumerable
                .Range(0, columns)
                .Select(c => TableCellNode.FromText($"r{r}c{c}")))));

    /// <summary>
    /// Applying a step exactly as it was before there was a pivot: cut the whole document before
    /// the range, cut the whole document after it, wrap the slice up to the root, and splice the
    /// three together.
    /// </summary>
    /// <remarks>
    /// A deliberate copy rather than a call, and written against the public model API rather than
    /// against <c>TreeSurgery</c>. This is the thing being compared with, so it has to be able to
    /// disagree — sharing the cutting or the splicing with the implementation would make the test
    /// pass for any pivot, correct or not.
    /// </remarks>
    private static class Reference
    {
        internal static StepResult Apply(DocumentNode doc, ReplaceStep step)
        {
            if (step.From < 0 || step.To < step.From || step.To > doc.ContentSize)
            {
                return StepResult.Failed("Range is not inside the document.");
            }

            var start = doc.Resolve(step.From);
            var end = doc.Resolve(step.To);

            if (step.Content.OpenStart > start.Depth || step.Content.OpenEnd > end.Depth)
            {
                return StepResult.Failed("The slice is open deeper than the position it goes into.");
            }

            var extra = start.Depth - step.Content.OpenStart;

            if (extra != end.Depth - step.Content.OpenEnd)
            {
                return StepResult.Failed("Inconsistent open depths.");
            }

            if (TakesATableApart(start, end, extra))
            {
                return StepResult.Failed("The range would join two parts of a table together.");
            }

            try
            {
                return Rebuild(doc, start, end, extra, step.Content);
            }
            catch (ArgumentException ex)
            {
                return StepResult.Failed(ex.Message);
            }
        }

        private static bool TakesATableApart(ResolvedPos start, ResolvedPos end, int extra)
        {
            for (var depth = start.SharedDepth(end) + 1; depth <= extra; depth++)
            {
                if (start.NodeAt(depth) is TableNode or TableRowNode or TableCellNode
                    || end.NodeAt(depth) is TableNode or TableRowNode or TableCellNode)
                {
                    return true;
                }
            }

            return false;
        }

        private static StepResult Rebuild(
            DocumentNode doc, ResolvedPos start, ResolvedPos end, int extra, Slice content)
        {
            var left = CutBefore(start, 0);
            var right = CutAfter(end, 0);

            IReadOnlyList<Node> middle = content.Content;

            for (var depth = extra; depth >= 1; depth--)
            {
                if (start.NodeAt(depth) is ParagraphNode paragraph)
                {
                    if (middle.Count > 0)
                    {
                        return StepResult.Failed("Cannot put block nodes inside a paragraph.");
                    }

                    middle = [paragraph.WithContent(InlineContent.Empty)];
                }
                else
                {
                    middle = [start.NodeAt(depth).WithChildren(middle)];
                }
            }

            if (Splice(left, doc.WithChildren(middle), start.Depth) is not { } withMiddle)
            {
                return StepResult.Failed("The slice cannot be joined to the content before it.");
            }

            if (Splice(withMiddle, right, end.Depth) is not DocumentNode result)
            {
                return StepResult.Failed("The slice cannot be joined to the content after it.");
            }

            var violations = DocumentSchema.Validate(result, doc);

            return violations.IsEmpty
                ? StepResult.Ok(result)
                : StepResult.Failed($"The result is not a valid document: {violations[0]}.");
        }

        private static Node CutBefore(ResolvedPos at, int depth)
        {
            var node = at.NodeAt(depth);

            if (depth == at.Depth)
            {
                return node is ParagraphNode paragraph
                    ? paragraph.WithContent(paragraph.Content.Substring(0, at.ParentOffset))
                    : node.WithChildren(node.Children.Take(at.ParentIndex).ToList());
            }

            var kept = node.Children.Take(at.IndexAt(depth)).ToList();
            kept.Add(CutBefore(at, depth + 1));

            return node.WithChildren(kept);
        }

        private static Node CutAfter(ResolvedPos at, int depth)
        {
            var node = at.NodeAt(depth);

            if (depth == at.Depth)
            {
                return node is ParagraphNode paragraph
                    ? paragraph.WithContent(
                        paragraph.Content.Substring(
                            at.ParentOffset,
                            paragraph.Content.Length - at.ParentOffset))
                    : node.WithChildren(node.Children.Skip(at.ParentIndex).ToList());
            }

            var kept = new List<Node> { CutAfter(at, depth + 1) };
            kept.AddRange(node.Children.Skip(at.IndexAt(depth) + 1));

            return node.WithChildren(kept);
        }

        private static Node? Splice(Node left, Node right, int depth)
        {
            var leftChildren = left.Children;
            var rightChildren = right.Children;

            if (depth <= 0 || leftChildren.Length == 0 || rightChildren.Length == 0)
            {
                return left.WithChildren(leftChildren.AddRange(rightChildren));
            }

            var seamLeft = leftChildren[^1];
            var seamRight = rightChildren[0];
            Node? merged;

            if (seamLeft is ParagraphNode before && seamRight is ParagraphNode after)
            {
                merged = before.WithContent(before.Content.Concat(after.Content));
            }
            else if (seamLeft.GetType() == seamRight.GetType() && !seamLeft.IsLeaf)
            {
                merged = Splice(seamLeft, seamRight, depth - 1);
            }
            else
            {
                return null;
            }

            if (merged is null)
            {
                return null;
            }

            var joined = leftChildren.Take(leftChildren.Length - 1).ToList();
            joined.Add(merged);
            joined.AddRange(rightChildren.Skip(1));

            return left.WithChildren(joined);
        }
    }
}
