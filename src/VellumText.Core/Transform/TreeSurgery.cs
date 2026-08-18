namespace VellumText;

/// <summary>
/// Cutting a document apart and putting it back together.
/// </summary>
/// <remarks>
/// <para>
/// Every replacement reduces to the same three moves: keep everything before the range, keep
/// everything after it, and splice the new content between them. What makes it subtle is that
/// both cuts run down a spine of ancestors, and putting the halves back together has to rejoin
/// that spine level by level or the document gains boundaries that were never there.
/// </para>
/// <para>
/// A join that is asked for and cannot be made is a failure, not something to paper over by
/// concatenating instead. Concatenating would silently change the document's size, and every
/// stale position in the editor is rewritten using a size the step predicted in advance.
/// </para>
/// </remarks>
/// <summary>
/// Rewrites one paragraph that a range touches.
/// </summary>
/// <param name="paragraph">The paragraph to rewrite.</param>
/// <param name="contentStart">Where its text starts, in document coordinates.</param>
/// <param name="start">Where the range starts within its text.</param>
/// <param name="end">Where the range ends within its text.</param>
internal delegate ParagraphNode ParagraphRewrite(
    ParagraphNode paragraph,
    int contentStart,
    int start,
    int end);

internal static class TreeSurgery
{
    /// <summary>
    /// Rebuilds the node at <paramref name="depth"/> keeping only what lies before the position.
    /// </summary>
    /// <param name="at">The position to cut at.</param>
    /// <param name="depth">The depth to rebuild from.</param>
    public static Node CutBefore(ResolvedPos at, int depth)
    {
        var node = at.NodeAt(depth);

        if (depth == at.Depth)
        {
            return node is ParagraphNode paragraph
                ? paragraph.WithContent(paragraph.Content.Substring(0, at.ParentOffset))
                : node.WithChildren(Slice(node.Children, 0, at.ParentIndex));
        }

        var index = at.IndexAt(depth);

        // Hoisted: Children is IReadOnlyList<Node> over an ImmutableArray, so every read of the
        // property boxes, and this loop read it once per child.
        var children = node.Children;
        var kept = new List<Node>(index + 1);

        for (var i = 0; i < index; i++)
        {
            kept.Add(children[i]);
        }

        kept.Add(CutBefore(at, depth + 1));

        return node.WithChildren(kept);
    }

    /// <summary>
    /// Rebuilds the node at <paramref name="depth"/> keeping only what lies after the position.
    /// </summary>
    /// <param name="at">The position to cut at.</param>
    /// <param name="depth">The depth to rebuild from.</param>
    public static Node CutAfter(ResolvedPos at, int depth)
    {
        var node = at.NodeAt(depth);

        if (depth == at.Depth)
        {
            return node is ParagraphNode paragraph
                ? paragraph.WithContent(
                    paragraph.Content.Substring(
                        at.ParentOffset,
                        paragraph.Content.Length - at.ParentOffset))
                : node.WithChildren(Slice(node.Children, at.ParentIndex, node.Children.Count - at.ParentIndex));
        }

        var index = at.IndexAt(depth);

        // Hoisted for the same reason as CutBefore, and sized up front: this loop kept the tail
        // of the document after an edit, so on a large document it was two boxed property reads
        // per child and a list doubling its way up to the final count.
        var children = node.Children;
        var kept = new List<Node>(children.Count - index) { CutAfter(at, depth + 1) };

        for (var i = index + 1; i < children.Count; i++)
        {
            kept.Add(children[i]);
        }

        return node.WithChildren(kept);
    }

    /// <summary>
    /// Joins two halves of the same node back together, merging <paramref name="depth"/>
    /// levels of their touching children.
    /// </summary>
    /// <param name="left">The half that comes first.</param>
    /// <param name="right">The half that comes second.</param>
    /// <param name="depth">How many levels of the seam must be merged rather than abutted.</param>
    /// <returns>The joined node, or null if a required merge is impossible.</returns>
    public static Node? Splice(Node left, Node right, int depth)
    {
        var leftChildren = left.Children;
        var rightChildren = right.Children;

        if (depth <= 0 || leftChildren.Count == 0 || rightChildren.Count == 0)
        {
            return left.WithChildren(Concat(leftChildren, rightChildren));
        }

        var seamLeft = leftChildren[^1];
        var seamRight = rightChildren[0];
        Node? merged;

        if (seamLeft is ParagraphNode before && seamRight is ParagraphNode after)
        {
            // The deepest seam there is. The leading paragraph's own attributes win, which is
            // what makes deleting from a heading into the paragraph below keep the heading.
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

        var joined = new List<Node>(leftChildren.Count + rightChildren.Count - 1);

        for (var i = 0; i < leftChildren.Count - 1; i++)
        {
            joined.Add(leftChildren[i]);
        }

        joined.Add(merged);

        for (var i = 1; i < rightChildren.Count; i++)
        {
            joined.Add(rightChildren[i]);
        }

        return left.WithChildren(joined);
    }

    /// <summary>
    /// Rewrites every paragraph the range touches, leaving the rest of the tree shared.
    /// </summary>
    /// <param name="doc">The document to rewrite.</param>
    /// <param name="from">Where the range starts.</param>
    /// <param name="to">Where it ends.</param>
    /// <param name="rewrite">
    /// Receives a paragraph, where its text starts in document coordinates, and the part of it
    /// the range covers, and returns its replacement.
    /// </param>
    /// <remarks>
    /// Formatting never changes the shape of the tree, so this walks it rather than cutting and
    /// splicing. Untouched subtrees are returned by reference, which is what keeps a bold command
    /// over one word from rebuilding a hundred-paragraph document.
    /// </remarks>
    internal static DocumentNode MapParagraphs(
        DocumentNode doc,
        int from,
        int to,
        ParagraphRewrite rewrite) =>
        (DocumentNode)Walk(doc, 0, from, to, rewrite);

    private static Node Walk(
        Node node,
        int contentStart,
        int from,
        int to,
        ParagraphRewrite rewrite)
    {
        if (node is ParagraphNode paragraph)
        {
            var start = Math.Max(from - contentStart, 0);
            var end = Math.Min(to - contentStart, paragraph.Content.Length);

            return end > start ? rewrite(paragraph, contentStart, start, end) : node;
        }

        if (node.IsLeaf)
        {
            return node;
        }

        List<Node>? children = null;
        var pos = contentStart;

        // Hoisted, and this is the whole cost of walking a large document. Children is declared
        // as IReadOnlyList<Node> and implemented by an ImmutableArray, so every read of the
        // property boxes the struct — and it was read three times per child, once in the loop
        // condition and twice in the body. On a 50,000-block document that is 150,000 boxes for
        // a walk that is otherwise almost free.
        var all = node.Children;

        for (var i = 0; i < all.Count; i++)
        {
            var child = all[i];
            var childContentStart = pos + 1;
            var childContentEnd = childContentStart + child.ContentSize;

            // A child matters only if the range reaches inside it. Touching its boundary is not
            // enough: marks live on characters, and the boundary belongs to this node.
            if (!child.IsLeaf && from < childContentEnd && to > childContentStart)
            {
                var replacement = Walk(child, childContentStart, from, to, rewrite);

                if (!ReferenceEquals(replacement, child))
                {
                    children ??= [.. all];
                    children[i] = replacement;
                }
            }

            pos += child.NodeSize;
        }

        return children is null ? node : node.WithChildren(children);
    }

    /// <summary>
    /// Replaces the node immediately after a position, rebuilding only its ancestors.
    /// </summary>
    /// <param name="at">A position directly before the node to replace.</param>
    /// <param name="replacement">What to put there.</param>
    internal static DocumentNode ReplaceNodeAfter(ResolvedPos at, Node replacement)
    {
        var rebuilt = replacement;

        for (var depth = at.Depth; depth >= 0; depth--)
        {
            var parent = at.NodeAt(depth);
            var children = new List<Node>(parent.Children);
            children[at.IndexAt(depth)] = rebuilt;
            rebuilt = parent.WithChildren(children);
        }

        return (DocumentNode)rebuilt;
    }

    /// <summary>A run of children, copied into an array of exactly the right size.</summary>
    /// <remarks>
    /// These replaced <c>[.. children.Take(n)]</c> and <c>[.. children.Skip(n)]</c>. A collection
    /// expression over a LINQ sequence cannot know how many items are coming, so it grows a
    /// buffer by doubling and copies as it goes — roughly twice the final size in allocation, on
    /// a list that is the whole document. Splitting a 50,000-block document happens on every
    /// keystroke, and the count is known before the copy starts.
    /// </remarks>
    private static Node[] Slice(IReadOnlyList<Node> children, int start, int count)
    {
        var slice = new Node[count];

        for (var i = 0; i < count; i++)
        {
            slice[i] = children[start + i];
        }

        return slice;
    }

    /// <summary>Two runs of children, joined into an array of exactly the right size.</summary>
    private static Node[] Concat(IReadOnlyList<Node> left, IReadOnlyList<Node> right)
    {
        var joined = new Node[left.Count + right.Count];

        for (var i = 0; i < left.Count; i++)
        {
            joined[i] = left[i];
        }

        for (var i = 0; i < right.Count; i++)
        {
            joined[left.Count + i] = right[i];
        }

        return joined;
    }
}
